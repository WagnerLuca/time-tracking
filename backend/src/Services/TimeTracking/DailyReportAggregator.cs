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
    bool CoreHoursMissed = false,
    /// <summary>The schedule's target less the holiday credit (before absences; the calendar's day target).</summary>
    double DayTargetHours = 0,
    /// <summary>Hours the absence took off the target.</summary>
    double CreditHours = 0,
    /// <summary>The running overtime sum without the initial overtime; null for a day before the balance started.</summary>
    double? OvertimeDelta = null,
    /// <summary>Failure entries started that day.</summary>
    int FailureCount = 0,
    bool AbsenceHalfDay = false,
    /// <summary>The day lies in a closed month and comes from its frozen figures (<see cref="FrozenFigures"/>).</summary>
    bool Frozen = false
)
{
    /// <summary>The day counts in the period sums and the balance (false for a day before the balance started).</summary>
    public bool Counted => OvertimeDelta != null;
}

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
        return await BuildAsync(context, OrgClock.For(timeZone), userId, orgId, dateFrom, dateTo, initialOvertimeHours,
            includeFuture, detailsFrom);
    }

    /// <summary>
    /// <see cref="BuildAsync(TimeTrackingDbContext, int, int, DateOnly, DateOnly, double, bool, DateOnly?)"/> with the org's
    /// clock already known, continuing a running total: the rows' overtime sum starts at <paramref name="startDelta"/> (a
    /// <see cref="BalanceCheckpoint.OvertimeDelta"/>) and <paramref name="monthEnds"/> collects it at every month's last day.
    /// </summary>
    internal static async Task<List<DailyReportRow>> BuildAsync(
        TimeTrackingDbContext context, OrgClock clock, int userId, int orgId,
        DateOnly dateFrom, DateOnly dateTo, double initialOvertimeHours, bool includeFuture = false, DateOnly? detailsFrom = null,
        double startDelta = 0, List<(DateOnly Day, double Delta)>? monthEnds = null, List<WorkSchedule>? schedules = null,
        IReadOnlyDictionary<DateOnly, FrozenDay>? frozen = null, DateOnly? balanceFrom = null)
    {
        // Only count days up to today — future days haven't happened yet and
        // would otherwise show as artificial "Missing" time with a misleading target.
        // (includeFuture: only for planned targets; such rows must not feed a balance.)
        var today = clock.Today();
        if (dateTo > today && !includeFuture) dateTo = today;
        if (dateFrom > dateTo) return [];

        // Days of closed months are read as frozen (FrozenFigures), never computed — a range of closed days loads nothing else
        frozen ??= (await FrozenFigures.DaysAsync(context, clock, orgId, [userId], dateFrom, dateTo)).GetValueOrDefault(userId);
        if (FrozenFigures.Covers(frozen, dateFrom, dateTo))
            return BuildRows(clock, dateFrom, dateTo, initialOvertimeHours, [], [], [], [], NoViolations, dateFrom,
                startDelta, monthEnds, frozen, balanceFrom);

        var rangeFromUtc = clock.StartOfDayUtc(dateFrom);
        var rangeToUtc = clock.StartOfDayUtc(dateTo.AddDays(1));

        var timeEntries = await context.TimeEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.OrganizationId == orgId && !e.IsRunning
                     && e.StartTime >= rangeFromUtc && e.StartTime < rangeToUtc)
            .OrderBy(e => e.StartTime)
            .ToListAsync();

        var workSchedules = schedules ?? await context.WorkSchedules.AsNoTracking()
            .Where(ws => ws.UserId == userId && ws.OrganizationId == orgId)
            .OrderBy(ws => ws.ValidFrom)
            .ToListAsync();

        var holidays = await context.Holidays.AsNoTracking()
            .Where(h => h.OrganizationId == orgId
                     && ((h.Date >= dateFrom && h.Date <= dateTo) || h.IsRecurring))
            // A one-off holiday wins over a recurring one on the same day (BuildRows keeps the first per date)
            .OrderBy(h => h.IsRecurring).ThenBy(h => h.Id)
            .ToListAsync();

        var absences = await context.AbsenceDays.AsNoTracking()
            .Include(a => a.CustomType)
            .Where(a => a.UserId == userId && a.OrganizationId == orgId
                     && a.Date >= dateFrom && a.Date <= dateTo)
            .ToListAsync();

        var detailsStart = detailsFrom > dateFrom ? detailsFrom.Value : dateFrom;
        var coreHoursMissed = detailsStart <= dateTo
            ? await CoreHours.OpenViolationsAsync(context, userId, orgId, detailsStart, dateTo)
            : NoViolations;
        return BuildRows(clock, dateFrom, dateTo, initialOvertimeHours, timeEntries, workSchedules, holidays, absences, coreHoursMissed,
            detailsStart, startDelta, monthEnds, frozen, balanceFrom);
    }

    /// <summary>
    /// The day loop of <c>BuildAsync</c> over already loaded data (entries: the member's finished
    /// entries in the range; schedules, holidays and absences: theirs/the org's). Shared with
    /// <see cref="BalancesAsync"/>, which loads the data of a whole team at once. Entry summaries are only built from
    /// <paramref name="summariesFrom"/> on (<see cref="DateOnly.MaxValue"/>: none, for callers that only need the figures).
    /// <para>
    /// The running total is kept as the overtime sum since the first counted day (<paramref name="startDelta"/> when continuing
    /// from a <see cref="BalanceCheckpoint"/>, else 0) and each row's <see cref="DailyReportRow.CumulativeOvertime"/> is
    /// <c>initialOvertimeHours + sum</c> — so a total continued from a checkpoint is bit for bit the one computed from the
    /// first day (the initial overtime is not part of the checkpoint). <paramref name="monthEnds"/> receives the sum at
    /// every month's last day (the checkpoints to write).
    /// </para>
    /// </summary>
    /// <param name="balanceFrom">Days before it are listed but do not move the running total (their
    /// <see cref="DailyReportRow.OvertimeDelta"/> is null, the cumulative figure the initial overtime). Null = <paramref name="dateFrom"/>.</param>
    /// <param name="frozen">The member's frozen days (<see cref="FrozenFigures"/>): such a day is taken as stored — figures,
    /// texts and its cumulative balance — and the running total continues from its stored sum. Days of closed months are
    /// never computed.</param>
    internal static List<DailyReportRow> BuildRows(OrgClock clock, DateOnly dateFrom, DateOnly dateTo, double initialOvertimeHours,
        IEnumerable<TimeEntry> timeEntries, List<WorkSchedule> workSchedules, IEnumerable<Holiday> holidays,
        IEnumerable<AbsenceDay> absences, IReadOnlyDictionary<DateOnly, CoreHours.Result> coreHoursMissed, DateOnly summariesFrom,
        double startDelta = 0, List<(DateOnly Day, double Delta)>? monthEnds = null,
        IReadOnlyDictionary<DateOnly, FrozenDay>? frozen = null, DateOnly? balanceFrom = null)
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
        var overtimeSum = startDelta;
        var counting = balanceFrom ?? dateFrom;
        var currentDate = dateFrom;

        while (currentDate <= dateTo)
        {
            if (frozen != null && frozen.TryGetValue(currentDate, out var f))
            {
                // Before the balance started (when frozen) it held nothing: a later start restarts after the month
                overtimeSum = f.OvertimeDelta ?? 0;
                rows.Add(FrozenFigures.ToRow(f));
            }
            else
                rows.Add(BuildDay(clock, currentDate, workSchedules, entriesByDate, holidaysByDate, absencesByDate, coreHoursMissed,
                    summariesFrom, initialOvertimeHours, currentDate >= counting, ref overtimeSum));
            if (monthEnds != null && currentDate.AddDays(1).Day == 1) monthEnds.Add((currentDate, overtimeSum));
            currentDate = currentDate.AddDays(1);
        }

        return rows;
    }

    private static DailyReportRow BuildDay(OrgClock clock, DateOnly currentDate, List<WorkSchedule> workSchedules,
        Dictionary<DateOnly, List<TimeEntry>> entriesByDate, Dictionary<DateOnly, Holiday> holidaysByDate,
        Dictionary<DateOnly, AbsenceDay> absencesByDate, IReadOnlyDictionary<DateOnly, CoreHours.Result> coreHoursMissed,
        DateOnly summariesFrom, double initialOvertimeHours, bool counted, ref double overtimeSum)
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
        if (counted) overtimeSum += overtime;

        var entrySummary = allDayEntries.Count > 0 && currentDate >= summariesFrom
            ? string.Join(" | ", allDayEntries.Select(e =>
                $"{clock.ToLocal(e.StartTime):HH:mm}-{(e.EndTime.HasValue ? clock.ToLocal(e.EndTime.Value).ToString("HH:mm") : "?")}" +
                (e.IsFailure ? FailureMarker : "") +
                (string.IsNullOrEmpty(e.Description) ? "" : $" ({e.Description})")))
            : "";

        return new DailyReportRow(
            currentDate, dayOfWeek, effectiveTarget, grossMinutes / 60.0, pauseMinutes,
            netHours, overtime, counted ? initialOvertimeHours + overtimeSum : initialOvertimeHours, status,
            isHoliday ? holiday!.Name : null,
            isAbsence ? absence!.Type : null,
            isAbsence ? absence!.Note : null,
            entrySummary,
            isAbsence && (absence!.Type == AbsenceType.Vacation || absence.CustomType?.CountsAsVacation == true),
            coreHoursMissed.ContainsKey(currentDate),
            DayTargetHours: targetHours - holidayCredit,
            CreditHours: absenceCredit,
            OvertimeDelta: counted ? overtimeSum : null,
            FailureCount: allDayEntries.Count - dayEntries.Count,
            AbsenceHalfDay: isAbsence && absence!.IsHalfDay);
    }

    /// <summary>
    /// Builds the report's display rows (clipped to [dateFrom, dateTo]) whose
    /// <see cref="DailyReportRow.CumulativeOvertime"/> reflects the member's true,
    /// never-reset running balance since their very first tracked entry — not just
    /// a total reset at the start of the requested period. Also returns that overall
    /// balance as of the end of the period (or "today" if the period is still ongoing,
    /// since <see cref="BuildAsync(TimeTrackingDbContext, int, int, DateOnly, DateOnly, double, bool, DateOnly?)"/> clamps
    /// internally). Used by the PDF reports, the balance endpoint and the daily CSV, so all of them show the same running total.
    /// The history before <paramref name="dateFrom"/> comes from the latest valid <see cref="BalanceCheckpoint"/> (the
    /// rows from there on are computed and the months that ended in them stored as new checkpoints), so the cost does not
    /// grow with the member's years — the figures are exactly those of a computation from the first entry.
    /// </summary>
    /// <param name="allDays">Also list the days of the range before the balance started (the daily CSV): they do not move the
    /// running total and carry the initial overtime as their cumulative figure. Without it rows start at the balance start.</param>
    public static async Task<(List<DailyReportRow> Rows, double OverallCumulative)> BuildWithRunningTotalAsync(
        TimeTrackingDbContext context, int userId, int orgId,
        DateOnly dateFrom, DateOnly dateTo, double initialOvertimeHours, bool allDays = false)
    {
        var timeZone = await context.Organizations.AsNoTracking()
            .Where(o => o.Id == orgId).Select(o => o.TimeZone).FirstOrDefaultAsync();
        var clock = OrgClock.For(timeZone);

        // The balance runs from the first tracked entry: days before someone started tracking
        // are not "missing" work. Without any entry there is nothing to count yet.
        var (members, stamp) = await BalanceCheckpoints.LoadAsync(context, orgId, clock, [userId], dateFrom, NoFailures);
        if (!members.TryGetValue(userId, out var member))
        {
            // Not even a former member (no membership row): no checkpoints, the whole history as before them
            var firstEntry = await context.TimeEntries.AsNoTracking()
                .Where(e => e.UserId == userId && e.OrganizationId == orgId && !e.IsRunning)
                .MinAsync(e => (DateTime?)e.StartTime);
            BalanceCheckpoints.Start? first = null;
            if (firstEntry != null)
            {
                var firstDay = clock.LocalDate(firstEntry.Value);
                first = new BalanceCheckpoints.Start(firstDay, 0, firstDay);
            }
            member = new BalanceCheckpoints.Member(first, null!, 0);
        }
        var start = member.Start;
        if (start == null && !allDays)
            return ([], initialOvertimeHours);

        // Rows before dateFrom only feed the running total: no core-hours evaluation or entry summaries for them
        var from = start == null || (allDays && dateFrom < start.From) ? dateFrom : start.From;
        // Closed months of the range are read as frozen; between the start and dateFrom's month there are none (the start is
        // after the latest frozen month before it)
        var frozen = member.FrozenFrom <= dateTo
            ? (await FrozenFigures.DaysAsync(context, clock, orgId, [userId], ClosedPeriods.MonthOf(dateFrom), dateTo)).GetValueOrDefault(userId)
            : null;
        var monthEnds = new List<(DateOnly Day, double Delta)>();
        var allRows = await BuildAsync(context, clock, userId, orgId, from, dateTo, initialOvertimeHours,
            detailsFrom: dateFrom, startDelta: start?.Delta ?? 0, monthEnds: monthEnds, schedules: member.Schedules,
            frozen: frozen ?? NoFrozenDays, balanceFrom: start?.From ?? DateOnly.MaxValue);
        if (start != null) await BalanceCheckpoints.SaveAsync(context, stamp, timeZone, [(userId, member, monthEnds)]);
        var displayRows = allRows.Where(r => r.Date >= dateFrom).ToList();
        var overallCumulative = allRows.Count > 0 ? allRows[^1].CumulativeOvertime : initialOvertimeHours;

        return (displayRows, overallCumulative);
    }

    private static readonly IReadOnlyDictionary<DateOnly, FrozenDay> NoFrozenDays = new Dictionary<DateOnly, FrozenDay>();
    private static readonly IReadOnlyDictionary<DateOnly, CoreHours.Result> NoViolations = new Dictionary<DateOnly, CoreHours.Result>();

    /// <summary>
    /// <see cref="BuildWithRunningTotalAsync"/>'s overall balance (as of today) for several members at once —
    /// the same number the balance endpoint shows — for the whole team in four queries (memberships with their checkpoint,
    /// first entry, failure count and schedules; entries; holidays; absences) instead of six per member.
    /// Also each member's number of failure entries started up to today (the team overview's count).
    /// </summary>
    /// <param name="runningOf">Members whose running timers (started before tomorrow) the caller wants as well — read in the same
    /// query and added to <paramref name="running"/> (the team overview's presence), so presence costs no query of its own.</param>
    public static async Task<Dictionary<int, (double Balance, int FailureCount)>> BalancesAsync(TimeTrackingDbContext context, Organization org,
        IReadOnlyDictionary<int, double> initialOvertimeByUser, IReadOnlyCollection<int>? runningOf = null, List<TimeEntry>? running = null)
    {
        var today = OrgClock.For(org).Today();
        var totals = await TotalsAsync(context, org, initialOvertimeByUser, today, today, runningOf, running, failureCounts: true);
        return totals.ToDictionary(t => t.Key, t => (t.Value.Balance, t.Value.FailureCount));
    }

    /// <summary>
    /// Target, net worked and overtime hours of a period and the overtime balance at its end, as summed from the report rows;
    /// <see cref="FailureCount"/>: failure entries started up to the period end (whole history, not only the period).
    /// </summary>
    /// <summary>A failure-count bound no entry is before (callers that do not need the counts).</summary>
    private static readonly DateTime NoFailures = DateTime.SpecifyKind(new DateTime(1900, 1, 1), DateTimeKind.Utc);

    public readonly record struct PeriodTotals(double Target, double Worked, double Overtime, double Balance, int FailureCount = 0);

    /// <summary>
    /// For several members at once: the sums of <see cref="DailyReportRow.TargetHours"/>, <see cref="DailyReportRow.NetWorkedHours"/>
    /// and <see cref="DailyReportRow.Overtime"/> over the rows from <paramref name="periodFrom"/> to <paramref name="to"/> (local days,
    /// clamped to today) and the running balance at the last row — exactly what per member
    /// <c>BuildAsync(firstEntryDay, to, initial)</c> gives (rows from the member's first finished entry on; nothing before it counts,
    /// and without any entry the balance is the initial overtime). A fixed number of queries for the whole team, independent of
    /// the member count; the history before <paramref name="periodFrom"/> comes from each member's latest valid
    /// <see cref="BalanceCheckpoint"/>, so only the rows since then are computed (and the months that ended in them stored).
    /// <see cref="PeriodTotals.FailureCount"/> is only counted with <paramref name="failureCounts"/>.
    /// </summary>
    public static async Task<Dictionary<int, PeriodTotals>> TotalsAsync(TimeTrackingDbContext context, Organization org,
        IReadOnlyDictionary<int, double> initialOvertimeByUser, DateOnly periodFrom, DateOnly to,
        IReadOnlyCollection<int>? runningOf = null, List<TimeEntry>? running = null, bool failureCounts = true,
        Dictionary<int, Dictionary<DateOnly, FrozenDay>>? frozenDays = null)
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

        Dictionary<int, BalanceCheckpoints.Member> members = [];
        BalanceCheckpoints.Stamp? stamp = null;
        if (userIds.Count > 0)
            (members, stamp) = await BalanceCheckpoints.LoadAsync(context, org.Id, clock, userIds, periodFrom,
                failureCounts ? toUtc : NoFailures);
        foreach (var (userId, member) in members)
            result[userId] = result[userId] with { FailureCount = member.FailureCount };
        var starts = members.Where(m => m.Value.Start is { } s && s.From <= to).ToDictionary(m => m.Key, m => m.Value.Start!);
        var startIds = starts.Keys.ToList();

        // Closed months of the period are read as frozen (the caller may have loaded them already); members whose rows are all
        // frozen need no source data
        var withFrozen = startIds.Where(id => members[id].FrozenFrom <= to).ToList();
        frozenDays ??= withFrozen.Count == 0 ? []
            : await FrozenFigures.DaysAsync(context, clock, org.Id, withFrozen, ClosedPeriods.MonthOf(periodFrom), to);
        var liveIds = startIds.Where(id => !FrozenFigures.Covers(frozenDays.GetValueOrDefault(id), starts[id].From, to)).ToList();
        var from = liveIds.Count > 0 ? liveIds.Min(id => starts[id].From) : to;
        var fromUtc = clock.StartOfDayUtc(from);

        // Finished entries from the earliest start on (only of members with rows to build), running timers for presence
        var loaded = liveIds.Count == 0 && runningIds.Count == 0 ? [] : await context.TimeEntries.AsNoTracking()
            .Where(e => e.OrganizationId == org.Id
                     && ((!e.IsRunning && liveIds.Contains(e.UserId) && e.StartTime >= fromUtc && e.StartTime < toUtc)
                         || (e.IsRunning && runningIds.Contains(e.UserId) && e.StartTime < tomorrowUtc)))
            .Select(e => new TimeEntry
            {
                UserId = e.UserId, StartTime = e.StartTime, EndTime = e.EndTime, IsRunning = e.IsRunning,
                IsFailure = e.IsFailure, PauseDurationMinutes = e.PauseDurationMinutes
            })
            .ToListAsync();
        running?.AddRange(loaded.Where(e => e.IsRunning));

        // A member without a balance (yet) whose period ends in a closed month: the balance frozen with it
        foreach (var (userId, days) in frozenDays)
            if (!starts.ContainsKey(userId) && result.ContainsKey(userId) && days.TryGetValue(to, out var last))
                result[userId] = result[userId] with { Balance = last.CumulativeOvertime };
        if (starts.Count == 0) return result;
        var finished = loaded.Where(e => !e.IsRunning).ToLookup(e => e.UserId);

        var holidays = liveIds.Count == 0 ? [] : await context.Holidays.AsNoTracking()
            .Where(h => h.OrganizationId == org.Id && ((h.Date >= from && h.Date <= to) || h.IsRecurring))
            .OrderBy(h => h.IsRecurring).ThenBy(h => h.Id)
            .ToListAsync();
        var absences = (liveIds.Count == 0 ? [] : await context.AbsenceDays.AsNoTracking()
                .Include(a => a.CustomType)
                .Where(a => a.OrganizationId == org.Id && liveIds.Contains(a.UserId) && a.Date >= from && a.Date <= to)
                .ToListAsync())
            .ToLookup(a => a.UserId);

        var noViolations = new Dictionary<DateOnly, CoreHours.Result>();
        var computed = new List<(int UserId, BalanceCheckpoints.Member Member, List<(DateOnly Day, double Delta)> MonthEnds)>();
        foreach (var (userId, start) in starts)
        {
            var monthEnds = new List<(DateOnly Day, double Delta)>();
            var rows = BuildRows(clock, start.From, to, initialOvertimeByUser[userId], finished[userId],
                members[userId].Schedules, holidays, absences[userId].Where(a => a.Date >= start.From), noViolations, DateOnly.MaxValue,
                start.Delta, monthEnds, frozenDays.GetValueOrDefault(userId));
            computed.Add((userId, members[userId], monthEnds));
            if (rows.Count == 0) continue;
            var period = rows.Where(r => r.Date >= periodFrom && r.Counted).ToList();
            result[userId] = new PeriodTotals(period.Sum(r => r.TargetHours), period.Sum(r => r.NetWorkedHours),
                period.Sum(r => r.Overtime), rows[^1].CumulativeOvertime, result[userId].FailureCount);
        }
        await BalanceCheckpoints.SaveAsync(context, stamp!, org.TimeZone, computed);
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
