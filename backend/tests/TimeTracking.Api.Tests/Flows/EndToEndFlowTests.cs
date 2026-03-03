using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Flows;

/// <summary>
/// End-to-end integration tests that cover complete user journeys 
/// spanning multiple controllers and verifying cross-cutting behavior.
/// </summary>
public class EndToEndFlowTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public EndToEndFlowTests(TimeTrackingApiFactory factory) => _factory = factory;

    /// <summary>
    /// Complete user lifecycle: register → create org → configure settings → 
    /// track time → view history → change password → delete account.
    /// </summary>
    [Fact]
    public async Task UserLifecycle_RegisterToDeleteAccount()
    {
        // 1. Register
        var client = _factory.CreateClient();
        var login = await TestHelpers.RegisterAsync(
            client, "lifecycle@test.com", "MyPassword1!", "Life", "Cycle");
        TestHelpers.SetBearerToken(client, login.AccessToken);

        // 2. Create organization
        var org = await TestHelpers.CreateOrganizationAsync(client, "Lifecycle Org", "lifecycle-org");
        org.Slug.Should().Be("lifecycle-org");
        org.MemberCount.Should().Be(1);

        // 3. Configure settings
        var settingsResp = await client.PutAsJsonAsync("/api/Organizations/lifecycle-org/settings", new
        {
            autoPauseEnabled = true,
            editPastEntriesMode = 2,
            memberTimeEntryVisibility = true
        });
        settingsResp.EnsureSuccessStatusCode();

        // 4. Track time
        var startResp = await client.PostAsJsonAsync("/api/TimeTracking/start", new
        {
            description = "Working on project",
            organizationSlug = "lifecycle-org"
        });
        startResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var stopResp = await client.PostAsJsonAsync("/api/TimeTracking/stop", new
        {
            description = "Done for now"
        });
        stopResp.EnsureSuccessStatusCode();
        var entry = await stopResp.Content.ReadFromJsonAsync<TimeEntryResponseDto>(TestHelpers.JsonOptions);
        entry!.IsRunning.Should().BeFalse();
        entry.OrganizationName.Should().NotBeNullOrWhiteSpace();

        // 5. View history
        var historyResp = await client.GetAsync("/api/TimeTracking");
        historyResp.EnsureSuccessStatusCode();
        var entries = await historyResp.Content.ReadFromJsonAsync<List<TimeEntryResponseDto>>(TestHelpers.JsonOptions);
        entries!.Should().Contain(e => e.Description == "Done for now");

        // 6. Change password
        var pwResp = await client.PostAsJsonAsync("/api/Auth/change-password", new
        {
            currentPassword = "MyPassword1!",
            newPassword = "NewPassword2!"
        });
        pwResp.EnsureSuccessStatusCode();

        // 7. Delete account
        var deleteResp = await client.DeleteAsync("/api/Auth/account");
        deleteResp.EnsureSuccessStatusCode();

        // 8. Verify can't login
        TestHelpers.ClearAuth(client);
        var failedLogin = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            email = "lifecycle@test.com",
            password = "NewPassword2!"
        });
        failedLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Multi-user organization flow: Owner creates org → adds members → 
    /// members track time → owner views overview → admin creates schedules.
    /// </summary>
    [Fact]
    public async Task MultiUserOrganization_FullFlow()
    {
        // Setup: Owner creates org
        var ownerClient = _factory.CreateClient();
        var ownerLogin = await TestHelpers.RegisterAsync(
            ownerClient, "multiowner@test.com", "Password123!", "Multi", "Owner");
        TestHelpers.SetBearerToken(ownerClient, ownerLogin.AccessToken);

        await TestHelpers.CreateOrganizationAsync(ownerClient, "Multi User Org", "multi-user-org");

        // Register employee
        var empClient = _factory.CreateClient();
        var empLogin = await TestHelpers.RegisterAsync(
            empClient, "employee@test.com", "Password123!", "Emp", "Loyee");
        TestHelpers.SetBearerToken(empClient, empLogin.AccessToken);

        // Owner adds employee
        var addResp = await ownerClient.PostAsJsonAsync("/api/Organizations/multi-user-org/members", new
        {
            userId = empLogin.User.Id,
            role = 0
        });
        addResp.EnsureSuccessStatusCode();

        // Employee tracks time
        await empClient.PostAsJsonAsync("/api/TimeTracking/start", new
        {
            description = "Employee working",
            organizationSlug = "multi-user-org"
        });
        await empClient.PostAsJsonAsync("/api/TimeTracking/stop", (object?)null);

        // Enable member time entry visibility and work schedule changes
        await ownerClient.PutAsJsonAsync("/api/Organizations/multi-user-org/settings", new
        {
            memberTimeEntryVisibility = true,
            workScheduleChangeMode = 2
        });

        // Owner views time overview
        var overviewResp = await ownerClient.GetAsync("/api/Organizations/multi-user-org/time-overview");
        overviewResp.EnsureSuccessStatusCode();
        var overview = await overviewResp.Content.ReadFromJsonAsync<List<MemberTimeOverviewResponseDto>>(TestHelpers.JsonOptions);
        overview!.Should().HaveCountGreaterThanOrEqualTo(1);

        // Owner views employee entries
        var entriesResp = await ownerClient.GetAsync(
            $"/api/Organizations/multi-user-org/member-entries/{empLogin.User.Id}");
        entriesResp.EnsureSuccessStatusCode();
        var empEntries = await entriesResp.Content.ReadFromJsonAsync<List<TimeEntryResponseDto>>(TestHelpers.JsonOptions);
        empEntries!.Should().HaveCountGreaterThanOrEqualTo(1);

        // Owner creates work schedule for employee

        var scheduleResp = await ownerClient.PostAsJsonAsync(
            $"/api/organizations/multi-user-org/members/{empLogin.User.Id}/work-schedules", new
            {
                validFrom = "2026-01-01",
                weeklyWorkHours = 40.0,
                distributeEvenly = true
            });
        scheduleResp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// Join request flow with RequiresApproval: new user requests to join →
    /// owner sees notification → accepts → user is member → can track time.
    /// </summary>
    [Fact]
    public async Task JoinRequestFlow_RequiresApproval_CompleteJourney()
    {
        // Owner creates org with RequiresApproval
        var ownerClient = _factory.CreateClient();
        var ownerLogin = await TestHelpers.RegisterAsync(
            ownerClient, "joinflowowner@test.com", "Password123!", "JF", "Owner");
        TestHelpers.SetBearerToken(ownerClient, ownerLogin.AccessToken);

        await TestHelpers.CreateOrganizationAsync(ownerClient, "Join Flow Org", "join-flow-org");
        await ownerClient.PutAsJsonAsync("/api/Organizations/join-flow-org/settings", new { joinPolicy = 1 });

        // New user wants to join
        var userClient = _factory.CreateClient();
        var userLogin = await TestHelpers.RegisterAsync(
            userClient, "joinflowuser@test.com", "Password123!", "JF", "User");
        TestHelpers.SetBearerToken(userClient, userLogin.AccessToken);

        // User creates join request
        var reqResp = await userClient.PostAsJsonAsync("/api/organizations/join-flow-org/requests", new
        {
            type = 0,
            message = "I want to join!"
        });
        reqResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var request = await reqResp.Content.ReadFromJsonAsync<OrgRequestResponseDto>(TestHelpers.JsonOptions);

        // User checks their requests
        var myReqResp = await userClient.GetAsync("/api/organizations/my-requests");
        myReqResp.EnsureSuccessStatusCode();
        var myReqs = await myReqResp.Content.ReadFromJsonAsync<List<OrgRequestResponseDto>>(TestHelpers.JsonOptions);
        myReqs!.Should().Contain(r => r.Id == request!.Id);

        // Owner sees admin notifications
        var notifResp = await ownerClient.GetAsync("/api/organizations/notifications");
        var notifs = await notifResp.Content.ReadFromJsonAsync<AdminNotificationResponseDto>(TestHelpers.JsonOptions);
        notifs!.PendingRequests.Should().BeGreaterThanOrEqualTo(1);

        // Owner accepts
        var acceptResp = await ownerClient.PutAsJsonAsync(
            $"/api/organizations/join-flow-org/requests/{request!.Id}", new { accept = true });
        acceptResp.EnsureSuccessStatusCode();

        // User can now track time for the org
        var startResp = await userClient.PostAsJsonAsync("/api/TimeTracking/start", new
        {
            description = "First day!",
            organizationSlug = "join-flow-org"
        });
        startResp.StatusCode.Should().Be(HttpStatusCode.Created);

        await userClient.PostAsJsonAsync("/api/TimeTracking/stop", (object?)null);
    }

    /// <summary>
    /// Holiday and absence management: owner creates holidays → 
    /// member creates absence → owner sees absences → admin creates absence for member.
    /// </summary>
    [Fact]
    public async Task HolidayAndAbsenceFlow()
    {
        var ownerClient = _factory.CreateClient();
        var ownerLogin = await TestHelpers.RegisterAsync(
            ownerClient, "habowner@test.com", "Password123!", "HAB", "Owner");
        TestHelpers.SetBearerToken(ownerClient, ownerLogin.AccessToken);

        await TestHelpers.CreateOrganizationAsync(ownerClient, "HAB Org", "hab-org");

        // Add member
        var memClient = _factory.CreateClient();
        var memLogin = await TestHelpers.RegisterAsync(
            memClient, "habmember@test.com", "Password123!", "HAB", "Member");
        TestHelpers.SetBearerToken(memClient, memLogin.AccessToken);

        await ownerClient.PostAsJsonAsync("/api/Organizations/hab-org/members", new
        {
            userId = memLogin.User.Id,
            role = 0
        });

        // Owner adds holiday
        var holResp = await ownerClient.PostAsJsonAsync("/api/organizations/hab-org/holidays", new
        {
            date = "2026-12-25",
            name = "Christmas",
            isRecurring = true
        });
        holResp.EnsureSuccessStatusCode();

        // Import preset holidays
        var presetResp = await ownerClient.PostAsync(
            "/api/organizations/hab-org/holidays/import-preset?preset=de&year=2026", null);
        presetResp.EnsureSuccessStatusCode();

        // Member creates an absence
        var absResp = await memClient.PostAsJsonAsync("/api/organizations/hab-org/absences", new
        {
            date = "2026-12-30",
            type = 1, // Vacation
            note = "Year-end break"
        });
        absResp.EnsureSuccessStatusCode();

        // Owner views all absences
        var allAbsResp = await ownerClient.GetAsync("/api/organizations/hab-org/absences");
        allAbsResp.EnsureSuccessStatusCode();
        var absences = await allAbsResp.Content.ReadFromJsonAsync<List<AbsenceDayResponseDto>>(TestHelpers.JsonOptions);
        absences!.Should().Contain(a => a.Note == "Year-end break");

        // Owner creates absence for member
        var adminAbsResp = await ownerClient.PostAsJsonAsync("/api/organizations/hab-org/absences/admin", new
        {
            userId = memLogin.User.Id,
            date = "2026-12-31",
            type = 2, // Other
            note = "Company closure"
        });
        adminAbsResp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// RBAC enforcement: member can't do admin things, admin can't do owner things.
    /// </summary>
    [Fact]
    public async Task RoleBasedAccessControl_EnforcesPermissions()
    {
        var ownerClient = _factory.CreateClient();
        var ownerLogin = await TestHelpers.RegisterAsync(
            ownerClient, "rbacowner@test.com", "Password123!", "RBAC", "Owner");
        TestHelpers.SetBearerToken(ownerClient, ownerLogin.AccessToken);

        await TestHelpers.CreateOrganizationAsync(ownerClient, "RBAC Org", "rbac-org");

        // Add admin
        var adminClient = _factory.CreateClient();
        var adminLogin = await TestHelpers.RegisterAsync(
            adminClient, "rbacadmin@test.com", "Password123!", "RBAC", "Admin");
        TestHelpers.SetBearerToken(adminClient, adminLogin.AccessToken);
        await ownerClient.PostAsJsonAsync("/api/Organizations/rbac-org/members", new
        {
            userId = adminLogin.User.Id,
            role = 1 // Admin
        });

        // Add member
        var memberClient = _factory.CreateClient();
        var memberLogin = await TestHelpers.RegisterAsync(
            memberClient, "rbacmember@test.com", "Password123!", "RBAC", "Member");
        TestHelpers.SetBearerToken(memberClient, memberLogin.AccessToken);
        await ownerClient.PostAsJsonAsync("/api/Organizations/rbac-org/members", new
        {
            userId = memberLogin.User.Id,
            role = 0 // Member
        });

        // Member can't update org
        var updateResp = await memberClient.PutAsJsonAsync("/api/Organizations/rbac-org", new
        {
            name = "Hacked!"
        });
        updateResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Member can't update settings
        var settingsResp = await memberClient.PutAsJsonAsync("/api/Organizations/rbac-org/settings", new
        {
            autoPauseEnabled = false
        });
        settingsResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Member can't view time overview
        var overviewResp = await memberClient.GetAsync("/api/Organizations/rbac-org/time-overview");
        overviewResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Member can't create holidays
        var holResp = await memberClient.PostAsJsonAsync("/api/organizations/rbac-org/holidays", new
        {
            date = "2026-01-01",
            name = "Fake Holiday",
            isRecurring = false
        });
        holResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Member can't create admin absences
        var absResp = await memberClient.PostAsJsonAsync("/api/organizations/rbac-org/absences/admin", new
        {
            userId = memberLogin.User.Id,
            date = "2026-01-01",
            type = 0
        });
        absResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Member can't view requests
        var reqResp = await memberClient.GetAsync("/api/organizations/rbac-org/requests");
        reqResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Member CAN create their own absence
        var ownAbsResp = await memberClient.PostAsJsonAsync("/api/organizations/rbac-org/absences", new
        {
            date = "2026-05-01",
            type = 0
        });
        ownAbsResp.EnsureSuccessStatusCode();

        // Member CAN track time
        var startResp = await memberClient.PostAsJsonAsync("/api/TimeTracking/start", new
        {
            organizationSlug = "rbac-org"
        });
        startResp.StatusCode.Should().Be(HttpStatusCode.Created);
        await memberClient.PostAsJsonAsync("/api/TimeTracking/stop", (object?)null);
    }

    /// <summary>
    /// Token refresh chain: login → refresh → use new token → refresh again.
    /// </summary>
    [Fact]
    public async Task TokenRefreshChain_MultipleCycles()
    {
        var client = _factory.CreateClient();
        var login = await TestHelpers.RegisterAsync(
            client, "tokenchain@test.com", "Password123!", "Token", "Chain");

        // First refresh
        var refresh1 = await client.PostAsJsonAsync("/api/Auth/refresh", new
        {
            refreshToken = login.RefreshToken
        });
        refresh1.EnsureSuccessStatusCode();
        var tokens1 = await refresh1.Content.ReadFromJsonAsync<RefreshTokenResponseDto>(TestHelpers.JsonOptions);

        // Use new access token
        TestHelpers.SetBearerToken(client, tokens1!.AccessToken);
        var meResp = await client.GetAsync("/api/Auth/me");
        meResp.EnsureSuccessStatusCode();

        // Second refresh with new refresh token
        var refresh2 = await client.PostAsJsonAsync("/api/Auth/refresh", new
        {
            refreshToken = tokens1.RefreshToken
        });
        refresh2.EnsureSuccessStatusCode();
        var tokens2 = await refresh2.Content.ReadFromJsonAsync<RefreshTokenResponseDto>(TestHelpers.JsonOptions);
        tokens2!.AccessToken.Should().NotBe(tokens1.AccessToken);
        tokens2.RefreshToken.Should().NotBe(tokens1.RefreshToken);
    }
}
