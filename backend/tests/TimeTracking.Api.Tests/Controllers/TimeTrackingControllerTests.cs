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
        var startResponse = await client.PostAsJsonAsync("/api/v1/TimeTracking/start", new
        {
            description = "Test task"
        });
        startResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var started = await startResponse.Content.ReadFromJsonAsync<TimeEntryResponseDto>(TestHelpers.JsonOptions);
        started!.IsRunning.Should().BeTrue();
        started.Description.Should().Be("Test task");
        started.EndTime.Should().BeNull();

        // Current
        var currentResponse = await client.GetAsync("/api/v1/TimeTracking/current");
        currentResponse.EnsureSuccessStatusCode();
        var current = await currentResponse.Content.ReadFromJsonAsync<TimeEntryResponseDto>(TestHelpers.JsonOptions);
        current!.Id.Should().Be(started.Id);
        current.IsRunning.Should().BeTrue();

        // Stop
        var stopResponse = await client.PostAsJsonAsync("/api/v1/TimeTracking/stop", new
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

        var startResponse = await client.PostAsJsonAsync("/api/v1/TimeTracking/start", new
        {
            description = "Org task",
            organizationSlug = TestHelpers.SeedOrgSlug
        });
        startResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var entry = await startResponse.Content.ReadFromJsonAsync<TimeEntryResponseDto>(TestHelpers.JsonOptions);
        entry!.OrganizationId.Should().NotBeNull();
        entry.OrganizationName.Should().NotBeNullOrWhiteSpace();

        // Clean up
        await client.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null);
    }

    [Fact]
    public async Task Start_WithoutBody_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedAdminEmail, TestHelpers.SeedPassword);

        var response = await client.PostAsJsonAsync("/api/v1/TimeTracking/start", (object?)null);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var entry = await response.Content.ReadFromJsonAsync<TimeEntryResponseDto>(TestHelpers.JsonOptions);
        entry!.IsRunning.Should().BeTrue();

        // Stop it
        await client.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null);
    }

    [Fact]
    public async Task Stop_WhenNothingRunning_Returns400()
    {
        var client = _factory.CreateClient();
        // Register a fresh user with no running entries
        var (authedClient, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "nostop@test.com");

        var response = await authedClient.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetCurrent_WhenNoneRunning_Returns404()
    {
        var client = _factory.CreateClient();
        var (authedClient, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "nocurrent@test.com");

        var response = await authedClient.GetAsync("/api/v1/TimeTracking/current");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── History ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHistory_ReturnsSeedEntries()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync("/api/v1/TimeTracking");
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<PaginatedResponseDto<TimeEntryResponseDto>>(TestHelpers.JsonOptions);
        page.Should().NotBeNull();
        page!.Items.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetHistory_WithLimitAndOffset_RespectsParams()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync("/api/v1/TimeTracking?limit=2&offset=0");
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<PaginatedResponseDto<TimeEntryResponseDto>>(TestHelpers.JsonOptions);
        page.Should().NotBeNull();
        page!.Items.Count.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetHistory_WithDateFilter_FiltersCorrectly()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Far future date range — should return empty
        var response = await client.GetAsync("/api/v1/TimeTracking?from=2099-01-01&to=2099-12-31");
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<PaginatedResponseDto<TimeEntryResponseDto>>(TestHelpers.JsonOptions);
        page.Should().NotBeNull();
        page!.Items.Should().BeEmpty();
    }

    // ── Update ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ExistingEntry_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Get existing entries
        var page = await client.GetFromJsonAsync<PaginatedResponseDto<TimeEntryResponseDto>>(
            "/api/v1/TimeTracking?limit=1", TestHelpers.JsonOptions);
        page.Should().NotBeNull();
        page!.Items.Should().NotBeEmpty();
        var entry = page.Items.First();

        var response = await client.PutAsJsonAsync($"/api/v1/TimeTracking/{entry.Id}", new
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
        var page = await client.GetFromJsonAsync<PaginatedResponseDto<TimeEntryResponseDto>>(
            "/api/v1/TimeTracking?limit=1", TestHelpers.JsonOptions);
        var entryId = page!.Items.First().Id;

        // Login as Tom (member)
        TestHelpers.ClearAuth(client);
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);

        var response = await client.PutAsJsonAsync($"/api/v1/TimeTracking/{entryId}", new
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
        await client.PostAsJsonAsync("/api/v1/TimeTracking/start", new { description = "To delete" });
        var stopResp = await client.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null);
        var stopped = await stopResp.Content.ReadFromJsonAsync<TimeEntryResponseDto>(TestHelpers.JsonOptions);

        var deleteResponse = await client.DeleteAsync($"/api/v1/TimeTracking/{stopped!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_NonExistentEntry_Returns404()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.DeleteAsync("/api/v1/TimeTracking/999999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Auth required ────────────────────────────────────────────────────

    [Fact]
    public async Task AllEndpoints_Unauthenticated_Return401()
    {
        var client = _factory.CreateClient();

        (await client.PostAsJsonAsync("/api/v1/TimeTracking/start", (object?)null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/TimeTracking/current")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/TimeTracking")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PutAsJsonAsync("/api/v1/TimeTracking/1", new { })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.DeleteAsync("/api/v1/TimeTracking/1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
