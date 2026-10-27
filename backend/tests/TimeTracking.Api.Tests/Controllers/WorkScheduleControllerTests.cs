using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Integration tests for WorkSchedule controller: self-service change / cancel (issue #82),
/// an admin's change for a member, initial overtime, and mode enforcement.
/// </summary>
public class WorkScheduleControllerTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public WorkScheduleControllerTests(TimeTrackingApiFactory factory) => _factory = factory;

    // ── Self-service: Get schedules ──────────────────────────────────────

    [Fact]
    public async Task GetMyWorkSchedules_ReturnsList()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync($"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/work-schedules");
        response.EnsureSuccessStatusCode();

        var schedules = await response.Content.ReadFromJsonAsync<List<WorkScheduleResponseDto>>(TestHelpers.JsonOptions);
        schedules.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMyWorkSchedule_ReturnsCurrentSchedule()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync($"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/work-schedule");
        // May return 200 or 404 depending on seed data
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    // ── Self-service: Change ─────────────────────────────────────────────

    [Fact]
    public async Task ChangeMyWorkSchedule_WhenAllowed_ReturnsThePlan()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Create a new org where we control settings
        await TestHelpers.CreateOrganizationAsync(client, "WS Create Test", "ws-create-test");

        // Ensure mode is Allowed
        await client.PutAsJsonAsync("/api/v1/Organizations/ws-create-test/settings", new
        {
            workScheduleChangeMode = 2 // Allowed
        });

        var response = await client.PostAsJsonAsync("/api/v1/organizations/ws-create-test/work-schedule/change", new
        {
            validFrom = "2026-04-01",
            weeklyWorkHours = 40.0,
            distributeEvenly = true
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var schedule = (await response.Content.ReadFromJsonAsync<WorkSchedulePlanResponseDto>(TestHelpers.JsonOptions))!.Current;
        schedule!.WeeklyWorkHours.Should().Be(40.0);
        schedule.TargetMon.Should().Be(8.0);
        schedule.TargetTue.Should().Be(8.0);
    }

    [Fact]
    public async Task ChangeMyWorkSchedule_WithManualTargets_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "WS Manual Test", "ws-manual-test");
        await client.PutAsJsonAsync("/api/v1/Organizations/ws-manual-test/settings", new { workScheduleChangeMode = 2 });

        var response = await client.PostAsJsonAsync("/api/v1/organizations/ws-manual-test/work-schedule/change", new
        {
            validFrom = "2026-05-01",
            distributeEvenly = false,
            targetMon = 8.0,
            targetTue = 8.0,
            targetWed = 8.0,
            targetThu = 8.0,
            targetFri = 0.0
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var schedule = (await response.Content.ReadFromJsonAsync<WorkSchedulePlanResponseDto>(TestHelpers.JsonOptions))!.Current;
        schedule!.TargetFri.Should().Be(0.0);
        schedule.TargetMon.Should().Be(8.0);
    }

    // ── Self-service: Change again, cancel the future one ────────────────

    [Fact]
    public async Task ChangeMyWorkSchedule_Again_EndsThePreviousOne()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "WS Update Test", "ws-update-test");
        await client.PutAsJsonAsync("/api/v1/Organizations/ws-update-test/settings", new { workScheduleChangeMode = 2 });

        (await client.PostAsJsonAsync("/api/v1/organizations/ws-update-test/work-schedule/change", new
        {
            validFrom = "2026-06-01",
            weeklyWorkHours = 35.0,
            distributeEvenly = true
        })).EnsureSuccessStatusCode();

        // Without a first day the change starts today
        var changeResp = await client.PostAsJsonAsync("/api/v1/organizations/ws-update-test/work-schedule/change", new
        {
            weeklyWorkHours = 30.0,
            distributeEvenly = true
        });
        changeResp.EnsureSuccessStatusCode();

        var plan = await changeResp.Content.ReadFromJsonAsync<WorkSchedulePlanResponseDto>(TestHelpers.JsonOptions);
        plan!.Current!.WeeklyWorkHours.Should().Be(30.0);
        plan.History.Should().ContainSingle().Which.WeeklyWorkHours.Should().Be(35.0);
    }

    [Fact]
    public async Task CancelMyFutureWorkSchedule_ReturnsThePlanWithoutIt()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "WS Delete Test", "ws-delete-test");
        await client.PutAsJsonAsync("/api/v1/Organizations/ws-delete-test/settings", new { workScheduleChangeMode = 2 });

        var createResp = await client.PostAsJsonAsync("/api/v1/organizations/ws-delete-test/work-schedule/change", new
        {
            validFrom = DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM-dd"),
            weeklyWorkHours = 40.0,
            distributeEvenly = true
        });
        createResp.EnsureSuccessStatusCode();
        (await createResp.Content.ReadFromJsonAsync<WorkSchedulePlanResponseDto>(TestHelpers.JsonOptions))!.Future.Should().NotBeNull();

        var deleteResp = await client.DeleteAsync("/api/v1/organizations/ws-delete-test/work-schedule/future");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await deleteResp.Content.ReadFromJsonAsync<WorkSchedulePlanResponseDto>(TestHelpers.JsonOptions))!.Future.Should().BeNull();
    }

    // ── Disabled mode ────────────────────────────────────────────────────

    [Fact]
    public async Task ChangeMyWorkSchedule_WhenDisabled_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "WS Disabled Test", "ws-disabled-test");

        // Set mode to Disabled
        await client.PutAsJsonAsync("/api/v1/Organizations/ws-disabled-test/settings", new { workScheduleChangeMode = 0 });

        // Register a new member
        var newClient = _factory.CreateClient();
        var newUser = await TestHelpers.RegisterAsync(newClient, "ws-disabled@test.com", "Password123!", "WS", "Disabled");

        // Add as member
        await TestHelpers.AddMemberAsync(_factory, client, "ws-disabled-test", newUser);

        TestHelpers.SetBearerToken(newClient, newUser.AccessToken);

        var response = await newClient.PostAsJsonAsync("/api/v1/organizations/ws-disabled-test/work-schedule/change", new
        {
            weeklyWorkHours = 40.0,
            distributeEvenly = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Admin: Manage member schedules ───────────────────────────────────

    [Fact]
    public async Task AdminChangeMemberSchedule_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "Admin WS Test", "admin-ws-test");

        // Add Tom
        var tomLogin = await TestHelpers.LoginAsync(_factory.CreateClient(), TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);
        await TestHelpers.AddMemberAsync(_factory, client, "admin-ws-test", tomLogin);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/admin-ws-test/members/{tomLogin.User.Id}/work-schedule/change", new
            {
                validFrom = "2026-09-01",
                weeklyWorkHours = 20.0,
                distributeEvenly = true
            });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var schedule = (await response.Content.ReadFromJsonAsync<WorkSchedulePlanResponseDto>(TestHelpers.JsonOptions))!.Current;
        schedule!.WeeklyWorkHours.Should().Be(20.0);
        schedule.UserId.Should().Be(tomLogin.User.Id);
    }

    [Fact]
    public async Task AdminGetMemberSchedules_ReturnsSchedules()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Get Tom's schedules from the seeded org
        var detail = await client.GetFromJsonAsync<OrganizationDetailResponseDto>(
            $"/api/v1/Organizations/{TestHelpers.SeedOrgSlug}", TestHelpers.JsonOptions);
        var tom = detail!.Members.First(m => m.Email == TestHelpers.SeedMemberEmail);

        var response = await client.GetAsync(
            $"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/members/{tom.Id}/work-schedules");
        response.EnsureSuccessStatusCode();

        var schedules = await response.Content.ReadFromJsonAsync<List<WorkScheduleResponseDto>>(TestHelpers.JsonOptions);
        schedules.Should().NotBeNull();
    }

    // ── Initial overtime ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateMyInitialOvertime_WhenAllowed_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "IO Test", "io-test-org");
        await client.PutAsJsonAsync("/api/v1/Organizations/io-test-org/settings", new { initialOvertimeMode = 2 });

        var response = await client.PutAsJsonAsync("/api/v1/organizations/io-test-org/initial-overtime", new
        {
            initialOvertimeHours = 10.5
        });
        response.EnsureSuccessStatusCode();
    }
}
