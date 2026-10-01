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
        foreach (var login in new[] { ga, a, b })
            (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, login)).EnsureSuccessStatusCode();

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
    public async Task AllGroupPermissions_InOneRequest_ForEveryMember()
    {
        var (owner, groupAdmin, slug, groupId, _, _) = await SetupAsync("grp-allperms");
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups", new { name = "Another" })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/permissions", new { csvImportMode = 2 })).EnsureSuccessStatusCode();

        var all = await groupAdmin.GetFromJsonAsync<List<JsonElement>>($"/api/v1/Organizations/{slug}/groups/permissions");
        all!.Select(g => g.GetProperty("groupName").GetString()).Should().Equal("Another", "Team");
        all.Single(g => g.GetProperty("groupId").GetInt32() == groupId).GetProperty("csvImportMode").GetString().Should().Be("Allowed");

        var (stranger, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "grp-allperms-x@test.com");
        (await stranger.GetAsync($"/api/v1/Organizations/{slug}/groups/permissions")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

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

    [Fact]
    public async Task Admin_ManagesGroupLifecycle_MembersCanOnlyRead()
    {
        var (owner, groupAdmin, slug, groupId, memberA, memberB) = await SetupAsync("grp-life");

        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}", new { name = "Renamed", description = "d", color = "#5A9188" }))
            .EnsureSuccessStatusCode();
        var detail = await groupAdmin.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}/groups/{groupId}");
        detail.GetProperty("name").GetString().Should().Be("Renamed");

        // Only org admins change roles, edit permissions, rename or delete
        (await groupAdmin.PatchAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/members/{memberB}", new { role = "Admin" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await groupAdmin.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/permissions", new { csvImportMode = 2 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await groupAdmin.DeleteAsync($"/api/v1/Organizations/{slug}/groups/{groupId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await owner.PatchAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/members/{memberB}", new { role = "Chief" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.PatchAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/members/{memberB}", new { role = "Admin" }))
            .EnsureSuccessStatusCode();

        var perms = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}/groups/{groupId}/permissions");
        perms.ValueKind.Should().Be(JsonValueKind.Object);

        (await owner.DeleteAsync($"/api/v1/Organizations/{slug}/groups/{groupId}")).EnsureSuccessStatusCode();
        var groups = await owner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/Organizations/{slug}/groups");
        groups.Should().NotContain(g => g.GetProperty("id").GetInt32() == groupId);
    }

    [Fact]
    public async Task EffectivePermissionsOfOthers_AreAdminOnly()
    {
        var (owner, groupAdmin, slug, _, memberA, _) = await SetupAsync("grp-effective");

        (await groupAdmin.GetAsync($"/api/v1/Organizations/{slug}/members/{memberA}/effective-permissions"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.GetAsync($"/api/v1/Organizations/{slug}/members/{memberA}/effective-permissions")).EnsureSuccessStatusCode();
        (await owner.GetAsync($"/api/v1/Organizations/{slug}/members/999999/effective-permissions"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddingNonMembersOrDuplicates_IsRejected()
    {
        var (owner, _, slug, groupId, _, memberB) = await SetupAsync("grp-reject");
        var (_, outsider) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "grp-reject-outsider@test.com");

        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/members", new { userId = outsider.User.Id }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/members", new { userId = memberB }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups/999999/members", new { userId = memberB }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
