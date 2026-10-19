using System.Data.Common;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// Balance checkpoints under concurrency and row-level security on real Postgres (Phase 2 of docs/PERFORMANCE_PLAN.md).
/// A reader (a balance computation) is paused at a chosen SQL command by an interceptor while a writer changes a balance
/// input in another context and commits — or holds its transaction open while the reader runs — then the reader goes on.
/// Whatever the interleaving, no stale checkpoint may be valid afterwards and the next read equals the full recompute.
/// </summary>
[Collection(PostgresCollection.Name)]
public class BalanceCheckpointConcurrencyPostgresTests(PostgresApiFactory factory)
{
    private readonly BalanceHarness _h = new(factory);

    private string Superuser => new NpgsqlConnectionStringBuilder(factory.SuperuserConnectionString) { Database = "timetracking" }.ConnectionString;

    private TimeTrackingDbContext Db(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<TimeTrackingDbContext>().UseNpgsql(Superuser);
        if (interceptors.Length > 0) options.AddInterceptors(interceptors);
        return new TimeTrackingDbContext(options.Options);
    }

    /// <summary>Pauses the first command matching <see cref="Match"/> until <see cref="Release"/>; <see cref="Reached"/> fires then.</summary>
    private sealed class Gate(Func<string, bool> match, bool pause = true) : DbCommandInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _hit;

        private async Task MaybeAsync(DbCommand command)
        {
            if (!match(command.CommandText) || Interlocked.Exchange(ref _hit, 1) == 1) return;
            Reached.TrySetResult();
            if (pause) await Release.Task.WaitAsync(TimeSpan.FromSeconds(60));
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await MaybeAsync(command);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await MaybeAsync(command);
            return result;
        }
    }

    /// <summary>The command after <c>LoadAsync</c> (the one reading the versions): "after the versions were read".</summary>
    private static Func<string, bool> AfterLoad()
    {
        var seen = false;
        return sql =>
        {
            if (sql.Contains("\"BalanceVersion\"") && sql.Contains("\"BalanceCheckpoints\"")) { seen = true; return false; }
            return seen;
        };
    }

    private static bool IsLoad(string sql) => sql.Contains("\"BalanceVersion\"") && sql.Contains("\"BalanceCheckpoints\"") && sql.StartsWith("SELECT");
    private static bool IsUpsert(string sql) => sql.Contains("INSERT INTO \"BalanceCheckpoints\"");

    private async Task<(int OrgId, int UserId, OrgClock Clock)> WorldAsync(string tag)
    {
        _ = factory.Server;
        var clock = OrgClock.For("Europe/Berlin");
        var today = clock.Today();
        await using var db = Db();
        var month = BalanceHarness.MonthOf(today);
        var org = new Organization { Name = tag, Slug = tag, MemberTimeEntryVisibility = true, BalanceWarmedMonth = month.AddMonths(1) };
        var user = new User { Email = $"{tag}@conc.test", FirstName = "Con", LastName = "Current" };
        db.AddRange(org, user);
        db.UserOrganizations.Add(new UserOrganization { User = user, Organization = org, InitialOvertimeHours = 1.5 });
        var start = month.AddMonths(-10);
        db.WorkSchedules.Add(new WorkSchedule
        {
            User = user, Organization = org, ValidFrom = start, WeeklyWorkHours = 40, TargetMon = 8, TargetTue = 8, TargetWed = 8, TargetThu = 8, TargetFri = 8
        });
        var i = 0;
        for (var d = start; d < today; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            // Late starts: a time-zone change moves these entries to other local days
            var s = clock.ToUtc(d.ToDateTime(new TimeOnly(i++ % 3 == 0 ? 23 : 9, 0)));
            db.TimeEntries.Add(new TimeEntry
            {
                User = user, Organization = org, StartTime = s, EndTime = s.AddMinutes(420 + i % 11 * 9), PauseDurationMinutes = 30,
                PauseIsManual = true, CreatedAt = s, UpdatedAt = s
            });
        }
        await db.SaveChangesAsync();
        return (org.Id, user.Id, clock);
    }

    /// <summary>The reader: <c>BalancesAsync</c> (the team overview's computation) with its own context.</summary>
    private async Task ReadAsync(int orgId, int userId, params IInterceptor[] interceptors)
    {
        await using var db = Db(interceptors);
        var org = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == orgId);
        await DailyReportAggregator.BalancesAsync(db, org, new Dictionary<int, double> { [userId] = 1.5 });
    }

    private async Task AssertConsistentAsync(int orgId, int userId, string because)
    {
        await _h.AssertValidCheckpointsAsync([orgId], because);
        await ReadAsync(orgId, userId);
        await _h.AssertValidCheckpointsAsync([orgId], because + ", after the next read");
        (await BalanceHarness.ValidCheckpointsAsync(Db(), [orgId])).Should().NotBeEmpty("the next read stores valid rows again");
        await using var db = Db();
        var reference = await BalanceHarness.ReferenceAsync(db, userId, orgId);
        var (_, overall) = await DailyReportAggregator.BuildWithRunningTotalAsync(db, userId, orgId, reference.Today, reference.Today, 1.5);
        Math.Abs(overall - reference.Balance(1.5)).Should().BeLessThanOrEqualTo(BalanceHarness.Exact, because);
    }

    /// <summary>Edits an entry two months back (an ended month) and commits.</summary>
    private async Task EditPastEntryAsync(int orgId, int userId, OrgClock clock, TimeTrackingDbContext? db = null)
    {
        var own = db == null;
        db ??= Db();
        try
        {
            var from = clock.StartOfDayUtc(BalanceHarness.MonthOf(clock.Today()).AddMonths(-2).AddDays(3));
            var e = await db.TimeEntries.Where(x => x.OrganizationId == orgId && x.UserId == userId && x.StartTime >= from).OrderBy(x => x.StartTime).FirstAsync();
            e.EndTime = e.EndTime!.Value.AddHours(2);
            await db.SaveChangesAsync();
        }
        finally
        {
            if (own) await db.DisposeAsync();
        }
    }

    private async Task ChangeTimeZoneAsync(int orgId, string zone)
    {
        await using var db = Db();
        (await db.Organizations.SingleAsync(o => o.Id == orgId)).TimeZone = zone;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task WriterCommitsBetweenTheReadersVersionsAndItsData_NoStaleValidRow()
    {
        var (orgId, userId, clock) = await WorldAsync("conc-after-load");
        var gate = new Gate(AfterLoad());
        var reader = ReadAsync(orgId, userId, gate);
        await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60));
        await EditPastEntryAsync(orgId, userId, clock);
        gate.Release.SetResult();
        await reader;
        await AssertConsistentAsync(orgId, userId, "writer committed after the reader read the versions");
    }

    [Fact]
    public async Task WriterCommitsBeforeTheReadersUpsert_NoStaleValidRow()
    {
        var (orgId, userId, clock) = await WorldAsync("conc-before-upsert");
        await ReadAsync(orgId, userId);
        // Invalidate from two months back, so the paused reader has rows to store from its (soon stale) data
        await EditPastEntryAsync(orgId, userId, clock);
        var gate = new Gate(IsUpsert);
        var reader = ReadAsync(orgId, userId, gate);
        await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60));
        await EditPastEntryAsync(orgId, userId, clock);
        gate.Release.SetResult();
        await reader;
        await AssertConsistentAsync(orgId, userId, "writer committed between the reader's data and its upsert");
    }

    [Fact]
    public async Task ReaderRunsWhileTheWritersTransactionIsOpen_NoStaleValidRow()
    {
        var (orgId, userId, clock) = await WorldAsync("conc-open-tx");
        await ReadAsync(orgId, userId);
        // The last three months are missing, so the reader computes them (from data the writer is about to change) and stores them
        var recent = BalanceHarness.MonthOf(clock.Today()).AddMonths(-3);
        await using (var db = Db())
            await db.BalanceCheckpoints.Where(c => c.OrganizationId == orgId && c.Month >= recent).ExecuteDeleteAsync();
        await using var writer = Db();
        await using var tx = await writer.Database.BeginTransactionAsync();
        await EditPastEntryAsync(orgId, userId, clock, writer);   // bump + carry inside the open transaction
        var gate = new Gate(IsUpsert, pause: false);
        var reader = ReadAsync(orgId, userId, gate);
        // The reader's upsert may wait for the writer's row locks: commit once it got there (or finished)
        await Task.WhenAny(reader, gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60)));
        await tx.CommitAsync();
        await reader.WaitAsync(TimeSpan.FromSeconds(60));
        await AssertConsistentAsync(orgId, userId, "reader ran while the writer's transaction was open");
    }

    [Fact]
    public async Task TimeZoneChange_BeforeTheReadersVersions_StoresNothingStale()
    {
        var (orgId, userId, _) = await WorldAsync("conc-tz-before");
        // The reader loaded the organization (old zone) and is about to read the versions when the zone changes
        var gate = new Gate(IsLoad);
        var reader = ReadAsync(orgId, userId, gate);
        await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60));
        await ChangeTimeZoneAsync(orgId, "Pacific/Kiritimati");
        gate.Release.SetResult();
        await reader;
        await AssertConsistentAsync(orgId, userId, "time zone changed before the reader read the versions");
    }

    [Fact]
    public async Task TimeZoneChange_AfterTheReadersVersions_StoresNothingStale()
    {
        var (orgId, userId, _) = await WorldAsync("conc-tz-after");
        var gate = new Gate(AfterLoad());
        var reader = ReadAsync(orgId, userId, gate);
        await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60));
        await ChangeTimeZoneAsync(orgId, "Pacific/Kiritimati");
        gate.Release.SetResult();
        await reader;
        await AssertConsistentAsync(orgId, userId, "time zone changed after the reader read the versions");
    }

    // ── Row-level security ──

    private async Task<NpgsqlConnection> OpenAsAsync(int userId)
    {
        var conn = new NpgsqlConnection(factory.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.user_id', @u, false), set_config('app.bypass_rls', 'off', false)";
        cmd.Parameters.AddWithValue("u", userId.ToString());
        await cmd.ExecuteNonQueryAsync();
        return conn;
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection conn, string sql, params (string, object)[] p)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in p) cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary>An org (owner + member over HTTP) whose member has three months of entries.</summary>
    private async Task<(HttpClient Owner, HttpClient Member, string Slug, int OrgId, int MemberId)> RlsOrgAsync(string prefix)
    {
        var (owner, member, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(factory, prefix, new { memberTimeEntryVisibility = true }, "rlsbal.test");
        await _h.SkipPrewarmAsync(orgId);
        await using var db = Db();
        var clock = OrgClock.For("Europe/Berlin");
        var today = clock.Today();
        for (var d = BalanceHarness.MonthOf(today).AddMonths(-3); d < today; d = d.AddDays(2))
        {
            var s = clock.ToUtc(d.ToDateTime(new TimeOnly(9, 0)));
            db.TimeEntries.Add(new TimeEntry { UserId = memberId, OrganizationId = orgId, StartTime = s, EndTime = s.AddHours(6), CreatedAt = s, UpdatedAt = s });
        }
        await db.SaveChangesAsync();
        return (owner, member, slug, orgId, memberId);
    }

    private async Task<int> RowsAsync(int orgId, int userId)
    {
        await using var db = Db();
        return await db.BalanceCheckpoints.CountAsync(c => c.OrganizationId == orgId && c.UserId == userId);
    }

    [Fact]
    public async Task Rls_AMemberCannotReadOrWriteAnotherOrganizationsCheckpoints()
    {
        var a = await RlsOrgAsync("rlsbal-a");
        var b = await RlsOrgAsync("rlsbal-b");
        (await a.Owner.GetAsync($"/api/v1/organizations/{a.Slug}/team-overview")).EnsureSuccessStatusCode();
        (await RowsAsync(a.OrgId, a.MemberId)).Should().BeGreaterThan(0);

        await using var asB = await OpenAsAsync(b.MemberId);
        (await ScalarAsync(asB, """SELECT count(*) FROM "BalanceCheckpoints" WHERE "OrganizationId" = @o""", ("o", a.OrgId))).Should().Be(0);
        (await ScalarAsync(asB, """WITH u AS (UPDATE "BalanceCheckpoints" SET "OvertimeDelta" = 999 WHERE "OrganizationId" = @o RETURNING 1) SELECT count(*) FROM u""",
            ("o", a.OrgId))).Should().Be(0, "RLS hides the rows from UPDATE");
        (await ScalarAsync(asB, """WITH d AS (DELETE FROM "BalanceCheckpoints" WHERE "OrganizationId" = @o RETURNING 1) SELECT count(*) FROM d""",
            ("o", a.OrgId))).Should().Be(0, "RLS hides the rows from DELETE");
        var insert = async () => await ScalarAsync(asB, """
            INSERT INTO "BalanceCheckpoints" ("OrganizationId", "UserId", "Month", "FirstDay", "OvertimeDelta", "Version", "OrgVersion", "Algorithm", "ComputedAt")
            VALUES (@o, @u, DATE '2001-01-01', DATE '2001-01-01', 1, 0, 0, 1, now()) RETURNING 1
            """, ("o", a.OrgId), ("u", a.MemberId));
        (await insert.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        (await RowsAsync(a.OrgId, a.MemberId)).Should().BeGreaterThan(0);
        await _h.AssertValidCheckpointsAsync([a.OrgId], "after the forbidden writes");
    }

    [Fact]
    public async Task Rls_TheAdminsTeamOverviewWritesTheMembersCheckpoints_AMembersOwnBalanceTheirs()
    {
        var a = await RlsOrgAsync("rlsbal-w");
        (await RowsAsync(a.OrgId, a.MemberId)).Should().Be(0);

        var overview = await a.Owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{a.Slug}/team-overview");
        overview.GetProperty("members").EnumerateArray().Should().Contain(m => m.GetProperty("userId").GetInt32() == a.MemberId);
        (await RowsAsync(a.OrgId, a.MemberId)).Should().BeGreaterThanOrEqualTo(2, "the owner's request (RLS as the owner) stored the member's months");
        await _h.AssertValidCheckpointsAsync([a.OrgId], "after the team overview");

        await using (var db = Db())
            await db.BalanceCheckpoints.Where(c => c.OrganizationId == a.OrgId).ExecuteDeleteAsync();
        var balance = await a.Member.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{a.Slug}/members/{a.MemberId}/balance");
        (await RowsAsync(a.OrgId, a.MemberId)).Should().BeGreaterThanOrEqualTo(2, "the member's own request (RLS as the member) stored their months");
        await _h.AssertValidCheckpointsAsync([a.OrgId], "after the member's balance");
        await using var check = Db();
        var reference = await BalanceHarness.ReferenceAsync(check, a.MemberId, a.OrgId);
        balance.GetProperty("cumulativeOvertimeHours").GetDouble().Should().Be(Math.Round(reference.Balance(0), 2));
    }
}
