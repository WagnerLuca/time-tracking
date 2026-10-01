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
    string EntrySummary,
    /// <summary>The absence counts against the vacation budget (vacation, or a custom type that counts as vacation).</summary>
    bool CountsAsVacation = false
);

/// <summary>
/// Builds day-by-day report rows for a user within an organization and date range.
/// Extracted from the original CSV daily-report logic so PDF reports use identical rules.
/// </summary>
public static class DailyReportAggregator
{
    /// <summary>Appended to a failure entry in <see cref="DailyReportRow.EntrySummary"/>; <see cref="ReportText.EntrySummary"/> translates it.</summary>
    public const string FailureMarker = " [not stopped, needs correction]";

    public static async Task<List<DailyReportRow>> BuildAsync(
        TimeTrackingDbContext context, int userId, int orgId,
        DateOnly dateFrom, DateOnly dateTo, double initialOvertimeHours, bool includeFuture = false)
    {
        // Days are the organization's local days (an entry at 00:30 Berlin belongs to that
        // day, not to the previous UTC day)
        var timeZone = await context.Organizations.AsNoTracking()
            .Where(o => o.Id == orgId).Select(o => o.TimeZone).FirstOrDefaultAsync();
        var clock = OrgClock.For(timeZone);

        // Only count days up to today — future days haven't happened yet and
        // would otherwise show as artificial "Missing" time with a misleading target.
        // (includeFuture: only for planned targets; such rows must not feed a balance.)
        var today = clock.Today();
        if (dateTo > today && !includeFuture) dateTo = today;
        if (dateFrom > dateTo) return [];

        var rangeFromUtc = clock.StartOfDayUtc(dateFrom);
        var rangeToUtc = clock.StartOfDayUtc(dateTo.AddDays(1));

        var timeEntries = await context.TimeEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.OrganizationId == orgId && !e.IsRunning
                     && e.StartTime >= rangeFromUtc && e.StartTime < rangeToUtc)
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
            .Include(a => a.CustomType)
            .Where(a => a.UserId == userId && a.OrganizationId == orgId
                     && a.Date >= dateFrom && a.Date <= dateTo)
            .ToListAsync();

        var entriesByDate = timeEntries
            .GroupBy(e => clock.LocalDate(e.StartTime))
            .ToDictionary(g => g.Key, g => g.ToList());

        var holidaysByDate = new Dictionary<DateOnly, Holiday>();
        foreach (var h in holidays)
        {
            for (var year = dateFrom.Year; year <= dateTo.Year; year++)
            {
                if (h.DateIn(year) is { } date && date >= dateFrom && date <= dateTo)
                    holidaysByDate.TryAdd(date, h);
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
            var allDayEntries = entriesByDate.GetValueOrDefault(currentDate) ?? [];
            // Failure entries (auto-stopped at business day end) count as 0 until corrected
            var dayEntries = allDayEntries.Where(e => !e.IsFailure).ToList();
            var hasFailures = dayEntries.Count < allDayEntries.Count;

            var grossMinutes = dayEntries.Sum(e => e.EndTime.HasValue
                ? (e.EndTime.Value - e.StartTime).TotalMinutes : 0);
            var pauseMinutes = dayEntries.Sum(e => e.PauseDurationMinutes);
            var netMinutes = Math.Max(0, grossMinutes - pauseMinutes);
            var netHours = netMinutes / 60.0;

            var isHoliday = holidaysByDate.TryGetValue(currentDate, out var holiday);
            var isAbsence = absencesByDate.TryGetValue(currentDate, out var absence);

            var holidayCredit = isHoliday ? (holiday!.IsHalfDay ? targetHours / 2.0 : targetHours) : 0;
            var absenceCredit = isAbsence ? AbsenceCredit(absence!, targetHours, netHours) : 0;
            var effectiveTarget = Math.Max(0, targetHours - holidayCredit - absenceCredit);
            string status;

            // An uncorrected forgotten timer is the one thing the reader must act on, so it wins
            if (hasFailures) { status = "Needs Correction"; }
            else if (isAbsence) { status = AbsenceStatus(absence!); }
            else if (isHoliday) { status = holiday!.IsHalfDay ? "Half Holiday" : "Holiday"; }
            else if (dayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) { status = "Weekend"; }
            else if (targetHours == 0) { status = "Day Off"; }
            else if (dayEntries.Count > 0) { status = "Worked"; }
            else { status = "Missing"; }

            var overtime = netHours - effectiveTarget;
            cumulativeOvertime += overtime;

            var entrySummary = allDayEntries.Count > 0
                ? string.Join(" | ", allDayEntries.Select(e =>
                    $"{clock.ToLocal(e.StartTime):HH:mm}-{(e.EndTime.HasValue ? clock.ToLocal(e.EndTime.Value).ToString("HH:mm") : "?")}" +
                    (e.IsFailure ? FailureMarker : "") +
                    (string.IsNullOrEmpty(e.Description) ? "" : $" ({e.Description})")))
                : "";

            rows.Add(new DailyReportRow(
                currentDate, dayOfWeek, effectiveTarget, grossMinutes / 60.0, pauseMinutes,
                netHours, overtime, cumulativeOvertime, status,
                isHoliday ? holiday!.Name : null,
                isAbsence ? absence!.Type : null,
                isAbsence ? absence!.Note : null,
                entrySummary,
                isAbsence && (absence!.Type == AbsenceType.Vacation || absence.CustomType?.CountsAsVacation == true)
            ));

            currentDate = currentDate.AddDays(1);
        }

        return rows;
    }

    /// <summary>
    /// Builds the report's display rows (clipped to [dateFrom, dateTo]) whose
    /// <see cref="DailyReportRow.CumulativeOvertime"/> reflects the member's true,
    /// never-reset running balance since their very first tracked entry — not just
    /// a total reset at the start of the requested period. Also returns that overall
    /// balance as of the end of the period (or "today" if the period is still ongoing,
    /// since <see cref="BuildAsync"/> clamps internally). Used by the PDF reports, the balance
    /// endpoint and the daily CSV, so all of them show the same running total.
    /// </summary>
    public static async Task<(List<DailyReportRow> Rows, double OverallCumulative)> BuildWithRunningTotalAsync(
        TimeTrackingDbContext context, int userId, int orgId,
        DateOnly dateFrom, DateOnly dateTo, double initialOvertimeHours)
    {
        var firstEntry = await context.TimeEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.OrganizationId == orgId && !e.IsRunning)
            .OrderBy(e => e.StartTime)
            .Select(e => (DateTime?)e.StartTime)
            .FirstOrDefaultAsync();

        // The balance runs from the first tracked entry: days before someone started tracking
        // are not "missing" work. Without any entry there is nothing to count yet.
        if (firstEntry == null)
            return ([], initialOvertimeHours);

        var timeZone = await context.Organizations.AsNoTracking()
            .Where(o => o.Id == orgId).Select(o => o.TimeZone).FirstOrDefaultAsync();
        var loopFrom = OrgClock.For(timeZone).LocalDate(firstEntry.Value);

        var allRows = await BuildAsync(context, userId, orgId, loopFrom, dateTo, initialOvertimeHours);
        var displayRows = allRows.Where(r => r.Date >= dateFrom).ToList();
        var overallCumulative = allRows.Count > 0 ? allRows[^1].CumulativeOvertime : initialOvertimeHours;

        return (displayRows, overallCumulative);
    }

    /// <summary>
    /// Hours an absence takes off the day's target. Vacation, sick days and plain "other"
    /// absences credit the (half) target; custom types follow their <see cref="AbsenceCreditMode"/>:
    /// None credits nothing, FullCredit the (half) target, FillToTarget only what is missing
    /// between the tracked hours and the (half) target.
    /// </summary>
    internal static double AbsenceCredit(AbsenceDay absence, double targetHours, double netWorkedHours)
    {
        var target = absence.IsHalfDay ? targetHours / 2.0 : targetHours;
        return absence.CustomType?.CreditMode switch
        {
            AbsenceCreditMode.None => 0,
            AbsenceCreditMode.FillToTarget => Math.Clamp(targetHours - netWorkedHours, 0, target),
            _ => target
        };
    }

    private static string AbsenceStatus(AbsenceDay absence) => absence.Type switch
    {
        AbsenceType.SickDay => "Sick",
        AbsenceType.Vacation => "Vacation",
        _ => absence.CustomType?.Name ?? "Absent"
    };

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
