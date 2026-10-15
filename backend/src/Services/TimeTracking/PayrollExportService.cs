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

/// <summary>
/// One CSV for payroll: a row per active member with target, worked and overtime hours of the
/// period, the overtime balance, absences and the number of entries that still need correcting.
/// German format for German payroll tools: <c>;</c> separator, decimal comma, UTF-8 BOM.
/// Hours follow the same rules as reports and balances (<see cref="DailyReportAggregator"/>).
/// Org admins only, and only while the org lets admins see members' hours (like every export).
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
        if (await context.GetOrgRoleAsync(callerUserId, org.Id) is not >= OrganizationRole.Admin)
            return ServiceResult.Forbidden<byte[]>("Only admins can export payroll data.");
        if (org.Require2fa && !await context.Users.AnyAsync(u => u.Id == callerUserId && u.TwoFactorEnabled))
            return ServiceResult.Forbidden<byte[]>("This organization requires two-factor authentication. Enable it in your account settings to continue.");
        if (!org.MemberTimeEntryVisibility)
            return ServiceResult.Forbidden<byte[]>("Admins may not see members' working hours in this organization (organization settings).");
        if (EntryTimes.ValidateRange(request.From, request.To) is { } rangeError)
            return ServiceResult.BadRequest<byte[]>(rangeError);

        var clock = OrgClock.For(org);
        var fromUtc = clock.StartOfDayUtc(request.From);
        var toUtc = clock.StartOfDayUtc(request.To.AddDays(1));

        var members = await context.UserOrganizations.AsNoTracking()
            .Include(uo => uo.User)
            .Where(uo => uo.OrganizationId == org.Id && uo.User.IsActive)
            .OrderBy(uo => uo.User.LastName).ThenBy(uo => uo.User.FirstName)
            .ToListAsync();
        var absences = (await context.AbsenceDays.AsNoTracking()
                .Include(a => a.CustomType)
                .Where(a => a.OrganizationId == org.Id && a.Date >= request.From && a.Date <= request.To)
                .ToListAsync())
            .ToLookup(a => a.UserId);
        var failures = (await context.TimeEntries.AsNoTracking()
                .Where(e => e.OrganizationId == org.Id && e.IsFailure && e.StartTime >= fromUtc && e.StartTime < toUtc)
                .Select(e => e.UserId)
                .ToListAsync())
            .ToLookup(id => id);

        var csv = new StringBuilder();
        csv.Append('﻿');
        csv.AppendLine(string.Join(';', request.Lang?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? HeadersEn : HeadersDe));

        foreach (var member in members)
        {
            var (target, worked, overtime, balance) = await HoursAsync(member, org.Id, clock, request.From, request.To);
            var memberAbsences = absences[member.UserId].ToList();
            static double Days(IEnumerable<AbsenceDay> days) => days.Sum(a => a.IsHalfDay ? 0.5 : 1.0);
            var vacation = Days(memberAbsences.Where(a => a.Type == AbsenceType.Vacation || a.CustomType?.CountsAsVacation == true));
            var sick = Days(memberAbsences.Where(a => a.Type == AbsenceType.SickDay));
            var other = Days(memberAbsences) - vacation - sick;

            csv.AppendLine(string.Join(';',
                Escape(member.PersonnelNumber ?? ""),
                Escape($"{member.User.FirstName} {member.User.LastName}"),
                Escape(member.User.Email),
                Number(target), Number(worked), Number(overtime), Number(balance),
                Number(vacation), Number(sick), Number(other),
                failures[member.UserId].Count().ToString(German)));
        }

        logger.LogInformation("Payroll export of org {OrgId} for {From}..{To} by user {UserId}: {Count} members",
            org.Id, request.From, request.To, callerUserId, members.Count);
        return ServiceResult.Ok(new UTF8Encoding(false).GetBytes(csv.ToString()));
    }

    /// <summary>Target, net worked and overtime hours of the period, and the overtime balance at its end (since the first entry, as in the balance).</summary>
    private async Task<(double Target, double Worked, double Overtime, double Balance)> HoursAsync(
        UserOrganization member, int orgId, OrgClock clock, DateOnly from, DateOnly to)
    {
        var firstEntry = await context.TimeEntries.AsNoTracking()
            .Where(e => e.UserId == member.UserId && e.OrganizationId == orgId && !e.IsRunning)
            .MinAsync(e => (DateTime?)e.StartTime);
        if (firstEntry == null) return (0, 0, 0, member.InitialOvertimeHours);

        var rows = await DailyReportAggregator.BuildAsync(context, member.UserId, orgId,
            clock.LocalDate(firstEntry.Value), to, member.InitialOvertimeHours);
        var period = rows.Where(r => r.Date >= from).ToList();
        var balance = rows.Count > 0 ? rows[^1].CumulativeOvertime : member.InitialOvertimeHours;
        return (period.Sum(r => r.TargetHours), period.Sum(r => r.NetWorkedHours), period.Sum(r => r.Overtime), balance);
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
