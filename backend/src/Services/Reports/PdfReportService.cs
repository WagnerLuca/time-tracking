using TimeTracking.Api.Models.Dtos;
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

    public PdfReportService(TimeTrackingDbContext context, ILogger<PdfReportService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ServiceResult<byte[]>> GenerateMonthlyReportAsync(
        string slug, int callerUserId, int targetUserId, int year, int month, string? lang = null)
    {
        if (month is < 1 or > 12)
            return ServiceResult.BadRequest<byte[]>("Month must be between 1 and 12.");

        var context = await ResolveReportContextAsync(slug, callerUserId, targetUserId);
        if (context.Error != null) return ServiceResult.Fail<byte[]>(context.Error.Value, context.ErrorMessage!);

        var dateFrom = new DateOnly(year, month, 1);
        var dateTo = dateFrom.AddMonths(1).AddDays(-1);

        var (rows, overallCumulative) = await DailyReportAggregator.BuildWithRunningTotalAsync(
            _context, targetUserId, context.Org!.Id, dateFrom, dateTo, context.Membership!.InitialOvertimeHours);

        var t = ReportText.For(lang);
        var period = $"{t.Month(month)} {year}";
        var bytes = RenderMonthlyPdf(t, context.Org.Name, context.TargetUser!, period, rows, overallCumulative);

        _logger.LogInformation(
            "Monthly PDF report generated for user {TargetUserId} in org {OrgSlug} ({Year}-{Month}) by user {CallerId}",
            targetUserId, slug, year, month, callerUserId);

        return ServiceResult.Ok(bytes);
    }

    public async Task<ServiceResult<byte[]>> GenerateYearlyReportAsync(
        string slug, int callerUserId, int targetUserId, int year, string? lang = null)
    {
        var context = await ResolveReportContextAsync(slug, callerUserId, targetUserId);
        if (context.Error != null) return ServiceResult.Fail<byte[]>(context.Error.Value, context.ErrorMessage!);

        var dateFrom = new DateOnly(year, 1, 1);
        var dateTo = new DateOnly(year, 12, 31);

        var (rows, overallCumulative) = await DailyReportAggregator.BuildWithRunningTotalAsync(
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

        var bytes = RenderYearlyPdf(ReportText.For(lang), context.Org.Name, context.TargetUser!, year, monthSummaries, overallCumulative);

        _logger.LogInformation(
            "Yearly PDF report generated for user {TargetUserId} in org {OrgSlug} ({Year}) by user {CallerId}",
            targetUserId, slug, year, callerUserId);

        return ServiceResult.Ok(bytes);
    }

    // ────────────────────────────────────────────────────
    //  Shared context resolution (org, membership, permission)
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<BalanceResponse>> GetBalanceAsync(string slug, int callerUserId, int targetUserId)
    {
        var context = await ResolveReportContextAsync(slug, callerUserId, targetUserId);
        if (context.Error != null)
            return ServiceResult.Fail<BalanceResponse>(context.Error.Value, context.ErrorMessage!);

        var clock = OrgClock.For(context.Org);
        var today = clock.Today();
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var periodStart = weekStart < monthStart ? weekStart : monthStart;

        var (rows, cumulative) = await DailyReportAggregator.BuildWithRunningTotalAsync(
            _context, targetUserId, context.Org!.Id, periodStart, today, context.Membership!.InitialOvertimeHours);

        PeriodBalance Period(DateOnly from)
        {
            var inPeriod = rows.Where(r => r.Date >= from && r.Date <= today).ToList();
            return new PeriodBalance
            {
                From = from,
                To = today,
                TargetHours = Math.Round(inPeriod.Sum(r => r.TargetHours), 2),
                WorkedHours = Math.Round(inPeriod.Sum(r => r.NetWorkedHours), 2),
                OvertimeHours = Math.Round(inPeriod.Sum(r => r.Overtime), 2)
            };
        }

        var failures = await _context.TimeEntries.AsNoTracking()
            .CountAsync(e => e.UserId == targetUserId && e.OrganizationId == context.Org.Id && e.IsFailure);

        return ServiceResult.Ok(new BalanceResponse
        {
            CumulativeOvertimeHours = Math.Round(cumulative, 2),
            InitialOvertimeHours = context.Membership.InitialOvertimeHours,
            Today = Period(today),
            Week = Period(weekStart),
            Month = Period(monthStart),
            FailureEntryCount = failures
        });
    }

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

    // ────────────────────────────────────────────────────
    //  PDF rendering
    // ────────────────────────────────────────────────────

    private static byte[] RenderMonthlyPdf(ReportText t, string orgName, User member, string period, List<DailyReportRow> rows, double overallCumulative)
    {
        var totalTarget = rows.Sum(r => r.TargetHours);
        var totalNet = rows.Sum(r => r.NetWorkedHours);
        var totalOvertime = rows.Sum(r => r.Overtime);
        var vacationDays = rows.Count(r => r.CountsAsVacation);
        var sickDays = rows.Count(r => r.AbsenceType == AbsenceType.SickDay);
        var holidayDays = rows.Count(r => r.HolidayName != null);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Element(c => ComposeHeader(c, t, orgName, member, t["pdf.monthlyTitle"], period));

                page.Content().PaddingTop(10).Column(column =>
                {
                    column.Spacing(12);

                    column.Item().Element(c => ComposeSummaryCards(c, [
                        (t["pdf.target"], FormatHours(t, totalTarget)),
                        (t["pdf.workedNet"], FormatHours(t, totalNet)),
                        (t["pdf.balance"], FormatSigned(t, totalOvertime)),
                        (t["pdf.overallCumulative"], FormatSigned(t, overallCumulative))
                    ]));
                    column.Item().Element(c => ComposeSummaryCards(c, [
                        (t["pdf.vacation"], $"{vacationDays}{t["pdf.daysSuffix"]}"),
                        (t["pdf.sick"], $"{sickDays}{t["pdf.daysSuffix"]}"),
                        (t["pdf.holidays"], $"{holidayDays}{t["pdf.daysSuffix"]}")
                    ]));

                    column.Item().Element(c => ComposeDailyTable(c, t, rows));
                });

                page.Footer().Element(c => ComposeFooter(c, t));
            });
        }).GeneratePdf();
    }

    private static byte[] RenderYearlyPdf(ReportText t, string orgName, User member, int year, List<MonthSummary> months, double overallCumulative)
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

                page.Header().Element(c => ComposeHeader(c, t, orgName, member, t["pdf.yearlyTitle"], year.ToString()));

                page.Content().PaddingTop(10).Column(column =>
                {
                    column.Spacing(12);

                    column.Item().Element(c => ComposeSummaryCards(c, [
                        (t["pdf.target"], FormatHours(t, totalTarget)),
                        (t["pdf.workedNet"], FormatHours(t, totalNet)),
                        (t["pdf.yearBalance"], FormatSigned(t, totalOvertime)),
                        (t["pdf.overallCumulative"], FormatSigned(t, overallCumulative))
                    ]));
                    column.Item().Element(c => ComposeSummaryCards(c, [
                        (t["pdf.vacation"], $"{vacationDays}{t["pdf.daysSuffix"]}"),
                        (t["pdf.sick"], $"{sickDays}{t["pdf.daysSuffix"]}"),
                        (t["pdf.holidays"], $"{holidayDays}{t["pdf.daysSuffix"]}")
                    ]));

                    column.Item().Element(c => ComposeMonthlyTable(c, t, months));
                });

                page.Footer().Element(c => ComposeFooter(c, t));
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(QuestPDF.Infrastructure.IContainer container, ReportText t, string orgName, User member, string title, string period)
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
                    inner.Item().AlignRight().Text(string.Format(t["pdf.generated"], DateTime.UtcNow.ToString($"{t.DateFormat} HH:mm", CultureInfo.InvariantCulture))).FontSize(7).FontColor(Colors.Grey.Medium);
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

    private static void ComposeDailyTable(QuestPDF.Infrastructure.IContainer container, ReportText t, List<DailyReportRow> rows)
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
                foreach (var text in new[] { "date", "day", "target", "worked", "net", "overtime", "cumul", "status", "note" }.Select(k => t["pdf." + k]))
                    header.Cell().Element(HeaderCell).Text(text);
            });

            var rowIndex = 0;
            foreach (var row in rows)
            {
                var isWeekendOrOff = row.Status is "Weekend" or "Day Off";
                var background = rowIndex % 2 == 1 ? Colors.Grey.Lighten4 : Colors.White;
                rowIndex++;

                table.Cell().Element(c => BodyCell(c, background)).Text(row.Date.ToString(t.DateFormat, CultureInfo.InvariantCulture));
                table.Cell().Element(c => BodyCell(c, background)).Text(t.Day(row.DayOfWeek));
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(isWeekendOrOff ? "-" : FormatHours(t, row.TargetHours));
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(row.WorkedHours > 0 ? FormatHours(t, row.WorkedHours) : "-");
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(row.WorkedHours > 0 ? FormatHours(t, row.NetWorkedHours) : "-");
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(span =>
                {
                    if (isWeekendOrOff && row.WorkedHours == 0) { span.Span("-"); return; }
                    span.Span(FormatSigned(t, row.Overtime)).FontColor(row.Overtime < 0 ? Colors.Red.Darken1 : Colors.Green.Darken1);
                });
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(FormatSigned(t, row.CumulativeOvertime));
                table.Cell().Element(c => BodyCell(c, background)).Text(t.Status(row.Status)).FontColor(row.Status == "Needs Correction" ? Colors.Red.Darken1 : Colors.Black);
                table.Cell().Element(c => BodyCell(c, background)).Text(BuildNote(t, row)).FontSize(7.5f);
            }
        });
    }

    private static void ComposeMonthlyTable(QuestPDF.Infrastructure.IContainer container, ReportText t, List<MonthSummary> months)
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
                foreach (var text in new[] { "month", "target", "workedNet", "balance", "cumul", "worked", "missing", "vac", "sick", "holiday" }.Select(k => t["pdf." + k]))
                    header.Cell().Element(HeaderCell).Text(text);
            });

            var rowIndex = 0;
            foreach (var m in months)
            {
                var background = rowIndex % 2 == 1 ? Colors.Grey.Lighten4 : Colors.White;
                rowIndex++;

                table.Cell().Element(c => BodyCell(c, background)).Text(t.Month(m.Month));
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(FormatHours(t, m.TargetHours));
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(FormatHours(t, m.NetWorkedHours));
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(span =>
                {
                    span.Span(FormatSigned(t, m.Overtime)).FontColor(m.Overtime < 0 ? Colors.Red.Darken1 : Colors.Green.Darken1);
                });
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(FormatSigned(t, m.CumulativeOvertimeAtEnd));
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(m.WorkedDays.ToString());
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(m.MissingDays.ToString());
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(m.VacationDays.ToString());
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(m.SickDays.ToString());
                table.Cell().Element(c => BodyCell(c, background)).AlignRight().Text(m.HolidayDays.ToString());
            }
        });
    }

    private static void ComposeFooter(QuestPDF.Infrastructure.IContainer container, ReportText t)
    {
        container.AlignCenter().Text(text =>
        {
            text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Medium));
            text.Span(t["pdf.page"]);
            text.CurrentPageNumber();
            text.Span(" / ");
            text.TotalPages();
        });
    }

    private static QuestPDF.Infrastructure.IContainer HeaderCell(QuestPDF.Infrastructure.IContainer container) =>
        container.Background(Colors.Grey.Darken2).Padding(4).DefaultTextStyle(x => x.FontColor(Colors.White).SemiBold().FontSize(8));

    private static QuestPDF.Infrastructure.IContainer BodyCell(QuestPDF.Infrastructure.IContainer container, string background) =>
        container.Background(background).Padding(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3);

    private static string BuildNote(ReportText t, DailyReportRow row)
    {
        if (row.HolidayName != null) return row.HolidayName;
        if (row.AbsenceType != null) return row.AbsenceNote ?? t.Absence(row.AbsenceType.Value);
        return t.EntrySummary(row.EntrySummary);
    }

    private static string FormatHours(ReportText t, double hours) => $"{hours.ToString("F2", t.Numbers)} h";

    private static string FormatSigned(ReportText t, double hours) => $"{hours.ToString("+0.00;-0.00;0.00", t.Numbers)} h";

    private record MonthSummary(
        int Month, double TargetHours, double WorkedHours, double NetWorkedHours,
        double Overtime, double CumulativeOvertimeAtEnd,
        int WorkedDays, int MissingDays, int VacationDays, int SickDays, int HolidayDays);
}
