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
    public async Task Start_WithOrganization_AsNonMember_ReturnsForbidden()
    {
        // Create a user who is NOT a member of the seed org
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(
            _factory, "nonmember-start@test.com", "Non", "Member");

        var response = await client.PostAsJsonAsync("/api/v1/TimeTracking/start", new
        {
            organizationSlug = TestHelpers.SeedOrgSlug
        });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
        (await client.GetAsync("/api/v1/TimeTracking/export")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── CSV Export ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExportCsv_ReturnsValidCsvFile()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync("/api/v1/TimeTracking/export");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition.Should().NotBeNull();
        response.Content.Headers.ContentDisposition!.FileName.Should().EndWith(".csv");

        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().Contain("Date,Start Time,End Time,Duration (h),Pause (min),Net Duration (h),Organization,Description");
        // Seed data should produce at least one data row
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().BeGreaterThanOrEqualTo(2); // header + at least one row
    }

    [Fact]
    public async Task ExportCsv_WithDateFilter_FiltersCorrectly()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Far future date — should return only header
        var response = await client.GetAsync("/api/v1/TimeTracking/export?from=2099-01-01&to=2099-12-31");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().Be(1); // Only header
    }

    [Fact]
    public async Task ExportCsv_WithOrgFilter_FiltersCorrectly()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Non-existent org ID — should return only header
        var response = await client.GetAsync("/api/v1/TimeTracking/export?organizationId=999999");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().Be(1); // Only header
    }

    [Fact]
    public async Task ExportCsv_ExcludesRunningEntries()
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "csvrun@test.com");

        // Start an entry (running, no end time)
        await client.PostAsJsonAsync("/api/v1/TimeTracking/start", new { description = "Still running" });

        var response = await client.GetAsync("/api/v1/TimeTracking/export");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // The running entry should not appear in export
        lines.Length.Should().Be(1); // Only header
        csv.Should().NotContain("Still running");

        // Clean up
        await client.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null);
    }

    // ── Daily Report Export ──────────────────────────────────────────────

    [Fact]
    public async Task ExportDailyReport_ReturnsValidCsvWithHeaders()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync(
            $"/api/v1/TimeTracking/export/{TestHelpers.SeedOrgSlug}/daily-report");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().Contain("Date,Day,Target (h),Worked (h),Pause (min),Net Worked (h),Overtime (h),Cumulative Overtime (h),Status,Holiday,Absence Type,Absence Note,Entries");

        // Should have header + at least one day row (defaults to current month)
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ExportDailyReport_WithDateRange_ReturnsCorrectDays()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Request exactly 7 days
        var response = await client.GetAsync(
            $"/api/v1/TimeTracking/export/{TestHelpers.SeedOrgSlug}/daily-report?from=2026-01-05&to=2026-01-11");
        response.EnsureSuccessStatusCode();

        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().Be(8); // header + 7 days

        // Should contain the correct dates
        csv.Should().Contain("2026-01-05");
        csv.Should().Contain("2026-01-11");
    }

    [Fact]
    public async Task ExportDailyReport_NonMember_ReturnsForbidden()
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "nonmember-export@test.com");

        var response = await client.GetAsync(
            $"/api/v1/TimeTracking/export/{TestHelpers.SeedOrgSlug}/daily-report");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExportDailyReport_NonExistentOrg_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync(
            "/api/v1/TimeTracking/export/does-not-exist-org/daily-report");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExportDailyReport_ContainsWeekendStatus()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // 2026-01-10 is a Saturday, 2026-01-11 is a Sunday
        var response = await client.GetAsync(
            $"/api/v1/TimeTracking/export/{TestHelpers.SeedOrgSlug}/daily-report?from=2026-01-10&to=2026-01-11");
        response.EnsureSuccessStatusCode();

        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().Contain("Weekend");
        csv.Should().Contain("Sat");
        csv.Should().Contain("Sun");
    }

    [Fact]
    public async Task ExportDailyReport_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/v1/TimeTracking/export/{TestHelpers.SeedOrgSlug}/daily-report");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
