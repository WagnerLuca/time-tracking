using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

public class PdfReportService : IPdfReportService
{
    private readonly TimeTrackingDbContext _context;
    private readonly ILogger<PdfReportService> _logger;

    private static readonly string[] MonthNames =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    ];

    public PdfReportService(TimeTrackingDbContext context, ILogger<PdfReportService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ServiceResult<byte[]>> GenerateMonthlyReportAsync(
        string slug, int callerUserId, int targetUserId, int year, int month)
    {
        if (month is < 1 or > 12)
            return ServiceResult.BadRequest<byte[]>("Month must be between 1 and 12.");

        var context = await ResolveReportContextAsync(slug, callerUserId, targetUserId);
        if (context.Error != null) return ServiceResult.Fail<byte[]>(context.Error.Value, context.ErrorMessage!);

        var dateFrom = new DateOnly(year, month, 1);
        var dateTo = dateFrom.AddMonths(1).AddDays(-1);

        var (rows, overallCumulative) = await BuildDisplayRowsAsync(
            _context, targetUserId, context.Org!.Id, dateFrom, dateTo, context.Membership!.InitialOvertimeHours);

        var period = $"{MonthNames[month - 1]} {year}";
        var bytes = RenderMonthlyPdf(context.Org.Name, context.TargetUser!, period, rows, overallCumulative);

        _logger.LogInformation(
            "Monthly PDF report generated for user {TargetUserId} in org {OrgSlug} ({Year}-{Month}) by user {CallerId}",
            targetUserId, slug, year, month, callerUserId);

        return ServiceResult.Ok(bytes);
    }

    public async Task<ServiceResult<byte[]>> GenerateYearlyReportAsync(
        string slug, int callerUserId, int targetUserId, int year)
    {
        var context = await ResolveReportContextAsync(slug, callerUserId, targetUserId);
        if (context.Error != null) return ServiceResult.Fail<byte[]>(context.Error.Value, context.ErrorMessage!);

        var dateFrom = new DateOnly(year, 1, 1);
        var dateTo = new DateOnly(year, 12, 31);

        var (rows, overallCumulative) = await BuildDisplayRowsAsync(
            _context, targetUserId, context.Org!.Id, dateFrom, dateTo, context.Membership!.InitialOvertimeHours);

        var monthSummaries = rows
            .GroupBy(r => r.Date.Month)
            .OrderBy(g => g.Key)
            .Select(g => new MonthSummary(
                g.Key,
                TargetHours: g.Sum(r => r.TargetHours),
                WorkedHours: g.Sum(r => r.WorkedHours),
                NetWorkedHours: g.Sum(r => r.NetWorkedHours),
                Overtime: g.Sum(r => r.Overtime),
                CumulativeOvertimeAtEnd: g.Last().CumulativeOvertime,
                WorkedDays: g.Count(r => r.Status == "Worked"),
                MissingDays: g.Count(r => r.Status == "Missing"),
                VacationDays: g.Count(r => r.CountsAsVacation),
                SickDays: g.Count(r => r.AbsenceType == AbsenceType.SickDay),
                HolidayDays: g.Count(r => r.HolidayName != null)
            ))
            .ToList();

        var bytes = RenderYearlyPdf(context.Org.Name, context.TargetUser!, year, monthSummaries, overallCumulative);

        _logger.LogInformation(
            "Yearly PDF report generated for user {TargetUserId} in org {OrgSlug} ({Year}) by user {CallerId}",
            targetUserId, slug, year, callerUserId);

        return ServiceResult.Ok(bytes);
    }

    // ────────────────────────────────────────────────────
    //  Shared context resolution (org, membership, permission)
    // ────────────────────────────────────────────────────

    private record ReportContext(
        Organization? Org, UserOrganization? Membership, User? TargetUser,
        ServiceErrorType? Error, string? ErrorMessage);

    private async Task<ReportContext> ResolveReportContextAsync(string slug, int callerUserId, int targetUserId)
    {
        var org = await _context.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Slug == slug);
        if (org == null)
            return new ReportContext(null, null, null, ServiceErrorType.NotFound, "Organization not found");

        var membership = await _context.UserOrganizations.AsNoTracking()
            .Include(uo => uo.User)
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == targetUserId);
        if (membership == null)
            return new ReportContext(null, null, null, ServiceErrorType.NotFound, "Member not found in this organization");

        var allowed = await EffectiveRuleResolver.CanExportDataForMemberAsync(_context, org.Id, callerUserId, targetUserId);
        if (!allowed)
            return new ReportContext(null, null, null, ServiceErrorType.Forbidden,
                "You do not have permission to export this member's report.");

        return new ReportContext(org, membership, membership.User, null, null);
    }

    /// <summary>
    /// Builds the report's display rows (clipped to [dateFrom, dateTo]) whose
    /// <see cref="DailyReportRow.CumulativeOvertime"/> reflects the member's true,
    /// never-reset running balance since their very first tracked entry — not just
    /// a total reset at the start of the requested period. Also returns that overall
    /// balance as of the end of the period (or "today" if the period is still ongoing,
    /// since <see cref="DailyReportAggregator.BuildAsync"/> clamps internally).
    /// </summary>
    private static async Task<(List<DailyReportRow> Rows, double OverallCumulative)> BuildDisplayRowsAsync(
        TimeTrackingDbContext context, int userId, int orgId,
        DateOnly dateFrom, DateOnly dateTo, double initialOvertimeHours)
    {
        var firstEntry = await context.TimeEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.OrganizationId == orgId && !e.IsRunning)
            .OrderBy(e => e.StartTime)
            .Select(e => (DateTime?)e.StartTime)
            .FirstOrDefaultAsync();

        var loopFrom = dateFrom;
        if (firstEntry != null)
        {
            var firstEntryDate = DateOnly.FromDateTime(firstEntry.Value);
            if (firstEntryDate < loopFrom) loopFrom = firstEntryDate;
        }

        var allRows = await DailyReportAggregator.BuildAsync(context, userId, orgId, loopFrom, dateTo, initialOvertimeHours);
        var displayRows = allRows.Where(r => r.Date >= dateFrom).ToList();
        var overallCumulative = allRows.Count > 0 ? allRows[^1].CumulativeOvertime : initialOvertimeHours;

        return (displayRows, overallCumulative);
    }

    // ────────────────────────────────────────────────────
    //  PDF rendering
    // ────────────────────────────────────────────────────

    private static byte[] RenderMonthlyPdf(string orgName, User member, string period, List<DailyReportRow> rows, double overallCumulative)
    {
        var totalTarget = rows.Sum(r => r.TargetHours);
        var totalNet = rows.Sum(r => r.NetWorkedHours);
        var totalOvertime = rows.Sum(r => r.Overtime);
        var vacationDays = rows.Count(r => r.AbsenceType == AbsenceType.Vacation);
        var sickDays = rows.Count(r => r.AbsenceType == AbsenceType.SickDay);
        var holidayDays = rows.Count(r => r.HolidayName != null);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Element(c => ComposeHeader(c, orgName, member, "Monthly Time Report", period));

                page.Content().PaddingTop(10).Column(column =>
                {
                    column.Spacing(12);

                    column.Item().Element(c => ComposeSummaryCards(c, [
                        ("Target", FormatHours(totalTarget)),
                        ("Worked (net)", FormatHours(totalNet)),
                        ("Balance", FormatSigned(totalOvertime)),
                        ("Overall Cumulative", FormatSigned(overallCumulative))
                    ]));
                    column.Item().Element(c => ComposeSummaryCards(c, [
                        ("Vacation", $"{vacationDays}d"),
                        ("Sick", $"{sickDays}d"),
                        ("Holidays", $"{holidayDays}d")
                    ]));

                    column.Item().Element(c => ComposeDailyTable(c, rows));
                });

                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf();
    }

    private static byte[] RenderYearlyPdf(string orgName, User member, int year, List<MonthSummary> months, double overallCumulative)
    {
        var totalTarget = months.Sum(m => m.TargetHours);
        var totalNet = months.Sum(m => m.NetWorkedHours);
        var totalOvertime = months.Sum(m => m.Overtime);
        var vacationDays = months.Sum(m => m.VacationDays);
        var sickDays = months.Sum(m => m.SickDays);
        var holidayDays = months.Sum(m => m.HolidayDays);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Element(c => ComposeHeader(c, orgName, member, "Yearly Time Report", year.ToString()));

                page.Content().PaddingTop(10).Column(column =>
                {
                    column.Spacing(12);

                    column.Item().Element(c => ComposeSummaryCards(c, [
                        ("Target", FormatHours(totalTarget)),
                        ("Worked (net)", FormatHours(totalNet)),
                        ("Year Balance", FormatSigned(totalOvertime)),
                        ("Overall Cumulative", FormatSigned(overallCumulative))
                    ]));
                    column.Item().Element(c => ComposeSummaryCards(c, [
                        ("Vacation", $"{vacationDays}d"),
                        ("Sick", $"{sickDays}d"),
                        ("Holidays", $"{holidayDays}d")
                    ]));

                    column.Item().Element(c => ComposeMonthlyTable(c, months));
                });

                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(QuestPDF.Infrastructure.IContainer container, string orgName, User member, string title, string period)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(inner =>
                {
                    inner.Item().Text(title).FontSize(16).SemiBold();
                    inner.Item().Text($"{member.FirstName} {member.LastName}").FontSize(11);
                    inner.Item().Text(member.Email).FontSize(9).FontColor(Colors.Grey.Darken1);
                });
                row.ConstantItem(160).AlignRight().Column(inner =>
                {
                    inner.Item().AlignRight().Text(orgName).FontSize(10).SemiBold();
                    inner.Item().AlignRight().Text(period).FontSize(10).FontColor(Colors.Grey.Darken1);
                    inner.Item().AlignRight().Text($"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").FontSize(7).FontColor(Colors.Grey.Medium);
                });
            });
            column.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
        });
    }

    private static void ComposeSummaryCards(QuestPDF.Infrastructure.IContainer container, (string Label, string Value)[] cards)
    {
        container.Row(row =>
        {
            foreach (var (label, value) in cards)
            {
                row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Column(col =>
                {
                    col.Item().AlignCenter().Text(value).FontSize(12).SemiBold();
                    col.Item().AlignCenter().Text(label).FontSize(7).FontColor(Colors.Grey.Darken1);
                });
            }
        });
    }

    private static void ComposeDailyTable(QuestPDF.Infrastructure.IContainer container, List<DailyReportRow> rows)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(60);  // Date
                columns.ConstantColumn(30);  // Day
                columns.ConstantColumn(45);  // Target
                columns.ConstantColumn(45);  // Worked
                columns.ConstantColumn(45);  // Net
                columns.ConstantColumn(45);  // Overtime
                columns.ConstantColumn(55);  // Cumulative
                columns.ConstantColumn(55);  // Status
                columns.RelativeColumn();    // Note / entries
            });

            table.Header(header =>
            {
                foreach (var text in new[] { "Date", "Day", "Target", "Worked", "Net", "Overtime", "Cumul.", "Status", "Note" })
                    header.Cell().Element(HeaderCell).Text(text);
            });

            var rowIndex = 0;
            foreach (var row in rows)
            {
                var isWeekendOrOff = row.Status is "Weekend" or "Day Off";
                var background = rowIndex % 2 == 1 ? Colors.Grey.Lighten4 : Colors.White;
                rowIndex++;

                table.Cell().Element(c => BodyCell(c, background)).Text(row.Date.ToString("yyyy-MM-dd"));
                table.Cell().Element(c => BodyCell(c, background)).Text(row.DayOfWeek.ToString()[..3]);
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(isWeekendOrOff ? "-" : FormatHours(row.TargetHours));
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(row.WorkedHours > 0 ? FormatHours(row.WorkedHours) : "-");
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(row.WorkedHours > 0 ? FormatHours(row.NetWorkedHours) : "-");
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(t =>
                {
                    if (isWeekendOrOff && row.WorkedHours == 0) { t.Span("-"); return; }
                    t.Span(FormatSigned(row.Overtime)).FontColor(row.Overtime < 0 ? Colors.Red.Darken1 : Colors.Green.Darken1);
                });
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(FormatSigned(row.CumulativeOvertime));
                table.Cell().Element(c => BodyCell(c, background)).Text(row.Status).FontColor(row.Status == "Needs Correction" ? Colors.Red.Darken1 : Colors.Black);
                table.Cell().Element(c => BodyCell(c, background)).Text(BuildNote(row)).FontSize(7.5f);
            }
        });
    }

    private static void ComposeMonthlyTable(QuestPDF.Infrastructure.IContainer container, List<MonthSummary> months)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(70);  // Month
                columns.RelativeColumn();    // Target
                columns.RelativeColumn();    // Worked
                columns.RelativeColumn();    // Overtime
                columns.RelativeColumn();    // Cumulative
                columns.ConstantColumn(45);  // Worked days
                columns.ConstantColumn(45);  // Missing
                columns.ConstantColumn(40);  // Vacation
                columns.ConstantColumn(35);  // Sick
                columns.ConstantColumn(45);  // Holidays
            });

            table.Header(header =>
            {
                foreach (var text in new[] { "Month", "Target", "Worked (net)", "Balance", "Cumul.", "Worked", "Missing", "Vac.", "Sick", "Holiday" })
                    header.Cell().Element(HeaderCell).Text(text);
            });

            var rowIndex = 0;
            foreach (var m in months)
            {
                var background = rowIndex % 2 == 1 ? Colors.Grey.Lighten4 : Colors.White;
                rowIndex++;

                table.Cell().Element(c => BodyCell(c, background)).Text(MonthNames[m.Month - 1]);
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(FormatHours(m.TargetHours));
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(FormatHours(m.NetWorkedHours));
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(t =>
                {
                    t.Span(FormatSigned(m.Overtime)).FontColor(m.Overtime < 0 ? Colors.Red.Darken1 : Colors.Green.Darken1);
                });
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(FormatSigned(m.CumulativeOvertimeAtEnd));
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(m.WorkedDays.ToString());
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(m.MissingDays.ToString());
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(m.VacationDays.ToString());
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(m.SickDays.ToString());
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(m.HolidayDays.ToString());
            }
        });
    }

    private static void ComposeFooter(QuestPDF.Infrastructure.IContainer container)
    {
        container.AlignCenter().Text(text =>
        {
            text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Medium));
            text.Span("Page ");
            text.CurrentPageNumber();
            text.Span(" / ");
            text.TotalPages();
        });
    }

    private static QuestPDF.Infrastructure.IContainer HeaderCell(QuestPDF.Infrastructure.IContainer container) =>
        container.Background(Colors.Grey.Darken2).Padding(4).DefaultTextStyle(x => x.FontColor(Colors.White).SemiBold().FontSize(8));

    private static QuestPDF.Infrastructure.IContainer BodyCell(QuestPDF.Infrastructure.IContainer container, string background) =>
        container.Background(background).Padding(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3);

    private static string BuildNote(DailyReportRow row)
    {
        if (row.HolidayName != null) return row.HolidayName;
        if (row.AbsenceType != null) return row.AbsenceNote ?? row.AbsenceType.Value.ToString();
        return row.EntrySummary;
    }

    private static string FormatHours(double hours) => $"{hours.ToString("F2", CultureInfo.InvariantCulture)}h";

    private static string FormatSigned(double hours) => $"{hours.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)}h";

    private record MonthSummary(
        int Month, double TargetHours, double WorkedHours, double NetWorkedHours,
        double Overtime, double CumulativeOvertimeAtEnd,
        int WorkedDays, int MissingDays, int VacationDays, int SickDays, int HolidayDays);
}
