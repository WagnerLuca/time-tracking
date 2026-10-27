using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// The accounting panel (docs/ACCOUNTING_ROLE_PLAN.md): monthly summary of every member (JSON, CSV, PDF),
/// the months of a year with their closing state and warnings, and the vacation overview.
/// Endpoints are open to admins and accountants (<c>[RequireOrgRole(Admin, OrAccountant = true)]</c>);
/// accountants always see everybody's hours, admins and the Owner only while the organization lets
/// admins see members' hours (<see cref="Organization.MemberTimeEntryVisibility"/>), as in every export.
/// Nothing here writes; closing and reopening is <see cref="ClosedPeriodService"/>.
/// </summary>
public class AccountingService(TimeTrackingDbContext context, PayrollExportService payroll, ILogger<AccountingService> logger)
{
    public const string HoursHiddenMessage = "Admins may not see members' working hours in this organization (organization settings).";
    public const string YearRangeMessage = "Year must be between 2000 and 2100.";

    private async Task<(Organization? Org, ServiceErrorType? Error, string? Message, string? Code)> AuthorizeAsync(
        string slug, int callerUserId, bool hours)
    {
        var org = await context.FindOrgBySlugAsync(slug);
        if (org == null) return (null, ServiceErrorType.NotFound, "Organization not found", null);
        if (hours && !org.MemberTimeEntryVisibility && !await context.IsAccountantAsync(callerUserId, org.Id))
            return (null, ServiceErrorType.Forbidden, HoursHiddenMessage, null);
        return (org, null, null, null);
    }

    private static string? ValidateYear(int year) => year is < 2000 or > 2100 ? YearRangeMessage : null;

    public async Task<ServiceResult<AccountingSummaryResponse>> GetSummaryAsync(string slug, int callerUserId, int year, int month)
    {
        if (ValidateYear(year) is { } yearError) return ServiceResult.BadRequest<AccountingSummaryResponse>(yearError, "accounting.invalidYear");
        if (month is < 1 or > 12) return ServiceResult.BadRequest<AccountingSummaryResponse>("Month must be between 1 and 12.");
        var (org, error, message, code) = await AuthorizeAsync(slug, callerUserId, hours: true);
        if (org == null) return ServiceResult.Fail<AccountingSummaryResponse>(error!.Value, message!, code);

        var first = new DateOnly(year, month, 1);
        var rows = await payroll.RowsAsync(org, first, first.AddMonths(1).AddDays(-1));
        var closed = await context.ClosedPeriods.AnyAsync(p => p.OrganizationId == org.Id && p.Month == first);
        logger.LogInformation("Accounting summary of org {OrgId} for {Year}-{Month} by user {UserId}", org.Id, year, month, callerUserId);
        return ServiceResult.Ok(new AccountingSummaryResponse
        {
            Year = year,
            Month = month,
            IsClosed = closed,
            Members = rows.Select(r => new AccountingMemberSummary
            {
                UserId = r.UserId, PersonnelNumber = r.PersonnelNumber, Name = r.Name, Email = r.Email,
                TargetHours = r.TargetHours, WorkedHours = r.WorkedHours, OvertimeHours = r.OvertimeHours, OvertimeBalance = r.OvertimeBalance,
                VacationDays = r.VacationDays, SickDays = r.SickDays, OtherAbsenceDays = r.OtherAbsenceDays,
                FailureEntryCount = r.FailureEntryCount, IsFormerMember = r.IsFormerMember
            }).ToList()
        });
    }

    /// <summary>The monthly summary in the payroll CSV format.</summary>
    public async Task<ServiceResult<byte[]>> GetSummaryCsvAsync(string slug, int callerUserId, int year, int month, string? lang)
    {
        var summary = await GetSummaryAsync(slug, callerUserId, year, month);
        if (!summary.IsSuccess) return ServiceResult.Fail<byte[]>(summary.ErrorType!.Value, summary.ErrorMessage!, summary.Code);
        return ServiceResult.Ok(PayrollExportService.Csv(summary.Data!.Members.Select(ToRow), lang));
    }

    public async Task<ServiceResult<byte[]>> GetSummaryPdfAsync(string slug, int callerUserId, int year, int month, string? lang)
    {
        var summary = await GetSummaryAsync(slug, callerUserId, year, month);
        if (!summary.IsSuccess) return ServiceResult.Fail<byte[]>(summary.ErrorType!.Value, summary.ErrorMessage!, summary.Code);
        var org = (await context.FindOrgBySlugAsync(slug))!;
        return ServiceResult.Ok(RenderSummaryPdf(ReportText.For(lang), org.Name, summary.Data!));
    }

    /// <summary>Checks access for one member's day-by-day report (the PDF itself is <see cref="IPdfReportService"/>).</summary>
    public async Task<ServiceResult> AuthorizeMemberReportAsync(string slug, int callerUserId)
    {
        var (org, error, message, code) = await AuthorizeAsync(slug, callerUserId, hours: true);
        return org == null ? ServiceResult.Fail(error!.Value, message!, code) : ServiceResult.Ok();
    }

    public async Task<ServiceResult<AccountingMonthsResponse>> GetMonthsAsync(string slug, int callerUserId, int year)
    {
        if (ValidateYear(year) is { } yearError) return ServiceResult.BadRequest<AccountingMonthsResponse>(yearError, "accounting.invalidYear");
        var (org, error, message, code) = await AuthorizeAsync(slug, callerUserId, hours: false);
        if (org == null) return ServiceResult.Fail<AccountingMonthsResponse>(error!.Value, message!, code);

        var clock = OrgClock.For(org);
        var today = clock.Today();
        var months = Enumerable.Range(1, 12).Select(m => new DateOnly(year, m, 1)).ToList();
        var (firstMonth, lastMonth) = (months[0], months[^1]);
        var (fromUtc, toUtc) = (clock.StartOfDayUtc(firstMonth), clock.StartOfDayUtc(lastMonth.AddMonths(1)));
        var closed = await context.ClosedPeriods.AsNoTracking()
            .Where(p => p.OrganizationId == org.Id && p.Month >= firstMonth && p.Month <= lastMonth)
            .Select(p => new { p.Month, p.ClosedAt, p.ClosedByUserId })
            .ToListAsync();
        var closerIds = closed.Select(c => c.ClosedByUserId).OfType<int>().Distinct().ToList();
        var closers = await context.Users.AsNoTracking().IgnoreQueryFilters().Where(u => closerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName);

        var failureStarts = await context.TimeEntries.AsNoTracking()
            .Where(e => e.OrganizationId == org.Id && e.IsFailure
                && e.StartTime >= fromUtc && e.StartTime < toUtc)
            .Select(e => e.StartTime)
            .ToListAsync();
        var failures = failureStarts.Select(s => ClosedPeriods.MonthOf(clock.LocalDate(s))).CountBy(m => m).ToDictionary();
        // Closed months: the failure entries frozen with them
        var closedMonths = closed.Select(c => c.Month).ToList();
        if (closedMonths.Count > 0)
            foreach (var f in await context.FrozenMonths.AsNoTracking()
                         .Where(f => f.OrganizationId == org.Id && closedMonths.Contains(f.Month))
                         .GroupBy(f => f.Month).Select(g => new { Month = g.Key, Count = g.Sum(f => f.FailureEntryCount) })
                         .ToListAsync())
                failures[f.Month] = f.Count;
        var pending = await ClosedPeriods.PendingRequestsPerMonthAsync(context, org, months);

        return ServiceResult.Ok(new AccountingMonthsResponse
        {
            Year = year,
            Months = months.Select(m =>
            {
                var c = closed.FirstOrDefault(x => x.Month == m);
                return new AccountingMonthStatus
                {
                    Month = m,
                    HasEnded = m.AddMonths(1) <= today,
                    IsClosed = c != null,
                    ClosedAt = c?.ClosedAt,
                    ClosedByName = c?.ClosedByUserId is int id ? closers.GetValueOrDefault(id) : null,
                    FailureEntryCount = failures.GetValueOrDefault(m),
                    PendingRequestCount = pending.GetValueOrDefault(m)
                };
            }).ToList()
        });
    }

    public async Task<ServiceResult<VacationOverviewResponse>> GetVacationOverviewAsync(string slug, int callerUserId, int year)
    {
        if (ValidateYear(year) is { } yearError) return ServiceResult.BadRequest<VacationOverviewResponse>(yearError, "accounting.invalidYear");
        var (org, error, message, code) = await AuthorizeAsync(slug, callerUserId, hours: false);
        if (org == null) return ServiceResult.Fail<VacationOverviewResponse>(error!.Value, message!, code);

        var members = await context.UserOrganizations.AsNoTracking()
            .Where(uo => uo.OrganizationId == org.Id && uo.User.IsActive)
            .OrderBy(uo => uo.User.LastName).ThenBy(uo => uo.User.FirstName)
            .Select(uo => new { uo.UserId, Name = uo.User.FirstName + " " + uo.User.LastName, uo.PersonnelNumber })
            .ToListAsync();
        var ids = members.Select(m => m.UserId).ToList();
        var balances = await VacationBalances.YearsAsync(context, org, ids, year, OrgClock.For(org).Today());

        var (yearStart, yearEnd) = (new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
        var live = (await context.AbsenceDays.AsNoTracking()
                .Where(a => a.OrganizationId == org.Id && ids.Contains(a.UserId) && a.Date >= yearStart && a.Date <= yearEnd)
                .Select(a => new { a.UserId, a.Date, a.Type, CountsAsVacation = a.CustomType != null && a.CustomType.CountsAsVacation, a.IsHalfDay })
                .ToListAsync())
            .Select(a => new FrozenFigures.Absence(a.UserId, a.Date, a.Type, a.CountsAsVacation, a.IsHalfDay ? 0.5 : 1.0, 0));
        // Closed months as frozen
        var others = FrozenFigures.Overlay(live, await FrozenFigures.AbsencesAsync(context, org.Id, ids, yearStart, yearEnd))
            .Where(a => a.Type != AbsenceType.Vacation && !a.CountsAsVacation)
            .ToLookup(a => a.UserId);

        return ServiceResult.Ok(new VacationOverviewResponse
        {
            Year = year,
            Members = members.Select(m =>
            {
                var b = balances[m.UserId];
                var days = others[m.UserId].ToList();
                return new VacationOverviewRow
                {
                    UserId = m.UserId,
                    Name = m.Name,
                    PersonnelNumber = m.PersonnelNumber,
                    Entitlement = b.Entitlement,
                    Adjustment = b.Adjustment,
                    CarriedIn = b.CarriedIn,
                    Expired = b.Expired,
                    ExpiresOn = b.ExpiresOn,
                    Taken = b.Taken,
                    Planned = b.Planned,
                    Remaining = b.Remaining,
                    SickDays = days.Where(a => a.Type == AbsenceType.SickDay).Sum(a => a.Days),
                    OtherAbsenceDays = days.Where(a => a.Type != AbsenceType.SickDay).Sum(a => a.Days)
                };
            }).ToList()
        });
    }

    private static PayrollRow ToRow(AccountingMemberSummary m) => new(m.UserId, m.PersonnelNumber, m.Name, m.Email,
        m.TargetHours, m.WorkedHours, m.OvertimeHours, m.OvertimeBalance, m.VacationDays, m.SickDays, m.OtherAbsenceDays,
        m.FailureEntryCount, m.IsFormerMember);

    // ── PDF ──

    private static byte[] RenderSummaryPdf(ReportText t, string orgName, AccountingSummaryResponse summary)
    {
        string Days(double d) => d.ToString("0.#", t.Numbers);
        var members = summary.Members;
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(8.5f));

                page.Header().Column(column =>
                {
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(inner =>
                        {
                            inner.Item().Text(t["pdf.accountingTitle"]).FontSize(16).SemiBold();
                            inner.Item().Text($"{t.Month(summary.Month)} {summary.Year} · {t[summary.IsClosed ? "pdf.closed" : "pdf.open"]}").FontSize(10);
                        });
                        row.ConstantItem(220).AlignRight().Column(inner =>
                        {
                            inner.Item().AlignRight().Text(orgName).FontSize(10).SemiBold();
                            inner.Item().AlignRight().Text(string.Format(t["pdf.generated"], DateTime.UtcNow.ToString($"{t.DateFormat} HH:mm", CultureInfo.InvariantCulture)))
                                .FontSize(7).FontColor(Colors.Grey.Medium);
                        });
                    });
                    column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(55);  // Personnel no.
                        columns.RelativeColumn(3);   // Name
                        columns.RelativeColumn();    // Target
                        columns.RelativeColumn();    // Worked
                        columns.RelativeColumn();    // Overtime
                        columns.RelativeColumn();    // Balance
                        columns.ConstantColumn(40);  // Vacation
                        columns.ConstantColumn(35);  // Sick
                        columns.ConstantColumn(40);  // Other
                        columns.ConstantColumn(40);  // Corrections
                    });
                    table.Header(header =>
                    {
                        foreach (var key in new[] { "personnelNo", "member", "target", "workedNet", "balance", "overallCumulative", "vac", "sick", "otherAbsences", "corrections" })
                            header.Cell().Element(PdfReportService.HeaderCell).Text(t["pdf." + key]);
                    });
                    var index = 0;
                    foreach (var m in members)
                    {
                        var bg = index++ % 2 == 1 ? Colors.Grey.Lighten4 : Colors.White;
                        IContainer Cell(IContainer c) => PdfReportService.BodyCell(c, bg);
                        table.Cell().Element(Cell).Text(m.PersonnelNumber ?? "");
                        table.Cell().Element(Cell).Text(m.IsFormerMember ? $"{m.Name} ({t["pdf.former"]})" : m.Name);
                        table.Cell().Element(Cell).AlignRight().Text(PdfReportService.FormatHours(t, m.TargetHours));
                        table.Cell().Element(Cell).AlignRight().Text(PdfReportService.FormatHours(t, m.WorkedHours));
                        table.Cell().Element(Cell).AlignRight().Text(PdfReportService.FormatSigned(t, m.OvertimeHours))
                            .FontColor(m.OvertimeHours < 0 ? Colors.Red.Darken1 : Colors.Green.Darken1);
                        table.Cell().Element(Cell).AlignRight().Text(PdfReportService.FormatSigned(t, m.OvertimeBalance));
                        table.Cell().Element(Cell).AlignRight().Text(Days(m.VacationDays));
                        table.Cell().Element(Cell).AlignRight().Text(Days(m.SickDays));
                        table.Cell().Element(Cell).AlignRight().Text(Days(m.OtherAbsenceDays));
                        table.Cell().Element(Cell).AlignRight().Text(m.FailureEntryCount.ToString(CultureInfo.InvariantCulture))
                            .FontColor(m.FailureEntryCount > 0 ? Colors.Red.Darken1 : Colors.Black);
                    }
                    IContainer Total(IContainer c) => c.Background(Colors.Grey.Lighten3).Padding(4);
                    table.Cell().ColumnSpan(2).Element(Total).Text(t["pdf.total"]).SemiBold();
                    table.Cell().Element(Total).AlignRight().Text(PdfReportService.FormatHours(t, members.Sum(m => m.TargetHours))).SemiBold();
                    table.Cell().Element(Total).AlignRight().Text(PdfReportService.FormatHours(t, members.Sum(m => m.WorkedHours))).SemiBold();
                    table.Cell().Element(Total).AlignRight().Text(PdfReportService.FormatSigned(t, members.Sum(m => m.OvertimeHours))).SemiBold();
                    table.Cell().Element(Total).Text("");
                    table.Cell().Element(Total).AlignRight().Text(Days(members.Sum(m => m.VacationDays))).SemiBold();
                    table.Cell().Element(Total).AlignRight().Text(Days(members.Sum(m => m.SickDays))).SemiBold();
                    table.Cell().Element(Total).AlignRight().Text(Days(members.Sum(m => m.OtherAbsenceDays))).SemiBold();
                    table.Cell().Element(Total).AlignRight().Text(members.Sum(m => m.FailureEntryCount).ToString(CultureInfo.InvariantCulture)).SemiBold();
                });

                page.Footer().Element(c => PdfReportService.ComposeFooter(c, t));
            });
        }).GeneratePdf();
    }
}
