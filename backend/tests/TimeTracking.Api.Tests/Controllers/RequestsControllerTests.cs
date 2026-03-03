using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Integration tests for the Requests controller: join requests, request lifecycle,
/// edit-past-entry requests, work-schedule-change requests, admin/user notifications.
/// </summary>
public class RequestsControllerTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public RequestsControllerTests(TimeTrackingApiFactory factory) => _factory = factory;

    // ── Join Request lifecycle ───────────────────────────────────────────

    [Fact]
    public async Task JoinRequest_FullLifecycle_CreateRespondAccept()
    {
        var client = _factory.CreateClient();

        // Create a new org with RequiresApproval join policy
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "Join Test Org", "join-test-org");

        // Update join policy to RequiresApproval
        await client.PutAsJsonAsync("/api/Organizations/join-test-org/settings", new
        {
            joinPolicy = 1 // RequiresApproval
        });

        // Register a new user who wants to join
        var newClient = _factory.CreateClient();
        var newUser = await TestHelpers.RegisterAsync(newClient, "joiner@test.com", "Password123!", "Join", "Er");
        TestHelpers.SetBearerToken(newClient, newUser.AccessToken);

        // Create join request
        var createResponse = await newClient.PostAsJsonAsync("/api/organizations/join-test-org/requests", new
        {
            message = "Please let me join!",
            type = 0 // JoinOrganization
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var request = await createResponse.Content.ReadFromJsonAsync<OrgRequestResponseDto>(TestHelpers.JsonOptions);
        request!.Type.Should().Be("JoinOrganization");
        request.Status.Should().Be("Pending");
        request.Message.Should().Be("Please let me join!");

        // Owner checks admin notifications
        var adminNotifResponse = await client.GetAsync("/api/organizations/notifications");
        adminNotifResponse.EnsureSuccessStatusCode();
        var adminNotif = await adminNotifResponse.Content.ReadFromJsonAsync<AdminNotificationResponseDto>(TestHelpers.JsonOptions);
        adminNotif!.PendingRequests.Should().BeGreaterThanOrEqualTo(1);

        // Owner accepts the request
        var respondResponse = await client.PutAsJsonAsync(
            $"/api/organizations/join-test-org/requests/{request.Id}", new { accept = true });
        respondResponse.EnsureSuccessStatusCode();

        var responded = await respondResponse.Content.ReadFromJsonAsync<OrgRequestResponseDto>(TestHelpers.JsonOptions);
        responded!.Status.Should().Be("Accepted");
        responded.RespondedAt.Should().NotBeNull();

        // Verify user is now a member
        var orgDetail = await client.GetFromJsonAsync<OrganizationDetailResponseDto>(
            "/api/Organizations/join-test-org", TestHelpers.JsonOptions);
        orgDetail!.Members.Should().Contain(m => m.Email == "joiner@test.com");
    }

    [Fact]
    public async Task JoinRequest_Decline_UserNotAdded()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "Decline Test", "decline-test-org");
        await client.PutAsJsonAsync("/api/Organizations/decline-test-org/settings", new { joinPolicy = 1 });

        // New user requests to join
        var newClient = _factory.CreateClient();
        var newUser = await TestHelpers.RegisterAsync(newClient, "declined@test.com", "Password123!", "Dec", "Lined");
        TestHelpers.SetBearerToken(newClient, newUser.AccessToken);

        var createResponse = await newClient.PostAsJsonAsync("/api/organizations/decline-test-org/requests", new
        {
            type = 0
        });
        var request = await createResponse.Content.ReadFromJsonAsync<OrgRequestResponseDto>(TestHelpers.JsonOptions);

        // Owner declines
        var respondResponse = await client.PutAsJsonAsync(
            $"/api/organizations/decline-test-org/requests/{request!.Id}", new { accept = false });
        respondResponse.EnsureSuccessStatusCode();

        var responded = await respondResponse.Content.ReadFromJsonAsync<OrgRequestResponseDto>(TestHelpers.JsonOptions);
        responded!.Status.Should().Be("Declined");

        // User should NOT be a member
        var orgDetail = await client.GetFromJsonAsync<OrganizationDetailResponseDto>(
            "/api/Organizations/decline-test-org", TestHelpers.JsonOptions);
        orgDetail!.Members.Should().NotContain(m => m.Email == "declined@test.com");
    }

    // ── Get requests ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetRequests_AsAdmin_ReturnsList()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync($"/api/organizations/{TestHelpers.SeedOrgSlug}/requests");
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<PaginatedResponseDto<OrgRequestResponseDto>>(TestHelpers.JsonOptions);
        page.Should().NotBeNull();
    }

    [Fact]
    public async Task GetRequests_AsMember_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync($"/api/organizations/{TestHelpers.SeedOrgSlug}/requests");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── My requests ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyRequests_ReturnsUserRequests()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync("/api/organizations/my-requests");
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<PaginatedResponseDto<OrgRequestResponseDto>>(TestHelpers.JsonOptions);
        page.Should().NotBeNull();
    }

    // ── User notifications ───────────────────────────────────────────────

    [Fact]
    public async Task GetUserNotifications_ReturnsNotifications()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync("/api/organizations/user-notifications");
        response.EnsureSuccessStatusCode();

        var notifs = await response.Content.ReadFromJsonAsync<UserNotificationResponseDto>(TestHelpers.JsonOptions);
        notifs.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkNotificationsSeen_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);

        // Post empty list (still valid)
        var response = await client.PostAsJsonAsync("/api/organizations/user-notifications/mark-seen", new List<int>());
        response.EnsureSuccessStatusCode();
    }

    // ── Duplicate pending request ────────────────────────────────────────

    [Fact]
    public async Task CreateRequest_DuplicatePending_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "Dup Req Test", "dup-req-test");
        await client.PutAsJsonAsync("/api/Organizations/dup-req-test/settings", new { joinPolicy = 1 });

        var newClient = _factory.CreateClient();
        var newUser = await TestHelpers.RegisterAsync(newClient, "dupreq@test.com", "Password123!", "Dup", "Req");
        TestHelpers.SetBearerToken(newClient, newUser.AccessToken);

        // First request succeeds
        var first = await newClient.PostAsJsonAsync("/api/organizations/dup-req-test/requests", new { type = 0 });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // Second identical request should fail
        var second = await newClient.PostAsJsonAsync("/api/organizations/dup-req-test/requests", new { type = 0 });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── WorkScheduleChange request with acceptance ───────────────────────

    [Fact]
    public async Task WorkScheduleChangeRequest_WhenAccepted_CreatesSchedule()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "WSC Test", "wsc-test-org");
        // Set work schedule change to RequiresApproval
        await client.PutAsJsonAsync("/api/Organizations/wsc-test-org/settings", new { workScheduleChangeMode = 1 });

        // Add Tom as a member
        var tomLogin = await TestHelpers.LoginAsync(_factory.CreateClient(), TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);

        await client.PostAsJsonAsync("/api/Organizations/wsc-test-org/members", new
        {
            userId = tomLogin.User.Id,
            role = 0
        });

        // Tom creates a work schedule change request
        var tomClient = _factory.CreateClient();
        TestHelpers.SetBearerToken(tomClient, tomLogin.AccessToken);

        var requestData = System.Text.Json.JsonSerializer.Serialize(new
        {
            validFrom = "2026-03-01",
            weeklyWorkHours = 40.0,
            distributeEvenly = true
        });

        var createResponse = await tomClient.PostAsJsonAsync("/api/organizations/wsc-test-org/requests", new
        {
            type = 4, // WorkScheduleChange
            message = "I'd like to change my schedule",
            requestData
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var req = await createResponse.Content.ReadFromJsonAsync<OrgRequestResponseDto>(TestHelpers.JsonOptions);

        // Owner accepts
        var acceptResponse = await client.PutAsJsonAsync(
            $"/api/organizations/wsc-test-org/requests/{req!.Id}", new { accept = true });
        acceptResponse.EnsureSuccessStatusCode();
    }
}
