using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Integration tests for the Reports controller: monthly/yearly PDF generation
/// and the export-permission gating (self, org admin, group admin capability).
/// </summary>
public class ReportsControllerTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public ReportsControllerTests(TimeTrackingApiFactory factory) => _factory = factory;

    private record GroupDto { public int Id { get; init; } }

    [Fact]
    public async Task MonthlyReport_Self_ReturnsPdf()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);
        var ownId = await GetOwnUserIdAsync(client);

        var response = await client.GetAsync(
            $"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/members/{ownId}/reports/monthly?year=2026&month=6");

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(0);
        bytes.Take(4).Should().Equal((byte)'%', (byte)'P', (byte)'D', (byte)'F');
    }

    [Fact]
    public async Task MonthlyReport_CurrentMonth_ExtendingIntoFuture_StillReturnsPdf()
    {
        // The requested month includes days after "today" — the underlying
        // aggregator clamps to today, so this must still succeed with a valid PDF
        // (not blow up on an inverted or empty date range for a month just starting).
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);
        var ownId = await GetOwnUserIdAsync(client);
        var now = DateTime.UtcNow;

        var response = await client.GetAsync(
            $"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/members/{ownId}/reports/monthly?year={now.Year}&month={now.Month}");

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task YearlyReport_Self_ReturnsPdf()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);
        var ownId = await GetOwnUserIdAsync(client);

        var response = await client.GetAsync(
            $"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/members/{ownId}/reports/yearly?year=2026");

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(0);
        bytes.Take(4).Should().Equal((byte)'%', (byte)'P', (byte)'D', (byte)'F');
    }

    [Fact]
    public async Task MonthlyReport_OrgAdmin_ForOtherMember_Succeeds()
    {
        var ownerClient = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(ownerClient, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var detail = await ownerClient.GetFromJsonAsync<OrganizationDetailResponseDto>(
            $"/api/v1/Organizations/{TestHelpers.SeedOrgSlug}", TestHelpers.JsonOptions);
        var tom = detail!.Members.First(m => m.Email == TestHelpers.SeedMemberEmail);

        var response = await ownerClient.GetAsync(
            $"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/members/{tom.Id}/reports/monthly?year=2026&month=6");

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task MonthlyReport_RegularMember_ForOtherMember_IsForbidden()
    {
        var ownerClient = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(ownerClient, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var detail = await ownerClient.GetFromJsonAsync<OrganizationDetailResponseDto>(
            $"/api/v1/Organizations/{TestHelpers.SeedOrgSlug}", TestHelpers.JsonOptions);
        var owner = detail!.Members.First(m => m.Email == TestHelpers.SeedOwnerEmail);

        var memberClient = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(memberClient, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);

        var response = await memberClient.GetAsync(
            $"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/members/{owner.Id}/reports/monthly?year=2026&month=6");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MonthlyReport_UnknownMember_Returns404()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync(
            $"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/members/999999/reports/monthly?year=2026&month=6");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MonthlyReport_InvalidMonth_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        var ownId = await GetOwnUserIdAsync(client);

        var response = await client.GetAsync(
            $"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/members/{ownId}/reports/monthly?year=2026&month=13");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MonthlyReport_GroupAdminWithExportCapability_ForGroupMember_Succeeds()
    {
        var ownerClient = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(ownerClient, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(ownerClient, "Report Export Org", "report-export-org");

        // Register a group admin and a regular member
        var groupAdminClient = _factory.CreateClient();
        var groupAdmin = await TestHelpers.RegisterAsync(groupAdminClient, "report-groupadmin@test.com", "Password123!", "Group", "Admin");
        TestHelpers.SetBearerToken(groupAdminClient, groupAdmin.AccessToken);
        await ownerClient.PostAsJsonAsync("/api/v1/Organizations/report-export-org/members", new { userId = groupAdmin.User.Id, role = 0 });

        var memberClient = _factory.CreateClient();
        var member = await TestHelpers.RegisterAsync(memberClient, "report-member@test.com", "Password123!", "Plain", "Member");
        TestHelpers.SetBearerToken(memberClient, member.AccessToken);
        await ownerClient.PostAsJsonAsync("/api/v1/Organizations/report-export-org/members", new { userId = member.User.Id, role = 0 });

        // Create a group, add both, promote groupAdmin to Admin, enable export capability
        var createGroupResp = await ownerClient.PostAsJsonAsync("/api/v1/organizations/report-export-org/groups", new
        {
            name = "Reporters",
            description = "Test group",
            color = "#123456"
        });
        createGroupResp.EnsureSuccessStatusCode();
        var group = await createGroupResp.Content.ReadFromJsonAsync<GroupDto>(TestHelpers.JsonOptions);

        await ownerClient.PostAsJsonAsync($"/api/v1/organizations/report-export-org/groups/{group!.Id}/members", new
        {
            userId = groupAdmin.User.Id,
            role = "Admin"
        });
        await ownerClient.PostAsJsonAsync($"/api/v1/organizations/report-export-org/groups/{group.Id}/members", new
        {
            userId = member.User.Id,
            role = "Member"
        });

        // Before enabling the capability: group admin cannot export the member's report
        var forbiddenResponse = await groupAdminClient.GetAsync(
            $"/api/v1/organizations/report-export-org/members/{member.User.Id}/reports/monthly?year=2026&month=6");
        forbiddenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var permResponse = await ownerClient.PutAsJsonAsync($"/api/v1/organizations/report-export-org/groups/{group.Id}/permissions", new
        {
            groupAdminCanExportData = true
        });
        permResponse.EnsureSuccessStatusCode();

        // After enabling: group admin can export the member's report
        var allowedResponse = await groupAdminClient.GetAsync(
            $"/api/v1/organizations/report-export-org/members/{member.User.Id}/reports/yearly?year=2026");
        allowedResponse.EnsureSuccessStatusCode();
        allowedResponse.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static async Task<int> GetOwnUserIdAsync(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<UserInfoDto>("/api/v1/Auth/me", TestHelpers.JsonOptions);
        return me!.Id;
    }
}
