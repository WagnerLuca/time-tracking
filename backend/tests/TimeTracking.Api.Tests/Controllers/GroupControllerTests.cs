using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Integration tests for group member management and the rights of group admins.
/// </summary>
public class GroupControllerTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public GroupControllerTests(TimeTrackingApiFactory factory) => _factory = factory;

    /// <summary>Owner with an org and a group; a group admin and two plain org members.</summary>
    private async Task<(HttpClient Owner, HttpClient GroupAdmin, string Slug, int GroupId, int MemberA, int MemberB)> SetupAsync(string prefix)
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-owner@test.com");
        var org = await TestHelpers.CreateOrganizationAsync(owner, $"{prefix} Org", $"{prefix}-org");

        var (groupAdmin, ga) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-ga@test.com");
        var (_, a) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-a@test.com");
        var (_, b) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-b@test.com");
        foreach (var id in new[] { ga.User.Id, a.User.Id, b.User.Id })
            (await owner.PostAsJsonAsync($"/api/v1/Organizations/{org.Slug}/members", new { userId = id })).EnsureSuccessStatusCode();

        var created = await owner.PostAsJsonAsync($"/api/v1/Organizations/{org.Slug}/groups", new { name = "Team" });
        created.EnsureSuccessStatusCode();
        var groupId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{org.Slug}/groups/{groupId}/members",
            new { userId = ga.User.Id, role = "Admin" })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{org.Slug}/groups/{groupId}/members",
            new { userId = b.User.Id })).EnsureSuccessStatusCode();

        return (owner, groupAdmin, org.Slug, groupId, a.User.Id, b.User.Id);
    }

    private static Task<HttpResponseMessage> AllowGroupAdminsToManage(HttpClient owner, string slug, int groupId) =>
        owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/permissions",
            new { groupAdminCanManageMembers = true });

    [Fact]
    public async Task GroupAdmin_WithoutManageRight_CannotAddOrRemoveMembers()
    {
        var (_, groupAdmin, slug, groupId, memberA, memberB) = await SetupAsync("grp-noright");

        var add = await groupAdmin.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/members", new { userId = memberA });
        add.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var remove = await groupAdmin.DeleteAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/members/{memberB}");
        remove.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GroupAdmin_WithManageRight_CanAddAndRemoveRegularMembers()
    {
        var (owner, groupAdmin, slug, groupId, memberA, memberB) = await SetupAsync("grp-right");
        (await AllowGroupAdminsToManage(owner, slug, groupId)).EnsureSuccessStatusCode();

        var add = await groupAdmin.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/members", new { userId = memberA });
        add.IsSuccessStatusCode.Should().BeTrue();

        var remove = await groupAdmin.DeleteAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/members/{memberB}");
        remove.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task GroupAdmin_CannotGrantGroupAdminRole_EvenWithManageRight()
    {
        var (owner, groupAdmin, slug, groupId, memberA, _) = await SetupAsync("grp-escalate");
        (await AllowGroupAdminsToManage(owner, slug, groupId)).EnsureSuccessStatusCode();

        var add = await groupAdmin.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/members",
            new { userId = memberA, role = "Admin" });
        add.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PlainMember_CanLeaveGroup()
    {
        var (_, _, slug, groupId, _, _) = await SetupAsync("grp-leave");
        var member = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(member, "grp-leave-b@test.com", "Password123!");

        var me = await member.GetFromJsonAsync<JsonElement>("/api/v1/Auth/me");
        var leave = await member.DeleteAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/members/{me.GetProperty("id").GetInt32()}");
        leave.IsSuccessStatusCode.Should().BeTrue();
    }
}
