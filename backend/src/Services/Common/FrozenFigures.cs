using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Frozen figures of closed months (issue #81): closing a month stores every member's day rows and month totals
/// (<see cref="FrozenDay"/>, <see cref="FrozenMonth"/>) as the reports showed them at that moment; from then on they are the
/// truth for that month. <b>The one reader</b> is <c>DailyReportAggregator.BuildRows</c>: every loader of it (BuildAsync,
/// BuildWithRunningTotalAsync, TotalsAsync) loads the member's frozen days of the range (<see cref="DaysAsync"/>) and the loop
/// takes a frozen day as stored instead of computing it, the running total continuing from its stored sum; balance starts come
/// from the latest frozen month (<c>BalanceCheckpoints.LoadAsync</c>). What the loop does not cover reads the frozen days here:
/// absence and failure counts (<see cref="AbsencesAsync"/>, payroll, vacation balances, accounting overview) and the compliance
/// findings (<see cref="ComplianceAsync"/>). A day is frozen iff it has a row: no row (a member who joined later) = live.
/// <para>
/// Writes: <see cref="FreezeAsync"/> on closing (<c>ClosedPeriodService</c>) and for an admin's change inside a closed month
/// (<see cref="RefreezeAsync"/>, run by <c>TimeTrackingDbContext.SaveChangesAsync</c> in the same transaction for exactly the
/// member-months the closed-period guard let through); <see cref="UnfreezeAsync"/> on reopening; <see cref="FreezeUnfrozenAsync"/>
/// at startup for months closed before this existed. Nothing else changes the rows.
/// </para>
/// </summary>
public static class FrozenFigures
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Rows per INSERT statement (arrays, so no parameter limit; bounds the statement size).</summary>
    private const int InsertChunk = 5000;

    internal static DailyReportRow ToRow(FrozenDay f) => new(
        f.Date, f.Date.DayOfWeek, f.TargetHours, f.WorkedHours, f.PauseMinutes, f.NetWorkedHours, f.Overtime, f.CumulativeOvertime,
        f.Status, f.HolidayName, f.AbsenceType, f.AbsenceNote, f.EntrySummary, f.CountsAsVacation, f.CoreHoursMissed,
        f.DayTargetHours, f.CreditHours, f.OvertimeDelta, f.FailureCount, f.AbsenceHalfDay, Frozen: true);

    // ── Reading ──

    /// <summary>
    /// The members' (null: everybody's) frozen days in [<paramref name="from"/>, <paramref name="to"/>] by member and day. One
    /// query; none when the range starts in the current month or later (a month is closed only after it ended).
    /// </summary>
    internal static async Task<Dictionary<int, Dictionary<DateOnly, FrozenDay>>> DaysAsync(TimeTrackingDbContext db, OrgClock clock,
        int orgId, IReadOnlyCollection<int>? userIds, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (from > to || userIds?.Count == 0 || from >= ClosedPeriods.MonthOf(clock.Today())) return [];
        var query = db.FrozenDays.AsNoTracking().Where(d => d.OrganizationId == orgId && d.Date >= from && d.Date <= to);
        if (userIds != null)
        {
            var ids = userIds.Distinct().ToList();
            query = query.Where(d => ids.Contains(d.UserId));
        }
        return (await query.ToListAsync(ct))
            .GroupBy(d => d.UserId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(d => d.Date));
    }

    /// <summary>Every day of [from, to] is frozen (nothing of the range needs computing).</summary>
    internal static bool Covers(IReadOnlyDictionary<DateOnly, FrozenDay>? frozen, DateOnly from, DateOnly to)
    {
        if (frozen == null || from > to || frozen.Count < to.DayNumber - from.DayNumber + 1) return false;
        for (var day = from; day <= to; day = day.AddDays(1))
            if (!frozen.ContainsKey(day)) return false;
        return true;
    }

    /// <summary>An absence as the counts see it: <paramref name="Days"/> 1 or 0.5.</summary>
    public readonly record struct Absence(int UserId, DateOnly Date, AbsenceType Type, bool CountsAsVacation, double Days, double VacationUsed);

    /// <summary>
    /// The frozen side of absence counts in [from, to]: the frozen member-months (a live absence in one of them does not count)
    /// and the frozen days with an absence. One query (absence days plus each month's first day as its marker).
    /// </summary>
    internal static async Task<(HashSet<(int UserId, DateOnly Month)> Months, List<Absence> Absences)> AbsencesAsync(
        TimeTrackingDbContext db, int orgId, IReadOnlyCollection<int> userIds, DateOnly from, DateOnly to)
    {
        if (userIds.Count == 0 || from > to) return ([], []);
        var ids = userIds.Distinct().ToList();
        var rows = await db.FrozenDays.AsNoTracking()
            .Where(d => d.OrganizationId == orgId && ids.Contains(d.UserId) && d.Date >= from && d.Date <= to
                     && (d.AbsenceType != null || d.Date.Day == 1))
            .Select(d => new { d.UserId, d.Date, d.AbsenceType, d.CountsAsVacation, d.AbsenceHalfDay, d.VacationUsed })
            .ToListAsync();
        var months = rows.Select(r => (r.UserId, ClosedPeriods.MonthOf(r.Date))).ToHashSet();
        var absences = rows.Where(r => r.AbsenceType != null)
            .Select(r => new Absence(r.UserId, r.Date, r.AbsenceType!.Value, r.CountsAsVacation, r.AbsenceHalfDay ? 0.5 : 1.0, r.VacationUsed))
            .ToList();
        return (months, absences);
    }

    /// <summary>
    /// Live absences outside frozen months plus the frozen ones — the absences every count (payroll, vacation overview) reads.
    /// </summary>
    internal static List<Absence> Overlay(IEnumerable<Absence> live, (HashSet<(int UserId, DateOnly Month)> Months, List<Absence> Absences) frozen) =>
        live.Where(a => !frozen.Months.Contains((a.UserId, ClosedPeriods.MonthOf(a.Date)))).Concat(frozen.Absences).ToList();

    /// <summary>The compliance findings of [from, to]: <paramref name="live"/>'s for open days (not computed when every day is
    /// closed), the frozen ones for closed days.</summary>
    public static async Task<List<ComplianceDayResponse>> ComplianceAsync(TimeTrackingDbContext db, Organization org, int userId,
        DateOnly from, DateOnly to, Func<Task<List<ComplianceDayResponse>>> live)
    {
        var frozen = (await DaysAsync(db, OrgClock.For(org), org.Id, [userId], from, to)).GetValueOrDefault(userId);
        if (frozen == null) return await live();
        var open = Covers(frozen, from, to) ? [] : (await live()).Where(d => !frozen.ContainsKey(d.Date));
        return open
            .Concat(frozen.Values.Where(f => f.Compliance != null)
                .Select(f => JsonSerializer.Deserialize<ComplianceDayResponse>(f.Compliance!, Json)!))
            .OrderBy(d => d.Date)
            .ToList();
    }

    // ── Freezing ──

    /// <summary>
    /// Stores the figures of <paramref name="months"/> (ended months of <paramref name="org"/>) for <paramref name="userIds"/>, or
    /// for every member when null — active members plus former ones with time or absences in those months — replacing what
    /// those member-months held. Computed exactly like the reports (the running balance continues from the latest frozen month or
    /// checkpoint before them; other frozen months in between are taken as stored). Postgres: in the caller's transaction if
    /// there is one; InMemory: its own save.
    /// </summary>
    internal static async Task FreezeAsync(TimeTrackingDbContext db, Organization org, IReadOnlyCollection<DateOnly> months,
        IReadOnlyCollection<int>? userIds, CancellationToken ct = default)
    {
        if (months.Count == 0) return;
        var (frozenMonths, frozenDays) = await ComputeAsync(db, org, months.Distinct().Order().ToList(), userIds, ct);
        await WriteAsync(db, org.Id, months, userIds, frozenMonths, frozenDays, ct);
    }

    /// <summary>Re-freezes the member-months an admin changed inside closed months (with a reason) in this save.</summary>
    internal static async Task RefreezeAsync(TimeTrackingDbContext db, IReadOnlyCollection<(int OrgId, int UserId, DateOnly Month)> touched,
        CancellationToken ct)
    {
        foreach (var org in touched.GroupBy(t => t.OrgId))
        {
            var organization = await db.Organizations.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(o => o.Id == org.Key, ct);
            if (organization == null) continue;
            // One computation per set of months (usually one month of one member)
            foreach (var byMonths in org.GroupBy(t => t.UserId)
                         .GroupBy(u => string.Join(",", u.Select(t => t.Month).Distinct().Order())))
            {
                var months = byMonths.First().Select(t => t.Month).Distinct().ToList();
                await FreezeAsync(db, organization, months, byMonths.Select(u => u.Key).ToList(), ct);
            }
        }
    }

    /// <summary>Drops the frozen figures of a reopened month (its days are live again). Postgres: in the caller's transaction.</summary>
    internal static async Task UnfreezeAsync(TimeTrackingDbContext db, int orgId, DateOnly month, CancellationToken ct = default) =>
        await WriteAsync(db, orgId, [month], null, [], [], ct);

    /// <summary>
    /// Freezes the closed months that have no frozen figures yet (<see cref="ClosedPeriod.FrozenAt"/> null: closed before issue #81)
    /// from the current data, once, at startup. Returns the number of months frozen.
    /// </summary>
    public static async Task<int> FreezeUnfrozenAsync(TimeTrackingDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        var pending = await db.ClosedPeriods.IgnoreQueryFilters().Where(p => p.FrozenAt == null).ToListAsync(ct);
        foreach (var org in pending.GroupBy(p => p.OrganizationId))
        {
            var organization = await db.Organizations.IgnoreQueryFilters().AsNoTracking().FirstAsync(o => o.Id == org.Key, ct);
            await FreezeAsync(db, organization, org.Select(p => p.Month).ToList(), null, ct);
            foreach (var p in org) p.FrozenAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            logger?.LogInformation("Froze {Count} closed month(s) of organization {OrgId} from the current data.", org.Count(), org.Key);
        }
        return pending.Count;
    }

    private static async Task<(List<FrozenMonth> Months, List<FrozenDay> Days)> ComputeAsync(TimeTrackingDbContext db, Organization org,
        List<DateOnly> months, IReadOnlyCollection<int>? userIds, CancellationToken ct)
    {
        var clock = OrgClock.For(org);
        var first = months[0];
        var lastDay = months[^1].AddMonths(1).AddDays(-1);
        var (firstUtc, endUtc) = (clock.StartOfDayUtc(first), clock.StartOfDayUtc(lastDay.AddDays(1)));

        var ids = userIds?.ToList();
        var query = db.UserOrganizations.IgnoreQueryFilters().AsNoTracking().Where(uo => uo.OrganizationId == org.Id);
        query = ids != null
            ? query.Where(uo => ids.Contains(uo.UserId))
            : query.Where(uo => (uo.IsActive && uo.User.IsActive)
                || db.TimeEntries.Any(e => e.OrganizationId == org.Id && e.UserId == uo.UserId && !e.IsRunning
                                           && e.StartTime >= firstUtc && e.StartTime < endUtc)
                || db.AbsenceDays.Any(a => a.OrganizationId == org.Id && a.UserId == uo.UserId && a.Date >= first && a.Date <= lastDay));
        var memberships = await query.Select(uo => new { uo.UserId, uo.InitialOvertimeHours }).ToListAsync(ct);
        if (memberships.Count == 0) return ([], []);
        var memberIds = memberships.Select(m => m.UserId).ToList();

        var (members, _) = await BalanceCheckpoints.LoadAsync(db, org.Id, clock, memberIds, first,
            DateTime.SpecifyKind(new DateTime(1900, 1, 1), DateTimeKind.Utc), ct);
        // Entries from the earliest balance start (or the day before the first month, for the rest-period check)
        var from = members.Values.Select(m => m.Start?.From).OfType<DateOnly>().Append(first.AddDays(-1)).Min();
        var fromUtc = clock.StartOfDayUtc(from);
        var entries = (await db.TimeEntries.AsNoTracking()
                .Where(e => e.OrganizationId == org.Id && memberIds.Contains(e.UserId) && !e.IsRunning
                         && e.StartTime >= fromUtc && e.StartTime < endUtc)
                .OrderBy(e => e.StartTime)
                .ToListAsync(ct))
            .ToLookup(e => e.UserId);
        var holidays = await db.Holidays.AsNoTracking()
            .Where(h => h.OrganizationId == org.Id && ((h.Date >= from && h.Date <= lastDay) || h.IsRecurring))
            .OrderBy(h => h.IsRecurring).ThenBy(h => h.Id)
            .ToListAsync(ct);
        var absences = (await db.AbsenceDays.AsNoTracking()
                .Include(a => a.CustomType)
                .Where(a => a.OrganizationId == org.Id && memberIds.Contains(a.UserId) && a.Date >= from && a.Date <= lastDay)
                .ToListAsync(ct))
            .ToLookup(a => a.UserId);
        var violations = await CoreHours.OpenViolationsAsync(db, memberIds, org.Id, first, lastDay);
        // Other closed months between the ones frozen now are taken as stored
        var monthSet = months.ToHashSet();
        var otherFrozen = months.Count > 1 && months[^1] > months[0].AddMonths(months.Count - 1)
            ? await DaysAsync(db, clock, org.Id, memberIds, first, lastDay, ct)
            : [];

        var now = DateTime.UtcNow;
        var resultMonths = new List<FrozenMonth>();
        var resultDays = new List<FrozenDay>();
        var noViolations = new Dictionary<DateOnly, CoreHours.Result>();
        foreach (var m in memberships)
        {
            var member = members.GetValueOrDefault(m.UserId);
            var start = member?.Start;
            var schedules = member?.Schedules ?? [];
            var memberViolations = violations.GetValueOrDefault(m.UserId) ?? noViolations;
            var memberEntries = entries[m.UserId].ToList();
            var others = otherFrozen.GetValueOrDefault(m.UserId)?.Where(d => !monthSet.Contains(ClosedPeriods.MonthOf(d.Key)))
                .ToDictionary(d => d.Key, d => d.Value);
            var rowsFrom = start is { } s && s.From < first ? s.From : first;
            var rows = DailyReportAggregator.BuildRows(clock, rowsFrom, lastDay, m.InitialOvertimeHours, memberEntries, schedules,
                holidays, absences[m.UserId], memberViolations, first, start?.Delta ?? 0, frozen: others,
                balanceFrom: start?.From ?? DateOnly.MaxValue);
            var compliance = WorkingTimeAct.Findings(WorkingTimeAct.Days(clock, memberEntries, first, lastDay), memberViolations)
                .ToDictionary(d => d.Date);

            foreach (var month in months)
            {
                var monthRows = rows.Where(r => ClosedPeriods.MonthOf(r.Date) == month).ToList();
                if (monthRows.Count == 0) continue;
                foreach (var r in monthRows)
                    resultDays.Add(new FrozenDay
                    {
                        OrganizationId = org.Id, UserId = m.UserId, Date = r.Date,
                        DayTargetHours = r.DayTargetHours, CreditHours = r.CreditHours, TargetHours = r.TargetHours,
                        WorkedHours = r.WorkedHours, PauseMinutes = r.PauseMinutes, NetWorkedHours = r.NetWorkedHours,
                        Overtime = r.Overtime, OvertimeDelta = r.OvertimeDelta, CumulativeOvertime = r.CumulativeOvertime,
                        Status = r.Status, HolidayName = r.HolidayName, AbsenceType = r.AbsenceType, AbsenceHalfDay = r.AbsenceHalfDay,
                        AbsenceNote = r.AbsenceNote, CountsAsVacation = r.CountsAsVacation,
                        // The vacation balance's rule (VacationBalances): working days that are no holiday
                        VacationUsed = r.CountsAsVacation && r.HolidayName == null && WorkingDays.IsWorkingDay(schedules, r.Date)
                            ? (r.AbsenceHalfDay ? 0.5 : 1.0) : 0,
                        CoreHoursMissed = r.CoreHoursMissed, FailureCount = r.FailureCount, EntrySummary = r.EntrySummary,
                        Compliance = compliance.TryGetValue(r.Date, out var c) ? JsonSerializer.Serialize(c, Json) : null
                    });
                var counted = monthRows.Where(r => r.Counted).ToList();
                double Days(Func<DailyReportRow, bool> kind) =>
                    monthRows.Where(r => r.AbsenceType != null && kind(r)).Sum(r => r.AbsenceHalfDay ? 0.5 : 1.0);
                var vacation = Days(r => r.CountsAsVacation);
                var sick = Days(r => r.AbsenceType == AbsenceType.SickDay);
                resultMonths.Add(new FrozenMonth
                {
                    OrganizationId = org.Id, UserId = m.UserId, Month = month,
                    FirstDay = start != null && start.From <= monthRows[^1].Date ? start.FirstDay : null,
                    OvertimeDelta = monthRows[^1].OvertimeDelta ?? 0,
                    InitialOvertimeHours = m.InitialOvertimeHours,
                    TargetHours = counted.Sum(r => r.TargetHours), WorkedHours = counted.Sum(r => r.NetWorkedHours),
                    OvertimeHours = counted.Sum(r => r.Overtime), Balance = monthRows[^1].CumulativeOvertime,
                    VacationDays = vacation, SickDays = sick, OtherAbsenceDays = Days(_ => true) - vacation - sick,
                    FailureEntryCount = monthRows.Sum(r => r.FailureCount),
                    TimeZone = org.TimeZone, FrozenAt = now
                });
            }
        }
        return (resultMonths, resultDays);
    }

    /// <summary>Replaces the rows of <paramref name="months"/> (of <paramref name="userIds"/>, or of everybody) with the given ones.</summary>
    private static async Task WriteAsync(TimeTrackingDbContext db, int orgId, IReadOnlyCollection<DateOnly> months,
        IReadOnlyCollection<int>? userIds, List<FrozenMonth> newMonths, List<FrozenDay> newDays, CancellationToken ct)
    {
        var monthList = months.Distinct().ToArray();
        var users = userIds?.Distinct().ToArray();
        if (!db.Database.IsNpgsql())
        {
            var (first, last) = (monthList.Min(), monthList.Max().AddMonths(1));
            db.FrozenMonths.RemoveRange(await db.FrozenMonths
                .Where(f => f.OrganizationId == orgId && monthList.Contains(f.Month) && (users == null || users.Contains(f.UserId)))
                .ToListAsync(ct));
            db.FrozenDays.RemoveRange((await db.FrozenDays
                    .Where(f => f.OrganizationId == orgId && f.Date >= first && f.Date < last && (users == null || users.Contains(f.UserId)))
                    .ToListAsync(ct))
                .Where(f => monthList.Contains(ClosedPeriods.MonthOf(f.Date))));
            db.FrozenMonths.AddRange(newMonths);
            db.FrozenDays.AddRange(newDays);
            try
            {
                await db.SaveWithoutHooksAsync(ct);
            }
            finally
            {
                foreach (var e in db.ChangeTracker.Entries().Where(e => e.Entity is FrozenMonth or FrozenDay).ToList())
                    e.State = EntityState.Detached;
            }
            return;
        }

        var userFilter = users == null ? "" : """ AND "UserId" = ANY(@users)""";
        var parameters = new List<NpgsqlParameter>
        {
            new("org", orgId), new("months", monthList) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Date }
        };
        if (users != null) parameters.Add(new NpgsqlParameter("users", users));
        await db.Database.ExecuteSqlRawAsync(
            $"""DELETE FROM "FrozenDays" WHERE "OrganizationId" = @org AND date_trunc('month', "Date")::date = ANY(@months){userFilter}""",
            parameters.Select(p => p.Clone()), ct);
        await db.Database.ExecuteSqlRawAsync(
            $"""DELETE FROM "FrozenMonths" WHERE "OrganizationId" = @org AND "Month" = ANY(@months){userFilter}""",
            parameters.Select(p => p.Clone()), ct);

        if (newMonths.Count > 0)
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "FrozenMonths" ("OrganizationId", "UserId", "Month", "FirstDay", "OvertimeDelta", "InitialOvertimeHours",
                    "TargetHours", "WorkedHours", "OvertimeHours", "Balance", "VacationDays", "SickDays", "OtherAbsenceDays",
                    "FailureEntryCount", "TimeZone", "FrozenAt")
                SELECT * FROM unnest(@a::integer[], @b::integer[], @c::date[], @d::date[], @e::double precision[], @f::double precision[],
                    @g::double precision[], @h::double precision[], @i::double precision[], @j::double precision[], @k::double precision[],
                    @l::double precision[], @m::double precision[], @n::integer[], @o::text[], @p::timestamp with time zone[])
                """,
                [
                    Column("a", newMonths.Select(r => r.OrganizationId)), Column("b", newMonths.Select(r => r.UserId)),
                    Column("c", newMonths.Select(r => r.Month)), Column("d", newMonths.Select(r => r.FirstDay)),
                    Column("e", newMonths.Select(r => r.OvertimeDelta)), Column("f", newMonths.Select(r => r.InitialOvertimeHours)),
                    Column("g", newMonths.Select(r => r.TargetHours)), Column("h", newMonths.Select(r => r.WorkedHours)),
                    Column("i", newMonths.Select(r => r.OvertimeHours)), Column("j", newMonths.Select(r => r.Balance)),
                    Column("k", newMonths.Select(r => r.VacationDays)), Column("l", newMonths.Select(r => r.SickDays)),
                    Column("m", newMonths.Select(r => r.OtherAbsenceDays)), Column("n", newMonths.Select(r => r.FailureEntryCount)),
                    Column("o", newMonths.Select(r => r.TimeZone)), Column("p", newMonths.Select(r => r.FrozenAt))
                ], ct);

        foreach (var chunk in newDays.Chunk(InsertChunk))
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "FrozenDays" ("OrganizationId", "UserId", "Date", "DayTargetHours", "CreditHours", "TargetHours",
                    "WorkedHours", "PauseMinutes", "NetWorkedHours", "Overtime", "OvertimeDelta", "CumulativeOvertime", "Status",
                    "HolidayName", "AbsenceType", "AbsenceHalfDay", "AbsenceNote", "CountsAsVacation", "VacationUsed",
                    "CoreHoursMissed", "FailureCount", "EntrySummary", "Compliance")
                SELECT * FROM unnest(@a::integer[], @b::integer[], @c::date[], @d::double precision[], @e::double precision[],
                    @f::double precision[], @g::double precision[], @h::integer[], @i::double precision[], @j::double precision[],
                    @k::double precision[], @l::double precision[], @m::text[], @n::text[], @o::integer[], @p::boolean[], @q::text[],
                    @r::boolean[], @s::double precision[], @t::boolean[], @u::integer[], @v::text[], @w::text[])
                """,
                [
                    Column("a", chunk.Select(r => r.OrganizationId)), Column("b", chunk.Select(r => r.UserId)),
                    Column("c", chunk.Select(r => r.Date)), Column("d", chunk.Select(r => r.DayTargetHours)),
                    Column("e", chunk.Select(r => r.CreditHours)), Column("f", chunk.Select(r => r.TargetHours)),
                    Column("g", chunk.Select(r => r.WorkedHours)), Column("h", chunk.Select(r => r.PauseMinutes)),
                    Column("i", chunk.Select(r => r.NetWorkedHours)), Column("j", chunk.Select(r => r.Overtime)),
                    Column("k", chunk.Select(r => r.OvertimeDelta)), Column("l", chunk.Select(r => r.CumulativeOvertime)),
                    Column("m", chunk.Select(r => r.Status)), Column("n", chunk.Select(r => r.HolidayName)),
                    Column("o", chunk.Select(r => (int?)r.AbsenceType)), Column("p", chunk.Select(r => r.AbsenceHalfDay)),
                    Column("q", chunk.Select(r => r.AbsenceNote)), Column("r", chunk.Select(r => r.CountsAsVacation)),
                    Column("s", chunk.Select(r => r.VacationUsed)), Column("t", chunk.Select(r => r.CoreHoursMissed)),
                    Column("u", chunk.Select(r => r.FailureCount)), Column("v", chunk.Select(r => r.EntrySummary)),
                    Column("w", chunk.Select(r => r.Compliance))
                ], ct);
    }

    private static NpgsqlParameter Column<T>(string name, IEnumerable<T> values) => new(name, values.ToArray());
}
