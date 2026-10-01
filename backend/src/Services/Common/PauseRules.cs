using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

public static class PauseRules
{
    /// <summary>ArbZG §4: only breaks of at least 15 minutes count as rest breaks.</summary>
    public const int MinCreditedGapMinutes = 15;

    /// <summary>
    /// Sets the entry's automatic pause per organization-local day (ArbZG §4): the required break
    /// is that of the rule with the highest threshold the day's total work <em>exceeds</em> (gross
    /// time of all finished, non-failure entries of the user in that organization on the entry's
    /// local start day). Gaps of at least 15 minutes between those entries already are breaks, as
    /// are the pauses the day's other entries already carry; only the remaining deficit is put on
    /// <paramref name="entry"/>. The other entries are never changed, so a pause someone set by
    /// hand stays. No-op without an end time, without an organization or when auto-pause is off.
    /// Shared by stop, auto-stop on start, direct edits and approved edit requests.
    /// <para>
    /// ponytail: the day's other pauses are credited as they are, because the model cannot tell an
    /// automatic pause from a manual one. An earlier auto pause that a later gap makes superfluous
    /// therefore stays until that entry is recomputed; a <c>PauseIsManual</c> flag would fix it.
    /// </para>
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

        var others = await context.TimeEntries.AsNoTracking()
            .Where(e => e.Id != entry.Id && e.UserId == entry.UserId && e.OrganizationId == entry.OrganizationId
                     && !e.IsRunning && !e.IsFailure && e.EndTime != null
                     && e.StartTime >= dayStartUtc && e.StartTime < dayEndUtc)
            .Select(e => new { e.StartTime, EndTime = e.EndTime!.Value, e.PauseDurationMinutes })
            .ToListAsync();

        var dayEntries = others.Select(e => (e.StartTime, e.EndTime))
            .Append((StartTime: entry.StartTime, EndTime: entry.EndTime.Value))
            .OrderBy(e => e.StartTime)
            .ToList();
        var workHours = dayEntries.Sum(e => (e.EndTime - e.StartTime).TotalHours);

        var required = await context.PauseRules.AsNoTracking()
            .Where(pr => pr.OrganizationId == entry.OrganizationId && pr.MinHours < workHours)
            .OrderByDescending(pr => pr.MinHours)
            .Select(pr => (int?)pr.PauseMinutes)
            .FirstOrDefaultAsync() ?? 0;

        entry.PauseDurationMinutes = Math.Max(0, required - CreditedGapMinutes(dayEntries) - others.Sum(e => e.PauseDurationMinutes));
    }

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
