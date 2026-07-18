using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>
/// A single day's worked/target/overtime breakdown, combining time entries,
/// work schedules, holidays and absence days. Shared by CSV and PDF exports
/// so both use exactly the same business rules.
/// </summary>
public record DailyReportRow(
    DateOnly Date,
    DayOfWeek DayOfWeek,
    double TargetHours,
    double WorkedHours,
    int PauseMinutes,
    double NetWorkedHours,
    double Overtime,
    double CumulativeOvertime,
    string Status,
    string? HolidayName,
    AbsenceType? AbsenceType,
    string? AbsenceNote,
    string EntrySummary
);

/// <summary>
/// Builds day-by-day report rows for a user within an organization and date range.
/// Extracted from the original CSV daily-report logic so PDF reports use identical rules.
/// </summary>
public static class DailyReportAggregator
{
    public static async Task<List<DailyReportRow>> BuildAsync(
        TimeTrackingDbContext context, int userId, int orgId,
        DateOnly dateFrom, DateOnly dateTo, double initialOvertimeHours)
    {
        // Only count days up to today — future days haven't happened yet and
        // would otherwise show as artificial "Missing" time with a misleading target.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (dateTo > today) dateTo = today;
        if (dateFrom > dateTo) return [];

        var rangeFromUtc = dateFrom.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rangeToUtc = dateTo.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(1);

        var timeEntries = await context.TimeEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.OrganizationId == orgId && !e.IsRunning
                     && e.StartTime >= rangeFromUtc && e.StartTime <= rangeToUtc)
            .OrderBy(e => e.StartTime)
            .ToListAsync();

        var workSchedules = await context.WorkSchedules.AsNoTracking()
            .Where(ws => ws.UserId == userId && ws.OrganizationId == orgId)
            .OrderBy(ws => ws.ValidFrom)
            .ToListAsync();

        var holidays = await context.Holidays.AsNoTracking()
            .Where(h => h.OrganizationId == orgId
                     && ((h.Date >= dateFrom && h.Date <= dateTo) || h.IsRecurring))
            .ToListAsync();

        var absences = await context.AbsenceDays.AsNoTracking()
            .Where(a => a.UserId == userId && a.OrganizationId == orgId
                     && a.Date >= dateFrom && a.Date <= dateTo)
            .ToListAsync();

        var entriesByDate = timeEntries
            .GroupBy(e => DateOnly.FromDateTime(e.StartTime))
            .ToDictionary(g => g.Key, g => g.ToList());

        var holidaysByDate = new Dictionary<DateOnly, Holiday>();
        foreach (var h in holidays)
        {
            if (h.IsRecurring)
            {
                for (var year = dateFrom.Year; year <= dateTo.Year; year++)
                {
                    var recurringDate = new DateOnly(year, h.Date.Month, h.Date.Day);
                    if (recurringDate >= dateFrom && recurringDate <= dateTo)
                        holidaysByDate.TryAdd(recurringDate, h);
                }
            }
            else
            {
                holidaysByDate.TryAdd(h.Date, h);
            }
        }

        var absencesByDate = absences.ToDictionary(a => a.Date, a => a);

        var rows = new List<DailyReportRow>();
        var cumulativeOvertime = initialOvertimeHours;
        var currentDate = dateFrom;

        while (currentDate <= dateTo)
        {
            var dayOfWeek = currentDate.DayOfWeek;
            var targetHours = GetTargetHours(workSchedules, currentDate);
            var dayEntries = entriesByDate.GetValueOrDefault(currentDate) ?? [];

            var grossMinutes = dayEntries.Sum(e => e.EndTime.HasValue
                ? (e.EndTime.Value - e.StartTime).TotalMinutes : 0);
            var pauseMinutes = dayEntries.Sum(e => e.PauseDurationMinutes);
            var netMinutes = Math.Max(0, grossMinutes - pauseMinutes);
            var netHours = netMinutes / 60.0;

            var isHoliday = holidaysByDate.TryGetValue(currentDate, out var holiday);
            var isAbsence = absencesByDate.TryGetValue(currentDate, out var absence);

            var holidayCredit = isHoliday ? (holiday!.IsHalfDay ? targetHours / 2.0 : targetHours) : 0;
            var absenceCredit = isAbsence ? (absence!.IsHalfDay ? targetHours / 2.0 : targetHours) : 0;
            var effectiveTarget = Math.Max(0, targetHours - holidayCredit - absenceCredit);
            string status;

            if (isHoliday && isAbsence)
            {
                status = absence!.Type switch
                {
                    AbsenceType.SickDay => "Sick",
                    AbsenceType.Vacation => "Vacation",
                    _ => "Absent"
                };
            }
            else if (isHoliday) { status = holiday!.IsHalfDay ? "Half Holiday" : "Holiday"; }
            else if (isAbsence)
            {
                status = absence!.Type switch
                {
                    AbsenceType.SickDay => "Sick",
                    AbsenceType.Vacation => "Vacation",
                    _ => "Absent"
                };
            }
            else if (dayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) { status = "Weekend"; }
            else if (targetHours == 0) { status = "Day Off"; }
            else if (dayEntries.Count > 0) { status = "Worked"; }
            else { status = "Missing"; }

            var overtime = netHours - effectiveTarget;
            cumulativeOvertime += overtime;

            var entrySummary = dayEntries.Count > 0
                ? string.Join(" | ", dayEntries.Select(e =>
                    $"{e.StartTime:HH:mm}-{e.EndTime?.ToString("HH:mm") ?? "?"}" +
                    (string.IsNullOrEmpty(e.Description) ? "" : $" ({e.Description})")))
                : "";

            rows.Add(new DailyReportRow(
                currentDate, dayOfWeek, effectiveTarget, grossMinutes / 60.0, pauseMinutes,
                netHours, overtime, cumulativeOvertime, status,
                isHoliday ? holiday!.Name : null,
                isAbsence ? absence!.Type : null,
                isAbsence ? absence!.Note : null,
                entrySummary
            ));

            currentDate = currentDate.AddDays(1);
        }

        return rows;
    }

    private static double GetTargetHours(List<WorkSchedule> schedules, DateOnly date)
    {
        var schedule = schedules
            .Where(ws => ws.ValidFrom <= date && (ws.ValidTo == null || ws.ValidTo >= date))
            .OrderByDescending(ws => ws.ValidFrom)
            .FirstOrDefault();
        if (schedule == null) return 0;
        return date.DayOfWeek switch
        {
            DayOfWeek.Monday => schedule.TargetMon,
            DayOfWeek.Tuesday => schedule.TargetTue,
            DayOfWeek.Wednesday => schedule.TargetWed,
            DayOfWeek.Thursday => schedule.TargetThu,
            DayOfWeek.Friday => schedule.TargetFri,
            _ => 0
        };
    }
}
