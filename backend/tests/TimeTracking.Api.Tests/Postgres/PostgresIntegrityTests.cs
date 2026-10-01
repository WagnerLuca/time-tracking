using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// Regression tests for bugs that only surface on real Postgres (EF InMemory ignores
/// unique indexes and DateTime kinds): each of these returned 500 or corrupted data before.
/// </summary>
[Collection(PostgresCollection.Name)]
public class PostgresIntegrityTests
{
    private readonly PostgresApiFactory _factory;

    public PostgresIntegrityTests(PostgresApiFactory factory) => _factory = factory;

    [Fact]
    public async Task DateTimesWithoutTimeZone_AreAcceptedAsUtc()
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-utc@pg.test");
        (await client.PostAsJsonAsync("/api/v1/TimeTracking/start", new { description = "utc" })).EnsureSuccessStatusCode();
        var stopped = await (await client.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null)).Content.ReadFromJsonAsync<JsonElement>();
        var id = stopped.GetProperty("id").GetInt32();

        // No "Z": Kind=Unspecified, which Npgsql refuses for timestamptz unless normalized
        var update = await client.PutAsync($"/api/v1/TimeTracking/{id}",
            new StringContent("{\"startTime\":\"2026-03-03T08:00:00\",\"endTime\":\"2026-03-03T12:00:00\"}", Encoding.UTF8, "application/json"));
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        (await update.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("durationMinutes").GetDouble().Should().Be(240);

        // Date-only query parameters are Unspecified as well
        var history = await client.GetAsync("/api/v1/TimeTracking?from=2026-03-01&to=2026-03-31");
        history.StatusCode.Should().Be(HttpStatusCode.OK);
        (await history.Content.ReadAsStringAsync()).Should().Contain("utc");
    }

    [Fact]
    public async Task ConcurrentStarts_LeaveExactlyOneRunningTimer()
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-race@pg.test");

        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => client.PostAsJsonAsync("/api/v1/TimeTracking/start", new { description = "race" })));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.Conflict);
        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/TimeTracking?limit=50");
        page.GetProperty("items").EnumerateArray().Count(e => e.GetProperty("isRunning").GetBoolean()).Should().Be(1);
    }

    [Fact]
    public async Task MemberWhoLeft_CanJoinAgain()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-rejoin-owner@pg.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Rejoin Org", "pg-rejoin-org");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { joinPolicy = 2 /* Allowed */ })).EnsureSuccessStatusCode();

        var (member, m) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-rejoin-m@pg.test");
        (await member.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/requests", new { type = 0 })).EnsureSuccessStatusCode();
        (await owner.DeleteAsync($"/api/v1/Organizations/{org.Slug}/members/{m.User.Id}")).EnsureSuccessStatusCode();

        // The soft-deleted membership row still holds the unique (UserId, OrganizationId) index
        var again = await member.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/requests", new { type = 0 });
        again.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        again.IsSuccessStatusCode.Should().BeTrue();
        (await member.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{org.Slug}")).GetProperty("slug").GetString().Should().Be(org.Slug);
    }

    [Fact]
    public async Task SlugOfDeletedOrganization_CanBeReused()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-slug@pg.test");
        await TestHelpers.CreateOrganizationAsync(owner, "First", "pg-reused-slug");
        (await owner.DeleteAsync("/api/v1/Organizations/pg-reused-slug")).EnsureSuccessStatusCode();

        var second = await owner.PostAsJsonAsync("/api/v1/Organizations", new { name = "Second", slug = "pg-reused-slug" });
        second.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Email_IsUniqueRegardlessOfCase()
    {
        var client = _factory.CreateClient();
        await TestHelpers.RegisterAsync(client, "Case@PG.test", "StrongPass1!", "A", "B");
        var dup = await client.PostAsJsonAsync("/api/v1/Auth/register",
            new { email = "case@pg.TEST", password = "StrongPass1!", firstName = "C", lastName = "D" });
        dup.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConcurrentApprovals_ApplyOnlyOnce()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-approve-owner@pg.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Approve Org", "pg-approve-org");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { csvImportMode = 1 /* RequiresApproval */ })).EnsureSuccessStatusCode();
        var (member, m) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-approve-m@pg.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, m)).EnsureSuccessStatusCode();

        var data = JsonSerializer.Serialize(new
        {
            entries = new[] { new { date = "2026-03-02", startTime = "08:00", endTime = "12:00", pauseMinutes = 0, description = "once" } }
        });
        var id = (await (await member.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/requests", new { type = 5, requestData = data }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // Status is a concurrency token: only one of several simultaneous approvals may apply the import
        var results = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => owner.PutAsJsonAsync($"/api/v1/organizations/{org.Slug}/requests/{id}", new { accept = true })));
        results.Count(r => r.IsSuccessStatusCode).Should().Be(1);
        results.Should().OnlyContain(r => r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.Conflict || r.StatusCode == HttpStatusCode.BadRequest);

        var history = await member.GetFromJsonAsync<JsonElement>("/api/v1/TimeTracking?limit=50");
        history.GetProperty("items").EnumerateArray().Count(e => e.GetProperty("description").GetString() == "once").Should().Be(1);
    }
}
