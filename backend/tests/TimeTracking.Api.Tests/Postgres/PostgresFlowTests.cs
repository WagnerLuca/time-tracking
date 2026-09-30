using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TimeTracking.Api.Data;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// Flows that behave differently on real Postgres than on EF InMemory: RLS on anonymous
/// and background paths, unique indexes, DateTime kinds.
/// </summary>
[Collection(PostgresCollection.Name)]
public class PostgresFlowTests
{
    private readonly PostgresApiFactory _factory;

    public PostgresFlowTests(PostgresApiFactory factory) => _factory = factory;

    [Fact]
    public async Task TerminalPunch_WorksUnderRowLevelSecurity()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-punch-owner@pg.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Punch Org", "pg-punch-org");
        var (member, m) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-punch-m@pg.test");
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{org.Slug}/members", new { userId = m.User.Id })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/organizations/{org.Slug}/members/{m.User.Id}/badge", new { badgeId = "PG-1" })).EnsureSuccessStatusCode();
        var key = (await (await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/api-keys", new { name = "Door" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString();

        var terminal = _factory.CreateClient();
        terminal.DefaultRequestHeaders.Add("X-Api-Key", key);
        var punch = await terminal.PostAsJsonAsync("/api/v1/terminal/punch", new { badgeId = "PG-1" });
        punch.StatusCode.Should().Be(HttpStatusCode.OK);

        (await member.GetFromJsonAsync<JsonElement>("/api/v1/TimeTracking/current")).GetProperty("isRunning").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task FailureEntryJob_RunsAsSystem_AcrossOrganizations()
    {
        var (client, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-failure@pg.test");
        var org = await TestHelpers.CreateOrganizationAsync(client, "Failure Org", "pg-failure-org");
        (await client.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationId = org.Id })).EnsureSuccessStatusCode();

        // A scope outside any HTTP request is the trusted system (RLS bypass)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
        await FailureEntryService.CloseOverdueEntriesAsync(db, DateTime.UtcNow.AddDays(3));

        var failures = await client.GetFromJsonAsync<List<JsonElement>>("/api/v1/TimeTracking/failures");
        failures.Should().ContainSingle();
    }
}
