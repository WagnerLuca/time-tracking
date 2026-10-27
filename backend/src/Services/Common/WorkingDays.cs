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

    /// <summary>
    /// The member's working days among <paramref name="days"/>, without full holidays. Without an
    /// organization: Monday–Friday.
    /// </summary>
    public static async Task<List<DateOnly>> FilterAsync(TimeTrackingDbContext context, int userId, int? orgId, IReadOnlyList<DateOnly> days)
    {
        if (orgId is not int id || days.Count == 0)
            return days.Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();

        var schedules = await context.WorkSchedules.AsNoTracking()
            .Where(ws => ws.UserId == userId && ws.OrganizationId == id)
            .ToListAsync();
        var org = await context.Organizations.AsNoTracking().FirstAsync(o => o.Id == id);
        var holidays = await HolidayCalendar.LoadAsync(context, org, [userId], days.Min(), days.Max());
        // A full holiday is no working day; a half holiday still is (its other half costs half a vacation day)
        return days.Where(d => holidays.On(userId, d, schedules) is not { IsHalfDay: false } && IsWorkingDay(schedules, d)).ToList();
    }
}
