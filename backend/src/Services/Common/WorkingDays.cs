using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>
/// Which days a member actually works: the basis for counting vacation and other absence ranges.
/// </summary>
public static class WorkingDays
{
    /// <summary>Target &gt; 0 in the schedule valid that day; Monday–Friday when no schedule applies.</summary>
    public static bool IsWorkingDay(IEnumerable<WorkSchedule> schedules, DateOnly date)
    {
        var schedule = schedules
            .Where(ws => ws.ValidFrom <= date && (ws.ValidTo == null || ws.ValidTo >= date))
            .MaxBy(ws => ws.ValidFrom);
        return schedule != null
            ? schedule.TargetFor(date.DayOfWeek) > 0
            : date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    /// <summary>The organization's holiday dates (recurring ones resolved) between the two days.</summary>
    public static async Task<HashSet<DateOnly>> HolidayDatesAsync(TimeTrackingDbContext context, int orgId, DateOnly from, DateOnly to)
    {
        var holidays = await context.Holidays.AsNoTracking()
            .Where(h => h.OrganizationId == orgId && (h.IsRecurring || (h.Date >= from && h.Date <= to)))
            .ToListAsync();
        var dates = new HashSet<DateOnly>();
        for (var year = from.Year; year <= to.Year; year++)
            foreach (var h in holidays)
                if (h.DateIn(year) is { } d && d >= from && d <= to) dates.Add(d);
        return dates;
    }

    /// <summary>
    /// The member's working days among <paramref name="days"/>, without holidays. Without an
    /// organization: Monday–Friday.
    /// </summary>
    public static async Task<List<DateOnly>> FilterAsync(TimeTrackingDbContext context, int userId, int? orgId, IReadOnlyList<DateOnly> days)
    {
        if (orgId is not int id || days.Count == 0)
            return days.Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();

        var schedules = await context.WorkSchedules.AsNoTracking()
            .Where(ws => ws.UserId == userId && ws.OrganizationId == id)
            .ToListAsync();
        var holidays = await HolidayDatesAsync(context, id, days.Min(), days.Max());
        return days.Where(d => !holidays.Contains(d) && IsWorkingDay(schedules, d)).ToList();
    }
}
