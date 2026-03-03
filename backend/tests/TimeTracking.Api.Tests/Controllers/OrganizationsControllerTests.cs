using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Integration tests for Organization CRUD, members, settings, time overview, and member entries.
/// </summary>
public class OrganizationsControllerTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public OrganizationsControllerTests(TimeTrackingApiFactory factory) => _factory = factory;

    // ── List & Get ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrganizations_ReturnsSeededOrg()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/Organizations");
        response.EnsureSuccessStatusCode();

        var orgs = await response.Content.ReadFromJsonAsync<List<OrganizationResponseDto>>(TestHelpers.JsonOptions);
        orgs.Should().NotBeNull();
        orgs!.Should().Contain(o => o.Slug == TestHelpers.SeedOrgSlug);
    }

    [Fact]
    public async Task GetOrganization_BySlug_ReturnsDetail()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/Organizations/{TestHelpers.SeedOrgSlug}");
        response.EnsureSuccessStatusCode();

        var org = await response.Content.ReadFromJsonAsync<OrganizationDetailResponseDto>(TestHelpers.JsonOptions);
        org.Should().NotBeNull();
        org!.Slug.Should().Be(TestHelpers.SeedOrgSlug);
        org.Members.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task GetOrganization_NonExistent_Returns404()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/Organizations/does-not-exist");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetUserOrganizations_ReturnsOrgForSeedUser()
    {
        var client = _factory.CreateClient();
        var login = await TestHelpers.LoginAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync($"/api/Organizations/user/{login.User.Id}");
        response.EnsureSuccessStatusCode();

        var userOrgs = await response.Content.ReadFromJsonAsync<List<UserOrganizationResponseDto>>(TestHelpers.JsonOptions);
        userOrgs.Should().NotBeNull();
        userOrgs!.Should().Contain(o => o.Slug == TestHelpers.SeedOrgSlug);
    }

    // ── Create ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateOrganization_ValidData_Returns201()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var org = await TestHelpers.CreateOrganizationAsync(client, "Test Corp", "test-corp", "A test org");

        org.Name.Should().Be("Test Corp");
        org.Slug.Should().Be("test-corp");
        org.MemberCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateOrganization_DuplicateSlug_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.PostAsJsonAsync("/api/Organizations", new
        {
            name = "Duplicate",
            slug = TestHelpers.SeedOrgSlug
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateOrganization_InvalidSlug_Returns400()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.PostAsJsonAsync("/api/Organizations", new
        {
            name = "Bad Slug",
            slug = "INVALID SLUG!!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrganization_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/Organizations", new
        {
            name = "Auth Test",
            slug = "auth-test"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Update ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateOrganization_AsOwner_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Create a new org to update
        await TestHelpers.CreateOrganizationAsync(client, "Update Me", "update-me-org");

        var response = await client.PutAsJsonAsync("/api/Organizations/update-me-org", new
        {
            name = "Updated Name",
            description = "Updated desc"
        });
        response.EnsureSuccessStatusCode();

        var org = await response.Content.ReadFromJsonAsync<OrganizationResponseDto>(TestHelpers.JsonOptions);
        org!.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task UpdateOrganization_AsMember_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        // Tom is a Member in the seed org
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);

        var response = await client.PutAsJsonAsync($"/api/Organizations/{TestHelpers.SeedOrgSlug}", new
        {
            name = "Should Fail"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Delete ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteOrganization_AsOwner_Returns204()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        await TestHelpers.CreateOrganizationAsync(client, "ToDelete", "to-delete-org");

        var response = await client.DeleteAsync("/api/Organizations/to-delete-org");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's gone
        var getResponse = await client.GetAsync("/api/Organizations/to-delete-org");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Members ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AddMember_AsOwner_Returns201()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Create a new org and a new user to add
        await TestHelpers.CreateOrganizationAsync(client, "Member Test Org", "member-test-org");

        // Register a new user
        var newClient = _factory.CreateClient();
        var newUser = await TestHelpers.RegisterAsync(newClient, "addme@test.com", "Password123!", "Add", "Me");

        var response = await client.PostAsJsonAsync("/api/Organizations/member-test-org/members", new
        {
            userId = newUser.User.Id,
            role = 0 // Member
        });
        response.EnsureSuccessStatusCode();

        var member = await response.Content.ReadFromJsonAsync<OrganizationMemberResponseDto>(TestHelpers.JsonOptions);
        member!.Email.Should().Be("addme@test.com");
        member.Role.Should().Be("Member");
    }

    [Fact]
    public async Task UpdateMemberRole_AsOwner_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Get the org detail to find Anna's member ID
        var detail = await client.GetFromJsonAsync<OrganizationDetailResponseDto>(
            $"/api/Organizations/{TestHelpers.SeedOrgSlug}", TestHelpers.JsonOptions);
        var anna = detail!.Members.First(m => m.Email == TestHelpers.SeedAdminEmail);

        // Change Anna to Member
        var response = await client.PutAsJsonAsync(
            $"/api/Organizations/{TestHelpers.SeedOrgSlug}/members/{anna.Id}", new { role = 0 });
        response.EnsureSuccessStatusCode();

        // Verify
        var updated = await response.Content.ReadFromJsonAsync<OrganizationMemberResponseDto>(TestHelpers.JsonOptions);
        updated!.Role.Should().Be("Member");

        // Restore to Admin
        await client.PutAsJsonAsync(
            $"/api/Organizations/{TestHelpers.SeedOrgSlug}/members/{anna.Id}", new { role = 1 });
    }

    [Fact]
    public async Task RemoveMember_AsOwner_Returns204()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Create a new org with a new member, then remove them
        await TestHelpers.CreateOrganizationAsync(client, "Remove Test", "remove-test-org");

        var newClient = _factory.CreateClient();
        var newUser = await TestHelpers.RegisterAsync(newClient, "removeme@test.com", "Password123!", "Rem", "Ove");

        await client.PostAsJsonAsync("/api/Organizations/remove-test-org/members", new
        {
            userId = newUser.User.Id,
            role = 0
        });

        var response = await client.DeleteAsync(
            $"/api/Organizations/remove-test-org/members/{newUser.User.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Settings ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateSettings_AsOwner_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        await TestHelpers.CreateOrganizationAsync(client, "Settings Test", "settings-test-org");

        var response = await client.PutAsJsonAsync("/api/Organizations/settings-test-org/settings", new
        {
            autoPauseEnabled = false,
            editPastEntriesMode = 1, // RequiresApproval
            memberTimeEntryVisibility = true
        });
        response.EnsureSuccessStatusCode();

        // Verify
        var detail = await client.GetFromJsonAsync<OrganizationDetailResponseDto>(
            "/api/Organizations/settings-test-org", TestHelpers.JsonOptions);
        detail!.AutoPauseEnabled.Should().BeFalse();
        detail.EditPastEntriesMode.Should().Be("RequiresApproval");
        detail.MemberTimeEntryVisibility.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateSettings_AsMember_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);

        var response = await client.PutAsJsonAsync(
            $"/api/Organizations/{TestHelpers.SeedOrgSlug}/settings", new
            {
                autoPauseEnabled = false
            });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Time overview ────────────────────────────────────────────────────

    [Fact]
    public async Task GetTimeOverview_AsAdmin_ReturnsMembers()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync($"/api/Organizations/{TestHelpers.SeedOrgSlug}/time-overview");
        response.EnsureSuccessStatusCode();

        var overview = await response.Content.ReadFromJsonAsync<List<MemberTimeOverviewResponseDto>>(TestHelpers.JsonOptions);
        overview.Should().NotBeNull();
        overview!.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetTimeOverview_AsMember_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync($"/api/Organizations/{TestHelpers.SeedOrgSlug}/time-overview");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Member entries ───────────────────────────────────────────────────

    [Fact]
    public async Task GetMemberEntries_AsAdmin_ReturnsEntries()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Get Anna's user ID
        var detail = await client.GetFromJsonAsync<OrganizationDetailResponseDto>(
            $"/api/Organizations/{TestHelpers.SeedOrgSlug}", TestHelpers.JsonOptions);
        var anna = detail!.Members.First(m => m.Email == TestHelpers.SeedAdminEmail);

        var response = await client.GetAsync(
            $"/api/Organizations/{TestHelpers.SeedOrgSlug}/member-entries/{anna.Id}");
        response.EnsureSuccessStatusCode();

        var entries = await response.Content.ReadFromJsonAsync<List<TimeEntryResponseDto>>(TestHelpers.JsonOptions);
        entries.Should().NotBeNull();
    }

    // ── Initial overtime ─────────────────────────────────────────────────

    [Fact]
    public async Task SetMemberInitialOvertime_AsOwner_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Get Tom's member ID
        var detail = await client.GetFromJsonAsync<OrganizationDetailResponseDto>(
            $"/api/Organizations/{TestHelpers.SeedOrgSlug}", TestHelpers.JsonOptions);
        var tom = detail!.Members.First(m => m.Email == TestHelpers.SeedMemberEmail);

        var response = await client.PutAsJsonAsync(
            $"/api/Organizations/{TestHelpers.SeedOrgSlug}/members/{tom.Id}/initial-overtime",
            new { initialOvertimeHours = 5.0 });
        response.EnsureSuccessStatusCode();
    }
}
