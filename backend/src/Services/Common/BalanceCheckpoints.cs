using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>
/// Overtime balance checkpoints (Phase 2 of docs/PERFORMANCE_PLAN.md): a member's overtime sum at the end of every ended
/// org-local month, so a balance only computes the days since the latest valid checkpoint instead of the whole history.
/// <para>
/// Invariants: a <see cref="BalanceCheckpoint"/> is valid iff its <c>Version</c>, <c>OrgVersion</c> and <c>Algorithm</c>
/// equal <see cref="UserOrganization.BalanceVersion"/>, <see cref="Organization.BalanceVersion"/> and
/// <see cref="AlgorithmVersion"/>. The versions are read before the source data and written with the rows, so a computation
/// racing an edit only ever writes rows that are already invalid. <see cref="BalanceInvalidation"/> bumps the versions in the
/// same save as every change of a balance input in an ended month — so every write to TimeEntries, AbsenceDays,
/// WorkSchedules, Holidays, AbsenceTypeDefinitions and Organizations.TimeZone must go through
/// <c>TimeTrackingDbContext.SaveChangesAsync</c> (never ExecuteUpdate/raw SQL).
/// </para>
/// </summary>
public static class BalanceCheckpoints
{
    /// <summary>
    /// Bump whenever the day rules of <c>DailyReportAggregator.BuildRows</c> change (targets, credits, what counts — e.g. #76):
    /// every stored checkpoint then becomes invalid and is recomputed on the next read.
    /// </summary>
    public const int AlgorithmVersion = 1;

    /// <summary>Rows per upsert statement (9 parameters each, far below Postgres' 65535).</summary>
    private const int UpsertChunk = 1000;

    /// <summary>Where a member's rows start: <see cref="From"/> with the overtime sum <see cref="Delta"/> before it.</summary>
    internal sealed record Start(DateOnly From, double Delta, DateOnly FirstDay);

    /// <summary>
    /// The versions read before the source data; the checkpoints written from that data carry them. <see cref="TimeZone"/> is
    /// the organization's zone read in the same statement (same snapshot as <see cref="OrgVersion"/>): a caller whose clock
    /// used another zone (a concurrent time-zone change) must not store anything (<see cref="SaveAsync"/> checks it).
    /// </summary>
    internal sealed record Stamp(int OrgId, int OrgVersion, IReadOnlyDictionary<int, int> Members, DateOnly Today, string? TimeZone);

    /// <summary>
    /// The one-day margin between readers and writers (<see cref="BalanceInvalidation"/>). Invariant: a reader stores month M
    /// only when <c>LastDay(M) &lt; readerToday - 1</c>; a writer bumps for a change in month M when <c>LastDay(M) &lt; writerToday</c>.
    /// With the two org-local clocks at most one day apart (readerToday &lt;= writerToday + 1: same zone, and a reader that read
    /// the data before the write stamped its "today" no later than the writer's, plus clock skew), a stored month has
    /// LastDay(M) &lt;= readerToday - 2 &lt;= writerToday - 1, so every write that could make it stale bumps. A change on a
    /// month's last day touches no month a reader may store yet, so it rewrites nothing.
    /// </summary>
    internal static bool Storable(DateOnly monthLastDay, DateOnly readerToday) => monthLastDay < readerToday.AddDays(-1);

    /// <summary>What <see cref="LoadAsync"/> reads per member: where its rows start (null: no finished entry, no balance yet),
    /// its work schedules (ordered by <see cref="WorkSchedule.ValidFrom"/>), its failure entries before the given instant and its
    /// latest valid checkpoint of any month (<see cref="SaveAsync"/> does not write those again).</summary>
    internal sealed record Member(Start? Start, List<WorkSchedule> Schedules, int FailureCount, DateOnly? LatestCheckpoint = null);

    /// <summary>
    /// For each of <paramref name="userIds"/> (members of the organization, also former ones): where its rows for days from
    /// <paramref name="before"/> on start — the day after its latest valid checkpoint ending before <paramref name="before"/>,
    /// else its first tracked day with sum 0 — plus its schedules and number of failure entries started before
    /// <paramref name="failuresBeforeUtc"/>. One query (versions, checkpoint, first entry, failure count and schedules per
    /// membership), so the versions in the returned <see cref="Stamp"/> are read in the same snapshot as, and so not after,
    /// anything they cover.
    /// </summary>
    internal static async Task<(Dictionary<int, Member> Members, Stamp Stamp)> LoadAsync(TimeTrackingDbContext db, int orgId,
        OrgClock clock, IReadOnlyCollection<int> userIds, DateOnly before, DateTime failuresBeforeUtc, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        var bound = new DateOnly(before.Year, before.Month, 1);
        // The latest valid checkpoint as three scalar subqueries (each an index probe on (OrganizationId, UserId, Month)
        // backwards): EF turns a FirstOrDefault of several columns into a window over all of the organization's checkpoints
        var valid = db.BalanceCheckpoints.Where(c => c.OrganizationId == orgId && c.Algorithm == AlgorithmVersion);
        var checkpoints = valid.Where(c => c.Month < bound);
        var rows = await db.UserOrganizations.IgnoreQueryFilters().AsNoTracking()
            .Where(uo => uo.OrganizationId == orgId && ids.Contains(uo.UserId))
            .Select(uo => new
            {
                uo.UserId,
                uo.BalanceVersion,
                OrgVersion = uo.Organization.BalanceVersion,
                uo.Organization.TimeZone,
                CheckpointMonth = checkpoints
                    .Where(c => c.UserId == uo.UserId && c.Version == uo.BalanceVersion && c.OrgVersion == uo.Organization.BalanceVersion)
                    .OrderByDescending(c => c.Month).Select(c => (DateOnly?)c.Month).FirstOrDefault(),
                CheckpointDelta = checkpoints
                    .Where(c => c.UserId == uo.UserId && c.Version == uo.BalanceVersion && c.OrgVersion == uo.Organization.BalanceVersion)
                    .OrderByDescending(c => c.Month).Select(c => (double?)c.OvertimeDelta).FirstOrDefault(),
                CheckpointFirstDay = checkpoints
                    .Where(c => c.UserId == uo.UserId && c.Version == uo.BalanceVersion && c.OrgVersion == uo.Organization.BalanceVersion)
                    .OrderByDescending(c => c.Month).Select(c => (DateOnly?)c.FirstDay).FirstOrDefault(),
                LatestCheckpoint = valid
                    .Where(c => c.UserId == uo.UserId && c.Version == uo.BalanceVersion && c.OrgVersion == uo.Organization.BalanceVersion)
                    .Max(c => (DateOnly?)c.Month),
                FirstEntry = db.TimeEntries
                    .Where(e => e.OrganizationId == orgId && e.UserId == uo.UserId && !e.IsRunning)
                    .Min(e => (DateTime?)e.StartTime),
                Failures = db.TimeEntries
                    .Count(e => e.OrganizationId == orgId && e.UserId == uo.UserId && e.IsFailure && e.StartTime < failuresBeforeUtc),
                Schedules = db.WorkSchedules
                    .Where(ws => ws.OrganizationId == orgId && ws.UserId == uo.UserId)
                    .OrderBy(ws => ws.ValidFrom)
                    .ToList()
            })
            .ToListAsync(ct);
        var stamp = new Stamp(orgId, rows.Count > 0 ? rows[0].OrgVersion : 0,
            rows.ToDictionary(r => r.UserId, r => r.BalanceVersion), clock.Today(), rows.Count > 0 ? rows[0].TimeZone : null);

        var members = new Dictionary<int, Member>();
        foreach (var r in rows)
        {
            Start? start = null;
            if (r.CheckpointMonth is { } month)
                start = new Start(month.AddMonths(1), r.CheckpointDelta!.Value, r.CheckpointFirstDay!.Value);
            else if (r.FirstEntry is { } first)
            {
                var day = clock.LocalDate(first);
                start = new Start(day, 0, day);
            }
            members[r.UserId] = new Member(start, r.Schedules, r.Failures, r.LatestCheckpoint);
        }
        return (members, stamp);
    }

    /// <summary>
    /// Stores a checkpoint for every month that ended (last day before yesterday, <see cref="Storable"/>) in the computed rows (<paramref name="computed"/>:
    /// per member its start and the overtime sums at month ends from <c>BuildRows</c>), with the versions of
    /// <paramref name="stamp"/>. Postgres: one upsert per 1000 rows that replaces a row only with one of the same or newer
    /// versions, in its own statement — never inside a caller's transaction, a failure (e.g. a caller RLS does not let
    /// write) only costs the cache. InMemory: skipped while the context has unsaved changes (they must not be saved early).
    /// Not a source table: no closed-period guard, entry history or push.
    /// </summary>
    internal static async Task SaveAsync(TimeTrackingDbContext db, Stamp stamp, string? zoneUsed,
        IEnumerable<(int UserId, Member Member, List<(DateOnly Day, double Delta)> MonthEnds)> computed, CancellationToken ct = default)
    {
        // The rows were built with the caller's clock (zoneUsed); the versions belong to the zone read with them
        if (stamp.TimeZone != zoneUsed) return;
        var now = DateTime.UtcNow;
        var rows = new List<BalanceCheckpoint>();
        foreach (var (userId, member, monthEnds) in computed)
        {
            if (!stamp.Members.TryGetValue(userId, out var version) || member.Start is not { } start) continue;
            foreach (var (day, delta) in monthEnds)
                // Ended months that are not stored and valid yet (valid ones form a prefix from the first month on)
                if (Storable(day, stamp.Today) && !(member.LatestCheckpoint >= new DateOnly(day.Year, day.Month, 1)))
                    rows.Add(new BalanceCheckpoint
                    {
                        OrganizationId = stamp.OrgId, UserId = userId, Month = new DateOnly(day.Year, day.Month, 1),
                        FirstDay = start.FirstDay, OvertimeDelta = delta,
                        Version = version, OrgVersion = stamp.OrgVersion, Algorithm = AlgorithmVersion, ComputedAt = now
                    });
        }
        if (rows.Count == 0 || db.Database.CurrentTransaction != null) return;

        try
        {
            if (db.Database.IsNpgsql())
                foreach (var chunk in rows.Chunk(UpsertChunk))
                    await UpsertAsync(db, chunk, ct);
            else
                await SaveTrackedAsync(db, rows, ct);
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            db.GetService<ILoggerFactory>().CreateLogger(typeof(BalanceCheckpoints))
                .LogWarning(ex, "Could not store {Count} balance checkpoint(s) of organization {OrgId}.", rows.Count, stamp.OrgId);
        }
    }

    private static async Task UpsertAsync(TimeTrackingDbContext db, IReadOnlyList<BalanceCheckpoint> rows, CancellationToken ct)
    {
        var sql = new StringBuilder(
            """INSERT INTO "BalanceCheckpoints" ("OrganizationId", "UserId", "Month", "FirstDay", "OvertimeDelta", "Version", "OrgVersion", "Algorithm", "ComputedAt") VALUES """);
        var parameters = new List<object>(rows.Count * 9);
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var p = i * 9;
            if (i > 0) sql.Append(", ");
            sql.Append($"(@p{p}, @p{p + 1}, @p{p + 2}, @p{p + 3}, @p{p + 4}, @p{p + 5}, @p{p + 6}, @p{p + 7}, @p{p + 8})");
            parameters.Add(new NpgsqlParameter($"p{p}", r.OrganizationId));
            parameters.Add(new NpgsqlParameter($"p{p + 1}", r.UserId));
            parameters.Add(new NpgsqlParameter($"p{p + 2}", r.Month));
            parameters.Add(new NpgsqlParameter($"p{p + 3}", r.FirstDay));
            parameters.Add(new NpgsqlParameter($"p{p + 4}", r.OvertimeDelta));
            parameters.Add(new NpgsqlParameter($"p{p + 5}", r.Version));
            parameters.Add(new NpgsqlParameter($"p{p + 6}", r.OrgVersion));
            parameters.Add(new NpgsqlParameter($"p{p + 7}", r.Algorithm));
            parameters.Add(new NpgsqlParameter($"p{p + 8}", r.ComputedAt));
        }
        // Versions only grow: a row is replaced by one computed at the same-or-newer versions (never by a slower,
        // older computation), and not rewritten when nothing changes
        sql.Append("""
             ON CONFLICT ("OrganizationId", "UserId", "Month") DO UPDATE SET
                "FirstDay" = EXCLUDED."FirstDay", "OvertimeDelta" = EXCLUDED."OvertimeDelta", "Version" = EXCLUDED."Version",
                "OrgVersion" = EXCLUDED."OrgVersion", "Algorithm" = EXCLUDED."Algorithm", "ComputedAt" = EXCLUDED."ComputedAt"
             WHERE EXCLUDED."Version" >= "BalanceCheckpoints"."Version" AND EXCLUDED."OrgVersion" >= "BalanceCheckpoints"."OrgVersion"
               AND EXCLUDED."Algorithm" >= "BalanceCheckpoints"."Algorithm"
               AND (EXCLUDED."Version", EXCLUDED."OrgVersion", EXCLUDED."Algorithm")
                   <> ("BalanceCheckpoints"."Version", "BalanceCheckpoints"."OrgVersion", "BalanceCheckpoints"."Algorithm")
            """);
        await db.Database.ExecuteSqlRawAsync(sql.ToString(), parameters, ct);
    }

    private static async Task SaveTrackedAsync(TimeTrackingDbContext db, List<BalanceCheckpoint> rows, CancellationToken ct)
    {
        if (db.ChangeTracker.HasChanges()) return;
        var users = rows.Select(r => r.UserId).Distinct().ToList();
        var months = rows.Select(r => r.Month).Distinct().ToList();
        var orgId = rows[0].OrganizationId;
        var existing = (await db.BalanceCheckpoints
                .Where(c => c.OrganizationId == orgId && users.Contains(c.UserId) && months.Contains(c.Month))
                .ToListAsync(ct))
            // InMemory has no unique index: two concurrent first computations may both have added a month
            .GroupBy(c => (c.UserId, c.Month))
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var r in rows)
        {
            if (!existing.TryGetValue((r.UserId, r.Month), out var old)) { db.BalanceCheckpoints.Add(r); continue; }
            // Same rule as the Postgres upsert: only same-or-newer versions replace a row, identical ones rewrite nothing
            if (r.Version < old.Version || r.OrgVersion < old.OrgVersion || r.Algorithm < old.Algorithm) continue;
            if (r.Version == old.Version && r.OrgVersion == old.OrgVersion && r.Algorithm == old.Algorithm) continue;
            (old.FirstDay, old.OvertimeDelta, old.Version, old.OrgVersion, old.Algorithm, old.ComputedAt) =
                (r.FirstDay, r.OvertimeDelta, r.Version, r.OrgVersion, r.Algorithm, r.ComputedAt);
        }
        try
        {
            await db.SaveWithoutHooksAsync(ct);
        }
        finally
        {
            foreach (var entry in db.ChangeTracker.Entries<BalanceCheckpoint>().ToList())
                entry.State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Rows that can never be valid again: an old algorithm or version (versions only grow), a former membership row, or a
    /// deleted organization. Deleted by the retention cleanup (<c>RefreshTokenCleanupService</c>).
    /// </summary>
    public static IQueryable<BalanceCheckpoint> Invalid(TimeTrackingDbContext db) =>
        db.BalanceCheckpoints.IgnoreQueryFilters().Where(c => c.Algorithm != AlgorithmVersion
            || !c.Organization.IsActive
            || c.OrgVersion != c.Organization.BalanceVersion
            || !db.UserOrganizations.IgnoreQueryFilters()
                .Any(uo => uo.OrganizationId == c.OrganizationId && uo.UserId == c.UserId && uo.BalanceVersion == c.Version));

    /// <summary>
    /// Pre-warm (<see cref="BalanceCheckpointWarmupService"/>, system work): once per org-local month, from its 2nd on (the first
    /// day the month before is <see cref="Storable"/>; the 1st's auto-stop of that month's last timers came before), computes
    /// the balances of all active members, which stores the checkpoint of the month that just ended — balance reads then
    /// only compute the new month. One organization at a time, each saved on its own; a failing one is logged and retried on
    /// the next pass. Returns the number of organizations warmed.
    /// </summary>
    public static async Task<int> WarmAsync(TimeTrackingDbContext db, DateTime nowUtc, CancellationToken ct = default, ILogger? logger = null)
    {
        var orgIds = await db.Organizations.AsNoTracking().OrderBy(o => o.Id).Select(o => o.Id).ToListAsync(ct);
        var warmed = 0;
        foreach (var orgId in orgIds)
        {
            try
            {
                // Read now, not up front: a pass may run long, and the zone must be current (SaveAsync re-checks it anyway)
                var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orgId, ct);
                if (org == null) continue;
                var clock = OrgClock.For(org);
                var today = clock.Today(nowUtc);
                var month = new DateOnly(today.Year, today.Month, 1);
                if (org.BalanceWarmedMonth >= month || today < month.AddDays(1)) continue;
                var initial = await db.UserOrganizations.AsNoTracking()
                    .Where(uo => uo.OrganizationId == org.Id && uo.User.IsActive)
                    .ToDictionaryAsync(uo => uo.UserId, uo => uo.InitialOvertimeHours, ct);
                await DailyReportAggregator.TotalsAsync(db, org, initial, today, today, failureCounts: false);
                var tracked = await db.Organizations.FirstAsync(o => o.Id == org.Id, ct);
                tracked.BalanceWarmedMonth = month;
                await db.SaveChangesAsync(ct);
                warmed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                logger?.LogError(ex, "Error while pre-computing the balance checkpoints of organization {OrgId}.", orgId);
            }
        }
        return warmed;
    }
}

/// <summary>
/// The invalidation half of <see cref="BalanceCheckpoints"/>, run by <c>TimeTrackingDbContext.SaveChangesAsync</c> for every
/// save (system work too). A pending change of a balance input touching a month that has ended (last day before today; see
/// <see cref="BalanceCheckpoints.Storable"/> for the margin against readers) bumps the
/// version in the same save — per member for time entries (also a pure stop of a running timer and the auto-stop's failure
/// entries), absences and schedules (old and new owner/org); per organization for holidays, a custom absence type's credit
/// mode (or its deletion) and the time zone — and first carries the still valid checkpoints of earlier months over to the new
/// version, so only the months from the earliest touched one on are recomputed. A change in a month that has not ended
/// cannot touch a checkpoint (readers store a month only from the 2nd of the next one) and bumps nothing.
/// </summary>
internal sealed class BalanceInvalidation
{
    private readonly Dictionary<(int Org, int User), DateOnly> _members = [];
    private readonly Dictionary<int, DateOnly> _orgs = [];

    private static readonly HashSet<string> EntryFields =
    [
        nameof(TimeEntry.StartTime), nameof(TimeEntry.EndTime), nameof(TimeEntry.PauseDurationMinutes), nameof(TimeEntry.IsFailure),
        nameof(TimeEntry.IsRunning), nameof(TimeEntry.OrganizationId), nameof(TimeEntry.UserId)
    ];
    private static readonly HashSet<string> AbsenceFields =
    [
        nameof(AbsenceDay.Date), nameof(AbsenceDay.Type), nameof(AbsenceDay.IsHalfDay), nameof(AbsenceDay.CustomTypeId),
        nameof(AbsenceDay.OrganizationId), nameof(AbsenceDay.UserId)
    ];
    private static readonly HashSet<string> HolidayFields =
    [
        nameof(Holiday.Date), nameof(Holiday.IsRecurring), nameof(Holiday.IsHalfDay), nameof(Holiday.OrganizationId)
    ];

    /// <summary>The bumps the pending changes need, or null (the common case: nothing in an ended month).</summary>
    public static async Task<BalanceInvalidation?> CollectAsync(TimeTrackingDbContext db, CancellationToken ct)
    {
        var memberDays = new List<(int Org, int User, DateOnly? Day, DateTime? Utc)>();
        var orgDays = new List<(int Org, DateOnly Day)>();
        foreach (var e in db.ChangeTracker.Entries())
        {
            if (e.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            switch (e.Entity)
            {
                case TimeEntry:
                    if (!Relevant(e, EntryFields)) break;
                    // Running timers are not part of any balance: only a finished old/new state counts
                    foreach (var (values, _) in Sides(e))
                        if (values.Get<int?>(nameof(TimeEntry.OrganizationId)) is int org && !values.Get<bool>(nameof(TimeEntry.IsRunning)))
                            memberDays.Add((org, values.Get<int>(nameof(TimeEntry.UserId)), null, values.Get<DateTime>(nameof(TimeEntry.StartTime))));
                    break;
                case AbsenceDay:
                    if (!Relevant(e, AbsenceFields)) break;
                    foreach (var (values, _) in Sides(e))
                        memberDays.Add((values.Get<int>(nameof(AbsenceDay.OrganizationId)), values.Get<int>(nameof(AbsenceDay.UserId)),
                            values.Get<DateOnly>(nameof(AbsenceDay.Date)), null));
                    break;
                case WorkSchedule schedule:
                    if (e.State == EntityState.Modified && (Changed(e, nameof(WorkSchedule.OrganizationId)) || Changed(e, nameof(WorkSchedule.UserId))))
                    {
                        foreach (var (values, _) in Sides(e))
                            memberDays.Add((values.Get<int>(nameof(WorkSchedule.OrganizationId)), values.Get<int>(nameof(WorkSchedule.UserId)),
                                values.Get<DateOnly>(nameof(WorkSchedule.ValidFrom)), null));
                        break;
                    }
                    foreach (var (from, _) in ClosedPeriods.ScheduleRanges(e))
                        memberDays.Add((schedule.OrganizationId, schedule.UserId, from, null));
                    break;
                case Holiday:
                    if (!Relevant(e, HolidayFields)) break;
                    foreach (var (values, _) in Sides(e))
                        orgDays.Add((values.Get<int>(nameof(Holiday.OrganizationId)),
                            values.Get<bool>(nameof(Holiday.IsRecurring)) ? DateOnly.MinValue : values.Get<DateOnly>(nameof(Holiday.Date))));
                    break;
                case AbsenceTypeDefinition type:
                    // Types are soft-deleted (IsActive): their absences keep the type and its credit. Only a hard delete (none in
                    // the app) would null the absences' CustomTypeId in the database; covered just in case
                    if (e.State == EntityState.Deleted || (e.State == EntityState.Modified && Changed(e, nameof(AbsenceTypeDefinition.CreditMode))))
                        orgDays.Add((type.OrganizationId, DateOnly.MinValue));
                    break;
                case Organization org:
                    if (e.State == EntityState.Modified && Changed(e, nameof(Organization.TimeZone)))
                        orgDays.Add((org.Id, DateOnly.MinValue));
                    break;
            }
        }
        if (memberDays.Count == 0 && orgDays.Count == 0) return null;

        // Without a query: a day whose month has not ended in any time zone (local day >= UTC day - 1, local today <=
        // UTC today + 1) needs no bump — the usual case of a change today (timer stop, manual entry, absence)
        var latestToday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        static DateOnly LastDay(DateOnly d) => new DateOnly(d.Year, d.Month, 1).AddMonths(1).AddDays(-1);
        if (orgDays.Count == 0 && memberDays.All(d =>
                LastDay(d.Day ?? DateOnly.FromDateTime(d.Utc!.Value).AddDays(-1)) >= latestToday))
            return null;

        // The org-local clock (the time zone before this save) of every touched organization
        var orgIds = memberDays.Select(d => d.Org).Concat(orgDays.Select(d => d.Org)).Distinct().ToList();
        var zones = new Dictionary<int, string?>();
        foreach (var tracked in db.ChangeTracker.Entries<Organization>())
            if (orgIds.Contains(tracked.Entity.Id) && tracked.State != EntityState.Added)
                zones[tracked.Entity.Id] = (string?)tracked.Property(nameof(Organization.TimeZone)).OriginalValue;
        var missing = orgIds.Where(id => !zones.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            foreach (var o in await db.Organizations.IgnoreQueryFilters().AsNoTracking()
                         .Where(o => missing.Contains(o.Id)).Select(o => new { o.Id, o.TimeZone }).ToListAsync(ct))
                zones[o.Id] = o.TimeZone;
        var clocks = orgIds.ToDictionary(id => id, id => OrgClock.For(zones.GetValueOrDefault(id)));

        var result = new BalanceInvalidation();
        foreach (var (org, user, day, utc) in memberDays)
            if (EndedMonth(clocks[org], day ?? clocks[org].LocalDate(utc!.Value)) is { } month)
                result._members[(org, user)] = result._members.TryGetValue((org, user), out var m) && m < month ? m : month;
        foreach (var (org, day) in orgDays)
            if (EndedMonth(clocks[org], day) is { } month)
                result._orgs[org] = result._orgs.TryGetValue(org, out var m) && m < month ? m : month;
        return result._members.Count == 0 && result._orgs.Count == 0 ? null : result;
    }

    /// <summary>
    /// The first day of <paramref name="day"/>'s month when a reader may have stored a checkpoint for it or a later month:
    /// the month has ended (last day before today — the writer half of the invariant at <see cref="BalanceCheckpoints.Storable"/>).
    /// <see cref="DateOnly.MinValue"/> stays (every month).
    /// </summary>
    private static DateOnly? EndedMonth(OrgClock clock, DateOnly day)
    {
        if (day == DateOnly.MinValue) return day;
        var month = new DateOnly(day.Year, day.Month, 1);
        return month.AddMonths(1).AddDays(-1) < clock.Today() ? month : null;
    }

    /// <summary>
    /// InMemory (after the closed-period guard, before the save, part of it): bumps the tracked membership/organization rows and moves the checkpoints of
    /// months before the earliest touched one to the new version.
    /// </summary>
    public async Task ApplyTrackedAsync(TimeTrackingDbContext db, CancellationToken ct)
    {
        foreach (var ((orgId, userId), from) in _members.OrderBy(m => m.Key.Org).ThenBy(m => m.Key.User))
        {
            var membership = await db.UserOrganizations.IgnoreQueryFilters()
                .FirstOrDefaultAsync(uo => uo.OrganizationId == orgId && uo.UserId == userId, ct);
            if (membership == null) continue;
            var old = membership.BalanceVersion++;
            foreach (var c in await db.BalanceCheckpoints
                         .Where(c => c.OrganizationId == orgId && c.UserId == userId && c.Version == old && c.Month < from).ToListAsync(ct))
                c.Version = old + 1;
        }
        foreach (var (orgId, from) in _orgs.OrderBy(o => o.Key))
        {
            var org = await db.Organizations.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == orgId, ct);
            if (org == null) continue;
            var old = org.BalanceVersion++;
            foreach (var c in await db.BalanceCheckpoints
                         .Where(c => c.OrganizationId == orgId && c.OrgVersion == old && c.Month < from).ToListAsync(ct))
                c.OrgVersion = old + 1;
        }
    }

    /// <summary>
    /// Postgres (after the save, inside its transaction): increments the version in the database (the row lock orders
    /// concurrent bumps of one member/org until commit, so each carries exactly the rows of the version before it), then
    /// carries the checkpoints of earlier months over. Rows the caller cannot see (RLS) are not carried — they just become
    /// invalid and are recomputed.
    /// </summary>
    public async Task ApplySqlAsync(TimeTrackingDbContext db, CancellationToken ct)
    {
        foreach (var ((orgId, userId), from) in _members.OrderBy(m => m.Key.Org).ThenBy(m => m.Key.User))
        {
            var bumped = await db.Database.SqlQuery<int>($"""
                UPDATE "UserOrganizations" SET "BalanceVersion" = "BalanceVersion" + 1
                WHERE "OrganizationId" = {orgId} AND "UserId" = {userId} RETURNING "BalanceVersion" AS "Value"
                """).ToListAsync(ct);
            if (bumped.Count == 0 || from == DateOnly.MinValue) continue;
            var version = bumped[0];
            await db.Database.ExecuteSqlAsync($"""
                UPDATE "BalanceCheckpoints" SET "Version" = {version}
                WHERE "OrganizationId" = {orgId} AND "UserId" = {userId} AND "Version" = {version - 1} AND "Month" < {from}
                """, ct);
        }
        foreach (var (orgId, from) in _orgs.OrderBy(o => o.Key))
        {
            var bumped = await db.Database.SqlQuery<int>($"""
                UPDATE "Organizations" SET "BalanceVersion" = "BalanceVersion" + 1
                WHERE "Id" = {orgId} RETURNING "BalanceVersion" AS "Value"
                """).ToListAsync(ct);
            if (bumped.Count == 0 || from == DateOnly.MinValue) continue;
            var version = bumped[0];
            await db.Database.ExecuteSqlAsync($"""
                UPDATE "BalanceCheckpoints" SET "OrgVersion" = {version}
                WHERE "OrganizationId" = {orgId} AND "OrgVersion" = {version - 1} AND "Month" < {from}
                """, ct);
        }
    }

    private static bool Relevant(EntityEntry e, HashSet<string> fields) =>
        e.State != EntityState.Modified || e.Properties.Any(p => p.IsModified && fields.Contains(p.Metadata.Name)
                                                                && !Equals(p.OriginalValue, p.CurrentValue));

    private static bool Changed(EntityEntry e, string property) =>
        !Equals(e.Property(property).OriginalValue, e.Property(property).CurrentValue);

    /// <summary>The old values (modified/deleted) and the new ones (added/modified) of an entry.</summary>
    private static IEnumerable<(PropertyValues Values, bool Old)> Sides(EntityEntry e)
    {
        if (e.State != EntityState.Added) yield return (e.OriginalValues, true);
        if (e.State != EntityState.Deleted) yield return (e.CurrentValues, false);
    }
}

internal static class PropertyValuesExtensions
{
    public static T Get<T>(this PropertyValues values, string property) => (T)values[property]!;
}
