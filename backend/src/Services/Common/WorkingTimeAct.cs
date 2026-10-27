using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Working Time Act (ArbZG) checks on tracked time: more than 10 h net work on one day (§ 3)
/// and less than 11 h rest between the last entry of one day and the first of the next (§ 5).
/// Days are local days of the organization; only finished, non-failure entries of the same
/// organization (or the personal entries, without one) count. These are warnings, nothing is blocked.
/// </summary>
public static class WorkingTimeAct
{
    public const double MaxDailyHours = 10;
    public const double MinRestHours = 11;
    public const string DailyMax = "dailyMax";
    public const string RestPeriod = "restPeriod";

    /// <summary>One row per day in [from, to] that has entries.</summary>
    public static async Task<List<ComplianceDayResponse>> DaysAsync(
        TimeTrackingDbContext context, int userId, int? orgId, DateOnly from, DateOnly to)
    {
        var timeZone = orgId == null ? null : await context.Organizations.AsNoTracking()
            .Where(o => o.Id == orgId).Select(o => o.TimeZone).FirstOrDefaultAsync();
        var clock = OrgClock.For(timeZone);
        var fromUtc = clock.StartOfDayUtc(from.AddDays(-1));
        var toUtc = clock.StartOfDayUtc(to.AddDays(1));

        var entries = await context.TimeEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.OrganizationId == orgId && !e.IsRunning && !e.IsFailure
                     && e.EndTime != null && e.StartTime >= fromUtc && e.StartTime < toUtc)
            .ToListAsync();
        return Days(clock, entries, from, to);
    }

    /// <summary><see cref="DaysAsync"/> over already loaded entries (finished ones of the days from the day before
    /// <paramref name="from"/> on; failure entries are skipped here).</summary>
    public static List<ComplianceDayResponse> Days(OrgClock clock, IEnumerable<TimeEntry> entries, DateOnly from, DateOnly to)
    {
        var byDay = entries.Where(e => !e.IsRunning && !e.IsFailure && e.EndTime != null)
            .GroupBy(e => clock.LocalDate(e.StartTime)).ToDictionary(g => g.Key, g => g.ToList());

        var days = new List<ComplianceDayResponse>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (!byDay.TryGetValue(day, out var dayEntries)) continue;
            var netHours = dayEntries.Sum(e => Math.Max(0, (e.EndTime!.Value - e.StartTime).TotalMinutes - e.PauseDurationMinutes)) / 60.0;
            double? restHours = byDay.TryGetValue(day.AddDays(-1), out var previous)
                ? (dayEntries.Min(e => e.StartTime) - previous.Max(e => e.EndTime!.Value)).TotalHours
                : null;
            days.Add(new ComplianceDayResponse
            {
                Date = day,
                NetHours = Math.Round(netHours, 2),
                RestHoursBefore = restHours.HasValue ? Math.Round(restHours.Value, 2) : null,
                DailyMax = netHours > MaxDailyHours,
                RestPeriod = restHours < MinRestHours
            });
        }
        return days;
    }

    /// <summary>
    /// The compliance findings: the Working Time Act days merged with the open core-hours violations, only days with a finding.
    /// </summary>
    public static List<ComplianceDayResponse> Findings(IEnumerable<ComplianceDayResponse> workingTimeDays,
        IReadOnlyDictionary<DateOnly, CoreHours.Result> violations)
    {
        var days = workingTimeDays.ToDictionary(d => d.Date);
        foreach (var (date, core) in violations)
            days[date] = (days.GetValueOrDefault(date) ?? new ComplianceDayResponse { Date = date }) with
            {
                CoreHours = true,
                CoreHoursIssues = CoreHours.IssueNames(core.Issues),
                CoreHoursMissingMinutes = core.MissingMinutes,
                CoreHoursWindow = core.Window.ToString(),
                CoreHoursBackfilled = core.Backfilled
            };
        return days.Values.Where(d => d.DailyMax || d.RestPeriod || d.CoreHours).OrderBy(d => d.Date).ToList();
    }

    /// <summary>
    /// Warnings for a finished entry: <see cref="DailyMax"/> for its day, <see cref="RestPeriod"/>
    /// when the rest before its day or before the next day is too short.
    /// </summary>
    public static async Task<List<string>> WarningsAsync(TimeTrackingDbContext context, TimeEntry entry)
    {
        if (entry.IsRunning || entry.IsFailure || entry.EndTime == null) return [];
        var timeZone = entry.Organization?.TimeZone ?? (entry.OrganizationId == null ? null : await context.Organizations.AsNoTracking()
            .Where(o => o.Id == entry.OrganizationId).Select(o => o.TimeZone).FirstOrDefaultAsync());
        var day = OrgClock.For(timeZone).LocalDate(entry.StartTime);

        var days = await DaysAsync(context, entry.UserId, entry.OrganizationId, day, day.AddDays(1));
        var warnings = new List<string>();
        if (days.Any(d => d.Date == day && d.DailyMax)) warnings.Add(DailyMax);
        if (days.Any(d => d.RestPeriod)) warnings.Add(RestPeriod);
        return warnings;
    }

    /// <summary>The response with the entry's warnings filled in.</summary>
    public static async Task<TimeEntryResponse> WithWarningsAsync(TimeTrackingDbContext context, TimeEntry entry, TimeEntryResponse response) =>
        response with { Warnings = await WarningsAsync(context, entry) };
}
