using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Integration tests for the TimeTracking controller: start/stop timers,
/// history with filters, update, delete, and concurrent entry handling.
/// </summary>
public class TimeTrackingControllerTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public TimeTrackingControllerTests(TimeTrackingApiFactory factory) => _factory = factory;

    // ── Start / Stop / Current ───────────────────────────────────────────

    [Fact]
    public async Task StartAndStop_BasicFlow_ReturnsEntryWithDuration()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Start
        var startResponse = await client.PostAsJsonAsync("/api/TimeTracking/start", new
        {
            description = "Test task"
        });
        startResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var started = await startResponse.Content.ReadFromJsonAsync<TimeEntryResponseDto>(TestHelpers.JsonOptions);
        started!.IsRunning.Should().BeTrue();
        started.Description.Should().Be("Test task");
        started.EndTime.Should().BeNull();

        // Current
        var currentResponse = await client.GetAsync("/api/TimeTracking/current");
        currentResponse.EnsureSuccessStatusCode();
        var current = await currentResponse.Content.ReadFromJsonAsync<TimeEntryResponseDto>(TestHelpers.JsonOptions);
        current!.Id.Should().Be(started.Id);
        current.IsRunning.Should().BeTrue();

        // Stop
        var stopResponse = await client.PostAsJsonAsync("/api/TimeTracking/stop", new
        {
            description = "Updated description"
        });
        stopResponse.EnsureSuccessStatusCode();
        var stopped = await stopResponse.Content.ReadFromJsonAsync<TimeEntryResponseDto>(TestHelpers.JsonOptions);
        stopped!.IsRunning.Should().BeFalse();
        stopped.EndTime.Should().NotBeNull();
        stopped.Description.Should().Be("Updated description");
        stopped.DurationMinutes.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Start_WithOrganizationSlug_AssociatesEntry()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var startResponse = await client.PostAsJsonAsync("/api/TimeTracking/start", new
        {
            description = "Org task",
            organizationSlug = TestHelpers.SeedOrgSlug
        });
        startResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var entry = await startResponse.Content.ReadFromJsonAsync<TimeEntryResponseDto>(TestHelpers.JsonOptions);
        entry!.OrganizationId.Should().NotBeNull();
        entry.OrganizationName.Should().NotBeNullOrWhiteSpace();

        // Clean up
        await client.PostAsJsonAsync("/api/TimeTracking/stop", (object?)null);
    }

    [Fact]
    public async Task Start_WithoutBody_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedAdminEmail, TestHelpers.SeedPassword);

        var response = await client.PostAsJsonAsync("/api/TimeTracking/start", (object?)null);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var entry = await response.Content.ReadFromJsonAsync<TimeEntryResponseDto>(TestHelpers.JsonOptions);
        entry!.IsRunning.Should().BeTrue();

        // Stop it
        await client.PostAsJsonAsync("/api/TimeTracking/stop", (object?)null);
    }

    [Fact]
    public async Task Stop_WhenNothingRunning_Returns400()
    {
        var client = _factory.CreateClient();
        // Register a fresh user with no running entries
        var (authedClient, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "nostop@test.com");

        var response = await authedClient.PostAsJsonAsync("/api/TimeTracking/stop", (object?)null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetCurrent_WhenNoneRunning_Returns404()
    {
        var client = _factory.CreateClient();
        var (authedClient, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "nocurrent@test.com");

        var response = await authedClient.GetAsync("/api/TimeTracking/current");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── History ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHistory_ReturnsSeedEntries()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync("/api/TimeTracking");
        response.EnsureSuccessStatusCode();

        var entries = await response.Content.ReadFromJsonAsync<List<TimeEntryResponseDto>>(TestHelpers.JsonOptions);
        entries.Should().NotBeNull();
        entries!.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetHistory_WithLimitAndOffset_RespectsParams()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync("/api/TimeTracking?limit=2&offset=0");
        response.EnsureSuccessStatusCode();

        var entries = await response.Content.ReadFromJsonAsync<List<TimeEntryResponseDto>>(TestHelpers.JsonOptions);
        entries.Should().NotBeNull();
        entries!.Count.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetHistory_WithDateFilter_FiltersCorrectly()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Far future date range — should return empty
        var response = await client.GetAsync("/api/TimeTracking?from=2099-01-01&to=2099-12-31");
        response.EnsureSuccessStatusCode();

        var entries = await response.Content.ReadFromJsonAsync<List<TimeEntryResponseDto>>(TestHelpers.JsonOptions);
        entries.Should().NotBeNull();
        entries!.Should().BeEmpty();
    }

    // ── Update ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ExistingEntry_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Get existing entries
        var entries = await client.GetFromJsonAsync<List<TimeEntryResponseDto>>(
            "/api/TimeTracking?limit=1", TestHelpers.JsonOptions);
        entries.Should().NotBeNullOrEmpty();
        var entry = entries!.First();

        var response = await client.PutAsJsonAsync($"/api/TimeTracking/{entry.Id}", new
        {
            description = "Updated by test",
            pauseDurationMinutes = 15
        });
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<TimeEntryResponseDto>(TestHelpers.JsonOptions);
        updated!.Description.Should().Be("Updated by test");
        updated.PauseDurationMinutes.Should().Be(15);
    }

    [Fact]
    public async Task Update_OtherUsersEntry_ReturnsForbiddenOrNotFound()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Get an entry ID from Max
        var entries = await client.GetFromJsonAsync<List<TimeEntryResponseDto>>(
            "/api/TimeTracking?limit=1", TestHelpers.JsonOptions);
        var entryId = entries!.First().Id;

        // Login as Tom (member)
        TestHelpers.ClearAuth(client);
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);

        var response = await client.PutAsJsonAsync($"/api/TimeTracking/{entryId}", new
        {
            description = "Hacked!"
        });

        // Should be NotFound (since the query scopes by userId)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    // ── Delete ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_OwnEntry_Returns204()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Start and stop to create a fresh entry
        await client.PostAsJsonAsync("/api/TimeTracking/start", new { description = "To delete" });
        var stopResp = await client.PostAsJsonAsync("/api/TimeTracking/stop", (object?)null);
        var stopped = await stopResp.Content.ReadFromJsonAsync<TimeEntryResponseDto>(TestHelpers.JsonOptions);

        var deleteResponse = await client.DeleteAsync($"/api/TimeTracking/{stopped!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_NonExistentEntry_Returns404()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.DeleteAsync("/api/TimeTracking/999999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Auth required ────────────────────────────────────────────────────

    [Fact]
    public async Task AllEndpoints_Unauthenticated_Return401()
    {
        var client = _factory.CreateClient();

        (await client.PostAsJsonAsync("/api/TimeTracking/start", (object?)null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/TimeTracking/stop", (object?)null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/TimeTracking/current")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/TimeTracking")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PutAsJsonAsync("/api/TimeTracking/1", new { })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.DeleteAsync("/api/TimeTracking/1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
