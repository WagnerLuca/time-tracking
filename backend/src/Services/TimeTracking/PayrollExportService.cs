using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>Request payload for the payroll export.</summary>
public record PayrollExportRequest
{
    [Required]
    public required string OrgSlug { get; init; }
    /// <summary>First local day (inclusive).</summary>
    public DateOnly From { get; init; }
    /// <summary>Last local day (inclusive); days after today are not counted yet.</summary>
    public DateOnly To { get; init; }
    /// <summary>Header language: "de" (default) or "en". The format stays German either way.</summary>
    public string? Lang { get; init; }
}

/// <summary>One member's figures of a period (hours rounded to 2 decimals, days as counted, failure entries started in it).</summary>
public record PayrollRow(int UserId, string? PersonnelNumber, string Name, string Email,
    double TargetHours, double WorkedHours, double OvertimeHours, double OvertimeBalance,
    double VacationDays, double SickDays, double OtherAbsenceDays, int FailureEntryCount, bool IsFormerMember = false);

/// <summary>
/// One CSV for payroll: a row per member (active, or former with time/absences in the period) with target, worked and overtime hours of the
/// period, the overtime balance, absences and the number of entries that still need correcting.
/// German format for German payroll tools: <c>;</c> separator, decimal comma, UTF-8 BOM.
/// Hours follow the same rules as reports and balances (<see cref="DailyReportAggregator"/>).
/// Accountants; org admins and owners only while the org lets admins see members' hours (like every export).
/// </summary>
public class PayrollExportService(TimeTrackingDbContext context, ILogger<PayrollExportService> logger)
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private static readonly string[] HeadersDe =
    [
        "Personalnummer", "Name", "E-Mail", "Soll (h)", "Ist (h)", "Überstunden Zeitraum (h)", "Überstundensaldo (h)",
        "Urlaubstage", "Krankheitstage", "Sonstige Abwesenheiten", "Korrektur nötig (Anzahl)"
    ];

    private static readonly string[] HeadersEn =
    [
        "Personnel number", "Name", "Email", "Target (h)", "Worked (h)", "Overtime in period (h)", "Overtime balance (h)",
        "Vacation days", "Sick days", "Other absences", "Needs correction (count)"
    ];

    public async Task<ServiceResult<byte[]>> ExportAsync(int callerUserId, PayrollExportRequest request)
    {
        var org = await context.FindOrgBySlugAsync(request.OrgSlug);
        if (org == null) return ServiceResult.NotFound<byte[]>("Organization not found.");
        // Accountants always; admins and the Owner while the org lets admins see members' hours
        var accounting = await context.IsAccountantAsync(callerUserId, org.Id);
        if (!accounting && await context.GetOrgRoleAsync(callerUserId, org.Id) is not >= OrganizationRole.Admin)
            return ServiceResult.Forbidden<byte[]>("Only admins can export payroll data.");
        if (org.Require2fa && !await context.Users.AnyAsync(u => u.Id == callerUserId && u.TwoFactorEnabled))
            return ServiceResult.Forbidden<byte[]>("This organization requires two-factor authentication. Enable it in your account settings to continue.");
        if (!accounting && !org.MemberTimeEntryVisibility)
            return ServiceResult.Forbidden<byte[]>("Admins may not see members' working hours in this organization (organization settings).");
        if (EntryTimes.ValidateRange(request.From, request.To) is { } rangeError)
            return ServiceResult.BadRequest<byte[]>(rangeError);

        var rows = await RowsAsync(org, request.From, request.To);
        logger.LogInformation("Payroll export of org {OrgId} for {From}..{To} by user {UserId}: {Count} members",
            org.Id, request.From, request.To, callerUserId, rows.Count);
        return ServiceResult.Ok(Csv(rows, request.Lang));
    }

    /// <summary>
    /// One row per member over [from, to] (local days; days after today not counted yet): every active member,
    /// plus former members who tracked time or were absent in the period (<see cref="PayrollRow.IsFormerMember"/>).
    /// Also the accounting panel's monthly summary — no permission check here, callers authorize.
    /// </summary>
    public async Task<List<PayrollRow>> RowsAsync(Organization org, DateOnly from, DateOnly to)
    {
        var clock = OrgClock.For(org);
        var fromUtc = clock.StartOfDayUtc(from);
        var toUtc = clock.StartOfDayUtc(to.AddDays(1));

        var members = await context.UserOrganizations.AsNoTracking()
            .Include(uo => uo.User)
            .Where(uo => uo.OrganizationId == org.Id && uo.User.IsActive)
            .ToListAsync();
        var activeIds = members.Select(m => m.UserId).ToHashSet();
        var withActivity = await context.TimeEntries.AsNoTracking()
            .Where(e => e.OrganizationId == org.Id && !e.IsRunning && e.StartTime >= fromUtc && e.StartTime < toUtc)
            .Select(e => e.UserId)
            .Union(context.AbsenceDays.AsNoTracking()
                .Where(a => a.OrganizationId == org.Id && a.Date >= from && a.Date <= to)
                .Select(a => a.UserId))
            .Distinct()
            .ToListAsync();
        var formerIds = withActivity.Where(id => !activeIds.Contains(id)).ToList();
        if (formerIds.Count > 0)
            members.AddRange(await context.UserOrganizations.AsNoTracking().IgnoreQueryFilters()
                .Include(uo => uo.User)
                .Where(uo => uo.OrganizationId == org.Id && formerIds.Contains(uo.UserId))
                .ToListAsync());
        members = members.OrderBy(uo => uo.User.LastName).ThenBy(uo => uo.User.FirstName).ToList();
        var absences = (await context.AbsenceDays.AsNoTracking()
                .Include(a => a.CustomType)
                .Where(a => a.OrganizationId == org.Id && a.Date >= from && a.Date <= to)
                .ToListAsync())
            .ToLookup(a => a.UserId);
        var failures = await context.TimeEntries.AsNoTracking()
            .Where(e => e.OrganizationId == org.Id && e.IsFailure && e.StartTime >= fromUtc && e.StartTime < toUtc)
            .GroupBy(e => e.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(f => f.UserId, f => f.Count);

        // Hours of every member in four queries (not six per member): the same rows as BuildAsync from each member's first entry
        var hours = await DailyReportAggregator.TotalsAsync(context, org,
            members.ToDictionary(m => m.UserId, m => m.InitialOvertimeHours), from, to, failureCounts: false);

        var rows = new List<PayrollRow>();
        foreach (var member in members)
        {
            var (target, worked, overtime, balance, _) = hours[member.UserId];
            var memberAbsences = absences[member.UserId].ToList();
            static double Days(IEnumerable<AbsenceDay> days) => days.Sum(a => a.IsHalfDay ? 0.5 : 1.0);
            var vacation = Days(memberAbsences.Where(a => a.Type == AbsenceType.Vacation || a.CustomType?.CountsAsVacation == true));
            var sick = Days(memberAbsences.Where(a => a.Type == AbsenceType.SickDay));
            var other = Days(memberAbsences) - vacation - sick;
            rows.Add(new PayrollRow(member.UserId, member.PersonnelNumber, $"{member.User.FirstName} {member.User.LastName}", member.User.Email,
                Math.Round(target, 2), Math.Round(worked, 2), Math.Round(overtime, 2), Math.Round(balance, 2),
                vacation, sick, other, failures.GetValueOrDefault(member.UserId), IsFormerMember: !activeIds.Contains(member.UserId)));
        }
        return rows;
    }

    /// <summary>The rows as payroll CSV (German format; <paramref name="lang"/> "en" only changes the headers).</summary>
    public static byte[] Csv(IEnumerable<PayrollRow> rows, string? lang)
    {
        var csv = new StringBuilder();
        csv.Append('\uFEFF');
        csv.AppendLine(string.Join(';', lang?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? HeadersEn : HeadersDe));
        foreach (var r in rows)
            csv.AppendLine(string.Join(';',
                Escape(r.PersonnelNumber ?? ""), Escape(r.Name), Escape(r.Email),
                Number(r.TargetHours), Number(r.WorkedHours), Number(r.OvertimeHours), Number(r.OvertimeBalance),
                Number(r.VacationDays), Number(r.SickDays), Number(r.OtherAbsenceDays),
                r.FailureEntryCount.ToString(German)));
        return new UTF8Encoding(false).GetBytes(csv.ToString());
    }

    private static string Number(double value) => Math.Round(value, 2).ToString("0.00", German);

    /// <summary>Quotes cells with separators/quotes/line breaks and defuses spreadsheet formulas (CSV injection).</summary>
    internal static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
