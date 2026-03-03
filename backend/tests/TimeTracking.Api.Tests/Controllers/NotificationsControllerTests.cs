using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Integration tests for the Notifications controller: list, unread count,
/// mark as read, mark all as read.
/// </summary>
public class NotificationsControllerTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public NotificationsControllerTests(TimeTrackingApiFactory factory) => _factory = factory;

    // ── Get notifications ────────────────────────────────────────────────

    [Fact]
    public async Task GetNotifications_Authenticated_ReturnsOk()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync("/api/Notifications");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetNotifications_UnreadOnly_ReturnsOk()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync("/api/Notifications?unreadOnly=true");
        response.EnsureSuccessStatusCode();
    }

    // ── Unread count ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetUnreadCount_ReturnsNumber()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync("/api/Notifications/unread-count");
        response.EnsureSuccessStatusCode();
    }

    // ── Mark as read ─────────────────────────────────────────────────────

    [Fact]
    public async Task MarkAsRead_NonExistentId_Returns404()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.PutAsync("/api/Notifications/999999/read", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Mark all as read ─────────────────────────────────────────────────

    [Fact]
    public async Task MarkAllAsRead_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.PutAsync("/api/Notifications/read-all", null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Now unread count should be 0
        var countResponse = await client.GetAsync("/api/Notifications/unread-count");
        countResponse.EnsureSuccessStatusCode();
    }

    // ── Settings change generates notification ───────────────────────────

    [Fact]
    public async Task SettingsChange_GeneratesNotificationsForMembers()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Get Tom's unread count before
        var tomClient = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(tomClient, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);

        // Mark all read first
        await tomClient.PutAsync("/api/Notifications/read-all", null);

        var beforeCount = await tomClient.GetFromJsonAsync<object>("/api/Notifications/unread-count");

        // Owner changes settings
        await client.PutAsJsonAsync($"/api/Organizations/{TestHelpers.SeedOrgSlug}/settings", new
        {
            editPastEntriesMode = 0 // Disabled
        });

        // Tom should have a new notification
        var afterResponse = await tomClient.GetAsync("/api/Notifications?unreadOnly=true");
        afterResponse.EnsureSuccessStatusCode();
    }

    // ── Auth required ────────────────────────────────────────────────────

    [Fact]
    public async Task AllEndpoints_Unauthenticated_Return401()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync("/api/Notifications")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/Notifications/unread-count")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PutAsync("/api/Notifications/1/read", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PutAsync("/api/Notifications/read-all", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
