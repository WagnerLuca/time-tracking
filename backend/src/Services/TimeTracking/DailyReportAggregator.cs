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
    bool CountsAsVacation = false,
    // The tracked time does not cover the day's core working hours (see <see cref="CoreHours"/>).
    bool CoreHoursMissed = false
);

/// <summary>
/// Builds day-by-day report rows for a user within an organization and date range.
/// Extracted from the original CSV daily-report logic so PDF reports use identical rules.
/// </summary>
public static class DailyReportAggregator
{
    /// <summary>Appended to a failure entry in <see cref="DailyReportRow.EntrySummary"/>; <see cref="ReportText.EntrySummary"/> translates it.</summary>
    public const string FailureMarker = " [not stopped, needs correction]";

    /// <param name="detailsFrom">
    /// Rows before this day only feed the running total: their <see cref="DailyReportRow.CoreHoursMissed"/> stays false
    /// and their <see cref="DailyReportRow.EntrySummary"/> empty, so a caller that reads those only for its display range
    /// (<see cref="BuildWithRunningTotalAsync"/>, the payroll history) does not re-evaluate core hours and format entries
    /// for the member's whole history. Null = every row (the default).
    /// </param>
    public static async Task<List<DailyReportRow>> BuildAsync(
        TimeTrackingDbContext context, int userId, int orgId,
        DateOnly dateFrom, DateOnly dateTo, double initialOvertimeHours, bool includeFuture = false, DateOnly? detailsFrom = null)
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

        var detailsStart = detailsFrom > dateFrom ? detailsFrom.Value : dateFrom;
        var coreHoursMissed = detailsStart <= dateTo
            ? await CoreHours.OpenViolationsAsync(context, userId, orgId, detailsStart, dateTo)
            : new Dictionary<DateOnly, CoreHours.Result>();
        return BuildRows(clock, dateFrom, dateTo, initialOvertimeHours, timeEntries, workSchedules, holidays, absences, coreHoursMissed,
            detailsStart);
    }

    /// <summary>
    /// The day loop of <see cref="BuildAsync"/> over already loaded data (entries: the member's finished
    /// entries in the range; schedules, holidays and absences: theirs/the org's). Shared with
    /// <see cref="BalancesAsync"/>, which loads the data of a whole team at once. Entry summaries are only built from
    /// <paramref name="summariesFrom"/> on (<see cref="DateOnly.MaxValue"/>: none, for callers that only need the figures).
    /// </summary>
    private static List<DailyReportRow> BuildRows(OrgClock clock, DateOnly dateFrom, DateOnly dateTo, double initialOvertimeHours,
        IEnumerable<TimeEntry> timeEntries, List<WorkSchedule> workSchedules, IEnumerable<Holiday> holidays,
        IEnumerable<AbsenceDay> absences, IReadOnlyDictionary<DateOnly, CoreHours.Result> coreHoursMissed, DateOnly summariesFrom)
    {
        var entriesByDate = timeEntries
            .OrderBy(e => e.StartTime)
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
            else if (dayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday && targetHours == 0) { status = "Weekend"; }
            else if (targetHours == 0) { status = "Day Off"; }
            else if (dayEntries.Count > 0) { status = "Worked"; }
            else { status = "Missing"; }

            var overtime = netHours - effectiveTarget;
            cumulativeOvertime += overtime;

            var entrySummary = allDayEntries.Count > 0 && currentDate >= summariesFrom
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
                isAbsence && (absence!.Type == AbsenceType.Vacation || absence.CustomType?.CountsAsVacation == true),
                coreHoursMissed.ContainsKey(currentDate)
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

        // Rows before dateFrom only feed the running total: no core-hours evaluation or entry summaries for them
        var allRows = await BuildAsync(context, userId, orgId, loopFrom, dateTo, initialOvertimeHours, detailsFrom: dateFrom);
        var displayRows = allRows.Where(r => r.Date >= dateFrom).ToList();
        var overallCumulative = allRows.Count > 0 ? allRows[^1].CumulativeOvertime : initialOvertimeHours;

        return (displayRows, overallCumulative);
    }

    /// <summary>
    /// <see cref="BuildWithRunningTotalAsync"/>'s overall balance (as of today) for several members at once —
    /// the same number the balance endpoint shows — in four queries for the whole team (entries, schedules,
    /// holidays, absences) instead of six per member. Also each member's number of failure entries started up to today
    /// (the team overview's count), taken from the same entries.
    /// </summary>
    /// <param name="runningOf">Members whose running timers (started before tomorrow) the caller wants as well — read in the same
    /// query and added to <paramref name="running"/> (the team overview's presence), so presence costs no query of its own.</param>
    public static async Task<Dictionary<int, (double Balance, int FailureCount)>> BalancesAsync(TimeTrackingDbContext context, Organization org,
        IReadOnlyDictionary<int, double> initialOvertimeByUser, IReadOnlyCollection<int>? runningOf = null, List<TimeEntry>? running = null)
    {
        var today = OrgClock.For(org).Today();
        var totals = await TotalsAsync(context, org, initialOvertimeByUser, today, today, runningOf, running);
        return totals.ToDictionary(t => t.Key, t => (t.Value.Balance, t.Value.FailureCount));
    }

    /// <summary>
    /// Target, net worked and overtime hours of a period and the overtime balance at its end, as summed from the report rows;
    /// <see cref="FailureCount"/>: failure entries started up to the period end (whole history, not only the period).
    /// </summary>
    public readonly record struct PeriodTotals(double Target, double Worked, double Overtime, double Balance, int FailureCount = 0);

    /// <summary>
    /// For several members at once: the sums of <see cref="DailyReportRow.TargetHours"/>, <see cref="DailyReportRow.NetWorkedHours"/>
    /// and <see cref="DailyReportRow.Overtime"/> over the rows from <paramref name="periodFrom"/> to <paramref name="to"/> (local days,
    /// clamped to today) and the running balance at the last row — exactly what per member
    /// <c>BuildAsync(firstEntryDay, to, initial)</c> gives (rows from the member's first finished entry on; nothing before it counts,
    /// and without any entry the balance is the initial overtime). Four queries for the whole team (finished entries, schedules,
    /// holidays, absences — only the columns the figures need), independent of the member count; the history is still read in full
    /// (balance checkpoints come in Phase 2 of docs/PERFORMANCE_PLAN.md).
    /// </summary>
    public static async Task<Dictionary<int, PeriodTotals>> TotalsAsync(TimeTrackingDbContext context, Organization org,
        IReadOnlyDictionary<int, double> initialOvertimeByUser, DateOnly periodFrom, DateOnly to,
        IReadOnlyCollection<int>? runningOf = null, List<TimeEntry>? running = null)
    {
        var userIds = initialOvertimeByUser.Keys.ToList();
        var clock = OrgClock.For(org);
        var today = clock.Today();
        if (to > today) to = today;
        var result = userIds.ToDictionary(id => id, id => new PeriodTotals(0, 0, 0, initialOvertimeByUser[id]));
        List<int> runningIds = running == null || runningOf == null ? [] : runningOf.ToList();
        if (userIds.Count == 0 && runningIds.Count == 0) return result;
        var toUtc = clock.StartOfDayUtc(to.AddDays(1));
        var tomorrowUtc = clock.StartOfDayUtc(today.AddDays(1));

        var loaded = await context.TimeEntries.AsNoTracking()
            .Where(e => e.OrganizationId == org.Id
                     && ((!e.IsRunning && userIds.Contains(e.UserId) && e.StartTime < toUtc)
                         || (e.IsRunning && runningIds.Contains(e.UserId) && e.StartTime < tomorrowUtc)))
            .Select(e => new TimeEntry
            {
                UserId = e.UserId, StartTime = e.StartTime, EndTime = e.EndTime, IsRunning = e.IsRunning,
                IsFailure = e.IsFailure, PauseDurationMinutes = e.PauseDurationMinutes
            })
            .ToListAsync();
        running?.AddRange(loaded.Where(e => e.IsRunning));
        var finished = loaded.Where(e => !e.IsRunning).ToLookup(e => e.UserId);
        var firstDay = finished.ToDictionary(g => g.Key, g => clock.LocalDate(g.Min(e => e.StartTime)));
        if (firstDay.Count == 0) return result;
        // Failure entries are never running, so these are all of them up to the end
        foreach (var g in finished)
            result[g.Key] = result[g.Key] with { FailureCount = g.Count(e => e.IsFailure) };
        var from = firstDay.Values.Min();

        var schedules = (await context.WorkSchedules.AsNoTracking()
                .Where(ws => ws.OrganizationId == org.Id && userIds.Contains(ws.UserId))
                .OrderBy(ws => ws.ValidFrom)
                .ToListAsync())
            .ToLookup(ws => ws.UserId);
        var holidays = await context.Holidays.AsNoTracking()
            .Where(h => h.OrganizationId == org.Id && ((h.Date >= from && h.Date <= to) || h.IsRecurring))
            .ToListAsync();
        var absences = (await context.AbsenceDays.AsNoTracking()
                .Include(a => a.CustomType)
                .Where(a => a.OrganizationId == org.Id && userIds.Contains(a.UserId) && a.Date >= from && a.Date <= to)
                .ToListAsync())
            .ToLookup(a => a.UserId);

        var noViolations = new Dictionary<DateOnly, CoreHours.Result>();
        foreach (var (userId, start) in firstDay)
        {
            if (start > to) continue;
            var rows = BuildRows(clock, start, to, initialOvertimeByUser[userId], finished[userId],
                schedules[userId].ToList(), holidays, absences[userId].Where(a => a.Date >= start), noViolations, DateOnly.MaxValue);
            if (rows.Count == 0) continue;
            var period = rows.Where(r => r.Date >= periodFrom).ToList();
            result[userId] = new PeriodTotals(period.Sum(r => r.TargetHours), period.Sum(r => r.NetWorkedHours),
                period.Sum(r => r.Overtime), rows[^1].CumulativeOvertime, result[userId].FailureCount);
        }
        return result;
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
        // The latest schedule valid on the day (the first of equal ValidFroms, as a stable sort would pick);
        // a plain loop because it runs once per day of a member's whole history
        WorkSchedule? schedule = null;
        foreach (var ws in schedules)
            if (ws.ValidFrom <= date && (ws.ValidTo == null || ws.ValidTo >= date)
                && (schedule == null || ws.ValidFrom > schedule.ValidFrom))
                schedule = ws;
        if (schedule == null) return 0;
        return date.DayOfWeek switch
        {
            DayOfWeek.Monday => schedule.TargetMon,
            DayOfWeek.Tuesday => schedule.TargetTue,
            DayOfWeek.Wednesday => schedule.TargetWed,
            DayOfWeek.Thursday => schedule.TargetThu,
            DayOfWeek.Friday => schedule.TargetFri,
            DayOfWeek.Saturday => schedule.TargetSat,
            DayOfWeek.Sunday => schedule.TargetSun,
            _ => 0
        };
    }
}
