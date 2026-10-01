using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Npgsql;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// Manual entries, admin corrections and their history on real Postgres: the history is written
/// in the same transaction under row-level security, and is invisible to other organizations.
/// </summary>
[Collection(PostgresCollection.Name)]
public class EntryHistoryRlsTests
{
    private readonly PostgresApiFactory _factory;

    public EntryHistoryRlsTests(PostgresApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ManualEntryAndAdminCorrection_WriteHistoryUnderRls_OtherOrgsSeeNone()
    {
        var (owner, ownerLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "hist-owner@pg.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "History Org", "pg-history-org");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { memberTimeEntryVisibility = true })).EnsureSuccessStatusCode();
        var (member, m) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "hist-m@pg.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, m)).EnsureSuccessStatusCode();

        var manual = await member.PostAsJsonAsync("/api/v1/TimeTracking/manual", new
        {
            startTime = new DateTime(2026, 9, 14, 6, 0, 0, DateTimeKind.Utc), endTime = new DateTime(2026, 9, 14, 14, 0, 0, DateTimeKind.Utc),
            organizationId = org.Id
        });
        manual.StatusCode.Should().Be(HttpStatusCode.Created);
        var entryId = (await manual.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("entry").GetProperty("id").GetInt32();

        (await owner.PutAsJsonAsync($"/api/v1/organizations/{org.Slug}/members/{m.User.Id}/entries/{entryId}",
            new { endTime = new DateTime(2026, 9, 14, 15, 0, 0, DateTimeKind.Utc), pauseDurationMinutes = 45 })).EnsureSuccessStatusCode();

        var history = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/TimeTracking/{entryId}/history");
        history.EnumerateArray().Select(c => (c.GetProperty("kind").GetString(), c.GetProperty("changedByUserId").GetInt32()))
            .Should().Equal(("Created", m.User.Id), ("Updated", ownerLogin.User.Id));

        var (outsider, o) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "hist-out@pg.test");
        (await outsider.GetAsync($"/api/v1/TimeTracking/{entryId}/history")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var conn = new NpgsqlConnection(_factory.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT set_config('app.user_id', @u, false), set_config('app.bypass_rls', 'off', false);
            SELECT count(*) FROM "TimeEntryChanges";
            """;
        cmd.Parameters.AddWithValue("u", o.User.Id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.NextResultAsync();
        await reader.ReadAsync();
        reader.GetInt64(0).Should().Be(0);
    }
}
