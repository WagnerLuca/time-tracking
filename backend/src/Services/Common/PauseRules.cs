using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

public static class PauseRules
{
    /// <summary>ArbZG §4: only breaks of at least 15 minutes count as rest breaks.</summary>
    public const int MinCreditedGapMinutes = 15;

    /// <summary>
    /// Recomputes the automatic pauses of the entry's organization-local day (ArbZG §4): the
    /// required break follows from the rules the day's total work <em>exceeds</em> (gross time of
    /// all finished, non-failure entries of the user in that organization on that day), capped at
    /// the time beyond each threshold — see <see cref="RequiredPauseMinutes"/>. Gaps of at least 15 minutes between those entries and manual
    /// pauses (<see cref="TimeEntry.PauseIsManual"/>) already count; the remaining deficit goes on
    /// the day's longest entry with an automatic pause, the other automatic pauses become 0.
    /// No-op without an end time, without an organization or when auto-pause is off.
    /// Shared by stop, auto-stop on start, direct edits, manual entries and approved requests.
    /// ponytail: moving or deleting an entry does not recompute the day it left.
    /// </summary>
    public static async Task ApplyAsync(TimeTrackingDbContext context, TimeEntry entry)
    {
        if (entry.OrganizationId == null || !entry.EndTime.HasValue) return;

        var org = await context.Organizations.AsNoTracking()
            .Where(o => o.Id == entry.OrganizationId)
            .Select(o => new { o.AutoPauseEnabled, o.TimeZone })
            .FirstOrDefaultAsync();
        if (org is not { AutoPauseEnabled: true }) return;

        var clock = OrgClock.For(org.TimeZone);
        var day = clock.LocalDate(entry.StartTime);
        var dayStartUtc = clock.StartOfDayUtc(day);
        var dayEndUtc = clock.StartOfDayUtc(day.AddDays(1));

        // Tracked: their automatic pauses may change too
        var others = await context.TimeEntries
            .Where(e => e.Id != entry.Id && e.UserId == entry.UserId && e.OrganizationId == entry.OrganizationId
                     && !e.IsRunning && !e.IsFailure && e.EndTime != null
                     && e.StartTime >= dayStartUtc && e.StartTime < dayEndUtc)
            .ToListAsync();
        var dayEntries = others.Append(entry).OrderBy(e => e.StartTime).ToList();
        var workHours = dayEntries.Sum(e => (e.EndTime!.Value - e.StartTime).TotalHours);

        var rules = await context.PauseRules.AsNoTracking()
            .Where(pr => pr.OrganizationId == entry.OrganizationId)
            .Select(pr => new { pr.MinHours, pr.PauseMinutes })
            .ToListAsync();
        var required = RequiredPauseMinutes(rules.Select(r => (r.MinHours, r.PauseMinutes)), workHours);

        var deficit = Math.Max(0, required
            - CreditedGapMinutes(dayEntries.Select(e => (e.StartTime, e.EndTime!.Value)).ToList())
            - dayEntries.Where(e => e.PauseIsManual).Sum(e => e.PauseDurationMinutes));

        var automatic = dayEntries.Where(e => !e.PauseIsManual).ToList();
        foreach (var e in automatic) e.PauseDurationMinutes = 0;
        var carrier = automatic.OrderByDescending(e => e.EndTime!.Value - e.StartTime).ThenByDescending(e => e.StartTime).FirstOrDefault();
        if (carrier != null) carrier.PauseDurationMinutes = deficit;
    }

    /// <summary>
    /// The day's required break in whole minutes: every rule the work time exceeds asks for its
    /// pause, but never more than the time beyond its threshold (6 h 15 with "6 h → 30 min" is
    /// 15 min, so the net time stays 6 h), and the largest of those wins. Net time therefore never
    /// drops when more time is tracked (issue #76).
    /// </summary>
    public static int RequiredPauseMinutes(IEnumerable<(double MinHours, int PauseMinutes)> rules, double workHours) =>
        rules.Where(r => r.MinHours < workHours)
            .Select(r => Math.Min(r.PauseMinutes, (int)Math.Floor((workHours - r.MinHours) * 60 + 1e-9)))
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>Minutes between consecutive (possibly overlapping) entries, counting only gaps of at least 15 minutes.</summary>
    public static int CreditedGapMinutes(IReadOnlyList<(DateTime StartTime, DateTime EndTime)> sortedEntries)
    {
        var credited = 0.0;
        var coveredUntil = sortedEntries.Count > 0 ? sortedEntries[0].EndTime : default;
        foreach (var (start, end) in sortedEntries.Skip(1))
        {
            var gap = (start - coveredUntil).TotalMinutes;
            if (gap >= MinCreditedGapMinutes) credited += gap;
            if (end > coveredUntil) coveredUntil = end;
        }
        return (int)Math.Floor(credited);
    }
}
