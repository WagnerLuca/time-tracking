using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// The work schedule model (issue #82): a member has the <b>current</b> period (valid today), at most one <b>future</b> one
/// (starting after today; from its first day it is the current one) and the past periods as read-only history. Periods are
/// contiguous and never overlap; only the last one is open-ended. Every write goes through here: a change creates the next
/// period from a date (closing the one before it the day before; replacing a future one), an admin correction edits the
/// current one, the future one can be cancelled. The closed-period guard and frozen months (#81) keep closed months as they were.
/// </summary>
public static class WorkSchedulePlan
{
    public const double MaxWeeklyHours = 80;
    public const double MaxDailyHours = 24;

    /// <summary>A member's periods split as the UI shows them.</summary>
    public sealed record State(WorkSchedule? Current, WorkSchedule? Future, List<WorkSchedule> History);

    public static State Split(IReadOnlyList<WorkSchedule> periods, DateOnly today)
    {
        var ordered = periods.OrderBy(p => p.ValidFrom).ToList();
        var current = ordered.LastOrDefault(p => p.ValidFrom <= today && (p.ValidTo == null || p.ValidTo >= today));
        var future = ordered.FirstOrDefault(p => p.ValidFrom > today);
        var history = ordered.Where(p => p != current && p != future && p.ValidFrom <= today).OrderByDescending(p => p.ValidFrom).ToList();
        return new State(current, future, history);
    }

    /// <summary>Tracked periods of a member, ordered by start.</summary>
    internal static Task<List<WorkSchedule>> LoadAsync(TimeTrackingDbContext db, int userId, int orgId) =>
        db.WorkSchedules.Where(s => s.UserId == userId && s.OrganizationId == orgId).OrderBy(s => s.ValidFrom).ToListAsync();

    /// <summary>The targets of a change/correction; null = valid, else (message, code).</summary>
    internal static (string Message, string Code)? ValidateTargets(ScheduleTargets t)
    {
        if (t.WeeklyWorkHours is { } w && (double.IsNaN(w) || w < 0 || w > MaxWeeklyHours))
            return ($"Weekly hours must be between 0 and {MaxWeeklyHours}.", "schedule.weeklyRange");
        if (!(t.DistributeEvenly && t.WeeklyWorkHours.HasValue)
            && new[] { t.TargetMon, t.TargetTue, t.TargetWed, t.TargetThu, t.TargetFri, t.TargetSat, t.TargetSun }
                .Any(d => d is { } v && (double.IsNaN(v) || v < 0 || v > MaxDailyHours)))
            return ($"Daily hours must be between 0 and {MaxDailyHours}.", "schedule.dailyRange");
        if (t.HolidayRegion != null && HolidayPresets.Normalize(t.HolidayRegion) == null)
            return (HolidayPresets.UnknownRegionMessage, "holiday.unknownRegion");
        if (t.VacationDaysPerYear is { } days && (!double.IsFinite(days) || days < 0 || days > 365))
            return (VacationDaysRangeMessage, "schedule.vacationDaysRange");
        return null;
    }

    public const string VacationDaysRangeMessage = "Vacation days per year must be between 0 and 365.";

    /// <summary>
    /// The contract terms of a period (Arbeitsort's holiday region, vacation days per year): a new period keeps the ones of the
    /// period before it; only an admin (<paramref name="admin"/>) names other ones ("" region = none), so a member's own
    /// schedule change never moves their Arbeitsort.
    /// </summary>
    internal static void ApplyContract(WorkSchedule s, WorkSchedule? previous, ScheduleTargets t, bool admin)
    {
        if (previous != null) (s.HolidayRegion, s.VacationDaysPerYear) = (previous.HolidayRegion, previous.VacationDaysPerYear);
        if (!admin) return;
        if (t.HolidayRegion != null) s.HolidayRegion = HolidayPresets.Normalize(t.HolidayRegion) is { Length: > 0 } key ? key : null;
        if (t.VacationDaysPerYear != null) s.VacationDaysPerYear = t.VacationDaysPerYear;
    }

    internal static void ApplyTargets(WorkSchedule s, ScheduleTargets t)
    {
        s.WeeklyWorkHours = t.WeeklyWorkHours;
        if (t.WeeklyWorkHours.HasValue && t.DistributeEvenly)
        {
            var daily = Math.Round(t.WeeklyWorkHours.Value / 5.0, 2);
            (s.TargetMon, s.TargetTue, s.TargetWed, s.TargetThu, s.TargetFri, s.TargetSat, s.TargetSun) = (daily, daily, daily, daily, daily, 0, 0);
        }
        else
            (s.TargetMon, s.TargetTue, s.TargetWed, s.TargetThu, s.TargetFri, s.TargetSat, s.TargetSun) =
                (t.TargetMon ?? 0, t.TargetTue ?? 0, t.TargetWed ?? 0, t.TargetThu ?? 0, t.TargetFri ?? 0, t.TargetSat ?? 0, t.TargetSun ?? 0);
    }

    /// <summary>Why a write cannot be made: the HTTP kind, the English message and its stable code.</summary>
    public sealed record Refusal(ServiceErrorType Type, string Message, string Code);

    public const string StartsTooEarly = "A change must start after the current schedule's first day. Correct the current schedule instead.";
    public const string FutureExists = "There is already a future schedule. Replace it or cancel it first.";
    public const string CorrectionClosed = "The current schedule reaches into a closed month. Change it from a later day instead.";

    /// <summary>
    /// Stages "new schedule from <paramref name="validFrom"/>" (no save): the period before it ends the day before, an existing
    /// future one is replaced (only with <paramref name="replaceFuture"/>, else 409 <c>schedule.futureExists</c>). The start must
    /// be after every kept period's first day — rewriting the current one is a correction. Callers check who may start when
    /// (members: today or later; admins: any open day; requests: when approved).
    /// </summary>
    internal static async Task<Refusal?> StageChangeAsync(TimeTrackingDbContext db, int userId, int orgId, DateOnly validFrom,
        ScheduleTargets targets, bool replaceFuture, DateOnly today, bool admin = false)
    {
        if (ValidateTargets(targets) is { } invalid) return new Refusal(ServiceErrorType.BadRequest, invalid.Message, invalid.Code);
        var periods = await LoadAsync(db, userId, orgId);
        var future = Split(periods, today).Future;
        var kept = periods.Where(p => p != future).ToList();
        if (kept.Any(p => p.ValidFrom >= validFrom))
            return new Refusal(ServiceErrorType.BadRequest, StartsTooEarly, "schedule.beforeCurrent");
        if (future != null)
        {
            if (!replaceFuture)
                return new Refusal(ServiceErrorType.Conflict, FutureExists, "schedule.futureExists");
            db.WorkSchedules.Remove(future);
        }
        var previous = kept.MaxBy(p => p.ValidFrom);
        if (previous != null) previous.ValidTo = validFrom.AddDays(-1);
        var schedule = new WorkSchedule { UserId = userId, OrganizationId = orgId, ValidFrom = validFrom };
        ApplyTargets(schedule, targets);
        ApplyContract(schedule, previous, targets, admin);
        db.WorkSchedules.Add(schedule);
        return null;
    }

    /// <summary>
    /// Stages an admin's correction of the current period (no save): new targets and optionally a new first day (after the
    /// previous period's first day, not after today; the previous one then ends the day before). Never into a closed month:
    /// a current period reaching into one is refused (409 <c>schedule.correctionClosed</c>) — change it from a later day instead.
    /// </summary>
    internal static async Task<Refusal?> StageCorrectionAsync(TimeTrackingDbContext db, int userId, int orgId, DateOnly? newFrom,
        ScheduleTargets targets, DateOnly today, IReadOnlyCollection<DateOnly> closedMonths)
    {
        if (ValidateTargets(targets) is { } invalid) return new Refusal(ServiceErrorType.BadRequest, invalid.Message, invalid.Code);
        var periods = await LoadAsync(db, userId, orgId);
        if (Split(periods, today).Current is not { } current)
            return new Refusal(ServiceErrorType.NotFound, "There is no current work schedule.", "schedule.noCurrent");
        var from = newFrom ?? current.ValidFrom;
        if (from > today)
            return new Refusal(ServiceErrorType.BadRequest, "The current schedule cannot start after today.", "schedule.startAfterToday");
        var previous = periods.Where(p => p.ValidFrom < current.ValidFrom).MaxBy(p => p.ValidFrom);
        if (previous != null && from <= previous.ValidFrom)
            return new Refusal(ServiceErrorType.BadRequest, "The current schedule must start after the previous one's first day.", "schedule.beforePrevious");
        if (ClosedPeriods.FirstClosed(closedMonths, from < current.ValidFrom ? from : current.ValidFrom, current.ValidTo) != null)
            return new Refusal(ServiceErrorType.Conflict, CorrectionClosed, "schedule.correctionClosed");
        if (previous != null) previous.ValidTo = from.AddDays(-1);
        current.ValidFrom = from;
        ApplyTargets(current, targets);
        ApplyContract(current, null, targets, admin: true);
        return null;
    }

    /// <summary>Stages cancelling the future period (no save): the current one runs on open-ended.</summary>
    internal static async Task<Refusal?> StageCancelFutureAsync(TimeTrackingDbContext db, int userId, int orgId, DateOnly today)
    {
        var periods = await LoadAsync(db, userId, orgId);
        if (Split(periods, today).Future is not { } future)
            return new Refusal(ServiceErrorType.NotFound, "There is no future work schedule.", "schedule.noFuture");
        db.WorkSchedules.Remove(future);
        if (periods.Where(p => p.ValidFrom < future.ValidFrom).MaxBy(p => p.ValidFrom) is { } previous) previous.ValidTo = null;
        return null;
    }

    // ── Normalising old data ──

    /// <summary>What <see cref="NormalizeAllAsync"/> changed.</summary>
    public sealed record Report(int Members, int Overlaps, int Duplicates, int Gaps, int ExtraFutures)
    {
        public bool Changed => Members > 0;
        public static readonly Report None = new(0, 0, 0, 0, 0);
        public Report Plus(Report b) =>
            new(Members + b.Members, Overlaps + b.Overlaps, Duplicates + b.Duplicates, Gaps + b.Gaps, ExtraFutures + b.ExtraFutures);
    }

    /// <summary>
    /// Brings every member's periods into the model — run at startup after closed months were frozen (issue #81 keeps them as
    /// they were); idempotent. The rule, per member, in this order:
    /// <list type="number">
    /// <item>Same first day: the period with the lowest id stays (the one the day rule picked), the others go (<c>Duplicates</c>).</item>
    /// <item>Overlaps: every day keeps the target it had — the latest-starting period valid that day wins; an earlier period ends
    /// the day before a later one starts, and its rest after the later one ended continues as a copy of it (<c>Overlaps</c>:
    /// periods shortened, split or fully covered and removed).</item>
    /// <item>Gaps: a gap between two periods is closed by running the earlier one on, and the last period never ends
    /// (open-ended) (<c>Gaps</c>). Only these days change their target: they had none (0) before.</item>
    /// <item>Future: of several periods starting after today (org-local) the earliest stays and runs open-ended, the later ones
    /// are removed (<c>ExtraFutures</c>).</item>
    /// </list>
    /// </summary>
    public static async Task<Report> NormalizeAllAsync(TimeTrackingDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        var total = Report.None;
        var orgs = await db.Organizations.IgnoreQueryFilters().AsNoTracking().Select(o => new { o.Id, o.TimeZone }).ToListAsync(ct);
        foreach (var org in orgs)
        {
            var today = OrgClock.For(org.TimeZone).Today();
            var byUser = (await db.WorkSchedules.IgnoreQueryFilters().Where(s => s.OrganizationId == org.Id).ToListAsync(ct))
                .GroupBy(s => s.UserId);
            var report = Report.None;
            foreach (var periods in byUser)
                report = report.Plus(Normalize(db, periods.ToList(), today));
            if (report.Changed) await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            total = total.Plus(report);
        }
        logger?.LogInformation("Work schedules normalised: {Members} member(s) changed — {Overlaps} overlapping period(s) resolved, " +
            "{Duplicates} duplicate(s) removed, {Gaps} gap(s) closed, {ExtraFutures} extra future period(s) removed.",
            total.Members, total.Overlaps, total.Duplicates, total.Gaps, total.ExtraFutures);
        return total;
    }

    /// <summary>One member's periods (tracked): stages the changes and reports them (Members = 1 when anything changed).</summary>
    internal static Report Normalize(TimeTrackingDbContext db, List<WorkSchedule> periods, DateOnly today)
    {
        if (periods.Count == 0) return Report.None;
        var duplicates = periods.GroupBy(p => p.ValidFrom).SelectMany(g => g.OrderBy(p => p.Id).Skip(1)).ToList();
        foreach (var d in duplicates) db.WorkSchedules.Remove(d);
        var live = periods.Except(duplicates).OrderBy(p => p.ValidFrom).ToList();

        // The effective period on each stretch between boundaries (the day rule: the latest start valid that day)
        var points = live.Select(p => p.ValidFrom)
            .Concat(live.Where(p => p.ValidTo != null).Select(p => p.ValidTo!.Value.AddDays(1)))
            .Distinct().Order().ToList();
        var segments = new List<(WorkSchedule? Source, DateOnly From, DateOnly? To)>();
        for (var i = 0; i < points.Count; i++)
        {
            var start = points[i];
            DateOnly? end = i + 1 < points.Count ? points[i + 1].AddDays(-1) : null;
            var source = live.Where(p => p.ValidFrom <= start && (p.ValidTo == null || p.ValidTo >= start)).MaxBy(p => p.ValidFrom);
            segments.Add((source, start, end));
        }

        // Gaps: the period before runs on (after the last one: open-ended)
        var result = new List<(WorkSchedule Source, DateOnly From, DateOnly? To)>();
        var gaps = 0;
        foreach (var (source, from, to) in segments)
        {
            if (source == null)
            {
                if (result.Count == 0) continue;
                gaps++;
                result[^1] = (result[^1].Source, result[^1].From, to);
            }
            else if (result.Count > 0 && result[^1].Source == source) result[^1] = (source, result[^1].From, to);
            else result.Add((source, from, to));
        }

        // One future period at most: the earliest, open-ended
        var futures = result.Count(r => r.From > today);
        if (futures > 1)
        {
            result = result.Take(result.FindIndex(r => r.From > today) + 1).ToList();
            result[^1] = (result[^1].Source, result[^1].From, null);
        }

        // Write: a period's first stretch is the period itself, further ones are copies, covered ones go
        var overlaps = 0;
        foreach (var p in live)
        {
            var mine = result.Where(r => r.Source == p).ToList();
            if (mine.Count == 0)
            {
                db.WorkSchedules.Remove(p);
                if (p.ValidFrom <= today) overlaps++;
                continue;
            }
            // Shortened, split or moved (a gap fill only lengthens the end)
            var onlyLonger = mine.Count == 1 && mine[0].From == p.ValidFrom && (mine[0].To == null || (p.ValidTo != null && mine[0].To >= p.ValidTo));
            if (!onlyLonger) overlaps++;
            (p.ValidFrom, p.ValidTo) = (mine[0].From, mine[0].To);
            foreach (var extra in mine.Skip(1))
                db.WorkSchedules.Add(new WorkSchedule
                {
                    UserId = p.UserId, OrganizationId = p.OrganizationId, ValidFrom = extra.From, ValidTo = extra.To,
                    WeeklyWorkHours = p.WeeklyWorkHours, TargetMon = p.TargetMon, TargetTue = p.TargetTue, TargetWed = p.TargetWed,
                    TargetThu = p.TargetThu, TargetFri = p.TargetFri, TargetSat = p.TargetSat, TargetSun = p.TargetSun
                });
        }
        var extraFutures = Math.Max(0, futures - 1);
        var changed = duplicates.Count + overlaps + gaps + extraFutures > 0;
        return new Report(changed ? 1 : 0, overlaps, duplicates.Count, gaps, extraFutures);
    }
}
