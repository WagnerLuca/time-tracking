using System.Net.Http.Json;
using FluentAssertions;
using Npgsql;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// Row-level security on TimeEntries/AbsenceDays, checked directly in the database as the
/// app role: even a query without any WHERE clause (i.e. a buggy service) must not see or
/// write another organization's tracked time.
/// </summary>
[Collection(PostgresCollection.Name)]
public class RowLevelSecurityTests
{
    private readonly PostgresApiFactory _factory;

    public RowLevelSecurityTests(PostgresApiFactory factory) => _factory = factory;

    private async Task<(int UserId, int OrgId)> UserWithOrgAndEntryAsync(string prefix)
    {
        var (client, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}@rls.test");
        var org = await TestHelpers.CreateOrganizationAsync(client, $"{prefix} Org", $"{prefix}-org");
        (await client.PostAsJsonAsync("/api/v1/TimeTracking/start", new { description = $"{prefix} work", organizationId = org.Id })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/absences",
            new { date = "2026-03-02", type = 1, note = "rls" })).IsSuccessStatusCode.Should().BeTrue();
        return (login.User.Id, org.Id);
    }

    /// <summary>Opens a connection as the app role with the given RLS session settings.</summary>
    private async Task<NpgsqlConnection> OpenAsAsync(int? userId, bool bypass = false)
    {
        var conn = new NpgsqlConnection(_factory.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.user_id', @u, false), set_config('app.bypass_rls', @b, false)";
        cmd.Parameters.AddWithValue("u", userId?.ToString() ?? "");
        cmd.Parameters.AddWithValue("b", bypass ? "on" : "off");
        await cmd.ExecuteNonQueryAsync();
        return conn;
    }

    private static async Task<long> CountAsync(NpgsqlConnection conn, string sql, int? orgId = null)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (orgId.HasValue) cmd.Parameters.AddWithValue("org", orgId.Value);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task User_CannotSeeAnotherOrganizationsTimeEntriesOrAbsences_EvenWithoutFilter()
    {
        var a = await UserWithOrgAndEntryAsync("rls-a");
        var b = await UserWithOrgAndEntryAsync("rls-b");

        await using var asA = await OpenAsAsync(a.UserId);

        (await CountAsync(asA, "SELECT count(*) FROM \"TimeEntries\" WHERE \"OrganizationId\" = @org", b.OrgId)).Should().Be(0);
        (await CountAsync(asA, "SELECT count(*) FROM \"AbsenceDays\" WHERE \"OrganizationId\" = @org", b.OrgId)).Should().Be(0);
        (await CountAsync(asA, "SELECT count(*) FROM \"TimeEntries\" WHERE \"OrganizationId\" = @org", a.OrgId)).Should().BeGreaterThan(0);

        // The unfiltered table only ever contains rows of A's own organizations or A's own entries
        (await CountAsync(asA, """
            SELECT count(*) FROM "TimeEntries" te
            WHERE te."UserId" <> NULLIF(current_setting('app.user_id'), '')::int
              AND te."OrganizationId" NOT IN (SELECT "OrganizationId" FROM "UserOrganizations" WHERE "UserId" = NULLIF(current_setting('app.user_id'), '')::int)
            """)).Should().Be(0);
    }

    [Fact]
    public async Task AnonymousSession_SeesNothing_SystemSessionSeesEverything()
    {
        var b = await UserWithOrgAndEntryAsync("rls-anon");

        await using var anonymous = await OpenAsAsync(null);
        (await CountAsync(anonymous, "SELECT count(*) FROM \"TimeEntries\"")).Should().Be(0);

        await using var system = await OpenAsAsync(null, bypass: true);
        (await CountAsync(system, "SELECT count(*) FROM \"TimeEntries\" WHERE \"OrganizationId\" = @org", b.OrgId)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task User_CannotWriteIntoAnotherOrganization()
    {
        var a = await UserWithOrgAndEntryAsync("rls-write-a");
        var b = await UserWithOrgAndEntryAsync("rls-write-b");

        await using var asA = await OpenAsAsync(a.UserId);
        await using var cmd = asA.CreateCommand();
        cmd.CommandText = """
            INSERT INTO "TimeEntries" ("UserId", "OrganizationId", "StartTime", "EndTime", "IsRunning", "PauseDurationMinutes", "IsFailure", "CreatedAt", "UpdatedAt")
            VALUES (@user, @org, now(), now(), false, 0, false, now(), now())
            """;
        cmd.Parameters.AddWithValue("user", b.UserId);
        cmd.Parameters.AddWithValue("org", b.OrgId);

        var act = async () => await cmd.ExecuteNonQueryAsync();
        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501"); // insufficient_privilege (RLS)
    }

    [Fact]
    public async Task LeavingAnOrganization_RevokesAccessToItsRows()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "rls-leave-owner@rls.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Leave Org", "rls-leave-org");
        (await owner.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationId = org.Id })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null)).EnsureSuccessStatusCode();

        var (_, member) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "rls-leave-m@rls.test");
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{org.Slug}/members", new { userId = member.User.Id })).EnsureSuccessStatusCode();

        await using (var asMember = await OpenAsAsync(member.User.Id))
            (await CountAsync(asMember, "SELECT count(*) FROM \"TimeEntries\" WHERE \"OrganizationId\" = @org", org.Id)).Should().BeGreaterThan(0);

        (await owner.DeleteAsync($"/api/v1/Organizations/{org.Slug}/members/{member.User.Id}")).EnsureSuccessStatusCode();

        await using (var asFormerMember = await OpenAsAsync(member.User.Id))
            (await CountAsync(asFormerMember, "SELECT count(*) FROM \"TimeEntries\" WHERE \"OrganizationId\" = @org", org.Id)).Should().Be(0);
    }

    [Fact]
    public async Task AppRole_IsNotSuperuser_SoPoliciesActuallyApply()
    {
        await using var conn = new NpgsqlConnection(_factory.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user";
        ((bool)(await cmd.ExecuteScalarAsync())!).Should().BeFalse();
    }
}
