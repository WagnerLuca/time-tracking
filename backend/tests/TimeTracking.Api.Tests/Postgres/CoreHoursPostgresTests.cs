using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// The core hours background check on real Postgres: it runs outside a request (RLS bypass) and
/// writes its rows, admins read them back under RLS, other organizations see none.
/// </summary>
[Collection(PostgresCollection.Name)]
public class CoreHoursPostgresTests
{
    private readonly PostgresApiFactory _factory;

    public CoreHoursPostgresTests(PostgresApiFactory factory) => _factory = factory;

    [Fact]
    public async Task BackgroundCheck_WritesUnderBypass_VisibleToOrgOnly()
    {
        var (owner, _, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "pg-core",
            new { coreHoursEnabled = true, coreHoursStart = "09:00", coreHoursEnd = "15:00", memberTimeEntryVisibility = true }, "pg.test");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            db.TimeEntries.Add(new TimeEntry
            {
                UserId = memberId, OrganizationId = orgId,
                StartTime = new DateTime(2026, 9, 15, 7, 0, 0, DateTimeKind.Utc), EndTime = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc)   // Tue 15.09., 09:00-14:00 Berlin (UTC+2)
            });
            // As if the core hours and memberships had been in place before that day (the API stamps "now")
            (await db.Organizations.SingleAsync(o => o.Id == orgId)).CoreHoursChangedAt = null;
            foreach (var m in await db.UserOrganizations.Where(uo => uo.OrganizationId == orgId).ToListAsync())
                m.JoinedAt = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);   // that day, not the catch-up days before
            await db.SaveChangesAsync();

            var now = new DateTime(2026, 9, 15, 14, 0, 0, DateTimeKind.Utc);   // 16:00 local
            (await CoreHours.CheckDueAsync(db, now)).Should().Be(2);   // the member left early, the owner tracked nothing
            await CoreHours.CheckDueAsync(db, now.AddMinutes(1));
            (await db.CoreHoursChecks.CountAsync(c => c.OrganizationId == orgId)).Should().Be(2);
            (await db.Notifications.CountAsync(n => n.Type == CoreHours.NotificationType && n.RelatedEntityId == memberId)).Should().Be(2);
        }

        var days = await owner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/organizations/{slug}/members/{memberId}/compliance?from=2026-09-01&to=2026-09-30");
        days!.Single().GetProperty("coreHoursIssues").EnumerateArray().Select(i => i.GetString()).Should().Equal("early");

        var (_, outsider) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-core-out@pg.test");
        (await CountAsUserAsync(outsider.User.Id, orgId)).Should().Be(0);
        (await CountAsUserAsync(memberId, orgId)).Should().Be(2);   // own row + the org's
    }

    /// <summary>Fails every save that adds a core hours check of one organization.</summary>
    private sealed class FailingOrgInterceptor(int orgId) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            eventData.Context!.ChangeTracker.Entries<CoreHoursCheck>().Any(e => e.State == EntityState.Added && e.Entity.OrganizationId == orgId)
                ? throw new InvalidOperationException("broken organization")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private TimeTrackingDbContext Db(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<TimeTrackingDbContext>().UseNpgsql(
            new NpgsqlConnectionStringBuilder(_factory.SuperuserConnectionString) { Database = "timetracking" }.ConnectionString);
        if (interceptor != null) options.AddInterceptors(interceptor);
        return new TimeTrackingDbContext(options.Options);
    }

    private static readonly DateOnly Day = new(2026, 6, 2);   // Tuesday, before anyone else's members joined
    private static readonly DateTime AfterWindow = new(2026, 6, 2, 13, 10, 0, DateTimeKind.Utc);   // 15:10 Berlin

    /// <summary>An org with core hours 09:00–15:00 whose members joined at the start of <see cref="Day"/>.</summary>
    private async Task<(int OrgId, int MemberId)> CoreOrgAsync(string prefix)
    {
        var (_, _, _, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, prefix,
            new { coreHoursEnabled = true, coreHoursStart = "09:00", coreHoursEnd = "15:00", coreHoursToleranceMinutes = 5 }, "pg.test");
        await using var db = Db();
        (await db.Organizations.SingleAsync(o => o.Id == orgId)).CoreHoursChangedAt = null;
        foreach (var m in await db.UserOrganizations.Where(uo => uo.OrganizationId == orgId).ToListAsync())
            m.JoinedAt = new DateTime(2026, 6, 1, 22, 0, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();
        return (orgId, memberId);
    }

    [Fact]
    public async Task NothingDue_CostsOneQueryPerOrganization()
    {
        var (orgId, memberId) = await CoreOrgAsync("pg-core-cheap");
        var counter = new QueryCounter();
        await using var db = Db(counter);
        var org = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == orgId);
        var tree = await GroupTree.LoadAsync(db, orgId);

        counter.Count = 0;
        (await CoreHours.CheckOrgAsync(db, org, tree, AfterWindow.AddHours(-3), null, null, default)).Should().Be(0);   // window not over
        counter.Count.Should().BeLessThanOrEqualTo(1);

        (await CoreHours.CheckOrgAsync(db, org, tree, AfterWindow, null, null, default)).Should().Be(2);   // both missed it
        counter.Count = 0;
        (await CoreHours.CheckOrgAsync(db, org, tree, AfterWindow.AddMinutes(1), null, null, default)).Should().Be(0);   // already checked
        counter.Count.Should().BeLessThanOrEqualTo(1);
        (await db.CoreHoursChecks.CountAsync(c => c.OrganizationId == orgId && c.UserId == memberId)).Should().Be(1);
        await SwitchOffAsync(orgId);
    }

    /// <summary>Other tests run the check for every organization in this shared database: leave none behind.</summary>
    private async Task SwitchOffAsync(params int[] orgIds)
    {
        await using var db = Db();
        foreach (var org in await db.Organizations.Where(o => orgIds.Contains(o.Id)).ToListAsync())
            org.CoreHoursEnabled = false;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task OneFailingOrganization_DoesNotBlockTheOthers()
    {
        var (brokenId, _) = await CoreOrgAsync("pg-core-broken");
        var (fineId, fineMember) = await CoreOrgAsync("pg-core-fine");

        await using (var db = Db(new FailingOrgInterceptor(brokenId)))
            (await CoreHours.CheckDueAsync(db, AfterWindow, logger: NullLogger.Instance)).Should().BeGreaterThanOrEqualTo(2);

        await using (var db = Db())
        {
            (await db.CoreHoursChecks.CountAsync(c => c.OrganizationId == brokenId)).Should().Be(0);
            (await db.CoreHoursChecks.CountAsync(c => c.OrganizationId == fineId)).Should().Be(2);
            (await db.Notifications.CountAsync(n => n.Type == CoreHours.NotificationType && n.RelatedEntityId == fineMember)).Should().Be(1);
            // The next run retries the broken one
            (await CoreHours.CheckDueAsync(db, AfterWindow.AddMinutes(1))).Should().BeGreaterThanOrEqualTo(2);
            (await db.CoreHoursChecks.CountAsync(c => c.OrganizationId == brokenId)).Should().Be(2);
        }
        await SwitchOffAsync(brokenId, fineId);
    }

    private async Task<long> CountAsUserAsync(int userId, int orgId)
    {
        await using var conn = new NpgsqlConnection(_factory.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT set_config('app.user_id', @u, false), set_config('app.bypass_rls', 'off', false);
            SELECT count(*) FROM "CoreHoursChecks" WHERE "OrganizationId" = @org;
            """;
        cmd.Parameters.AddWithValue("u", userId.ToString());
        cmd.Parameters.AddWithValue("org", orgId);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.NextResultAsync();
        await reader.ReadAsync();
        return reader.GetInt64(0);
    }
}
