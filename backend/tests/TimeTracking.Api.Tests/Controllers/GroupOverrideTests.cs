using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// The override rule: a group override replaces the organization default in either direction,
/// the most permissive of several group overrides wins, no override means the org default.
/// </summary>
public class GroupOverrideTests : IClassFixture<TimeTrackingApiFactory>
{
    private const int Disabled = 0, Allowed = 2;
    private const int Private = 0, AdminOnly = 1, AllMembers = 2;

    private readonly TimeTrackingApiFactory _factory;

    public GroupOverrideTests(TimeTrackingApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Owner, string Slug, int OrgId)> OrgAsync(string prefix, object settings)
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-owner@ovr.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, $"{prefix} Org", $"{prefix}-org");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", settings)).EnsureSuccessStatusCode();
        return (owner, org.Slug, org.Id);
    }

    private async Task<(HttpClient Client, int UserId)> MemberAsync(HttpClient owner, string slug, string email)
    {
        var (client, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, email);
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/members", new { userId = login.User.Id })).EnsureSuccessStatusCode();
        return (client, login.User.Id);
    }

    private static async Task<int> GroupAsync(HttpClient owner, string slug, string name, object permissions, params (int UserId, string Role)[] members)
    {
        var id = (await (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups", new { name }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        foreach (var (userId, role) in members)
            (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{id}/members", new { userId, role })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{id}/permissions", permissions)).EnsureSuccessStatusCode();
        return id;
    }

    private static async Task<int> TrackedEntryAsync(HttpClient client, int orgId)
    {
        (await client.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationId = orgId })).EnsureSuccessStatusCode();
        var stopped = await (await client.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null)).Content.ReadFromJsonAsync<JsonElement>();
        return stopped.GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task StricterGroup_OverridesPermissiveOrgDefault_ForEditAndDelete()
    {
        var (owner, slug, orgId) = await OrgAsync("ovr-strict", new { editPastEntriesMode = Allowed });
        var (intern, internId) = await MemberAsync(owner, slug, "ovr-strict-intern@ovr.test");
        var (regular, _) = await MemberAsync(owner, slug, "ovr-strict-regular@ovr.test");
        await GroupAsync(owner, slug, "Interns", new { editPastEntriesMode = Disabled }, (internId, "Member"));

        var internEntry = await TrackedEntryAsync(intern, orgId);
        var regularEntry = await TrackedEntryAsync(regular, orgId);

        (await intern.PutAsJsonAsync($"/api/v1/TimeTracking/{internEntry}", new { description = "edit" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await intern.DeleteAsync($"/api/v1/TimeTracking/{internEntry}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await regular.PutAsJsonAsync($"/api/v1/TimeTracking/{regularEntry}", new { description = "edit" })).EnsureSuccessStatusCode();

        var perms = await intern.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/effective-permissions");
        perms.GetProperty("editPastEntriesMode").GetString().Should().Be("Disabled");
    }

    [Fact]
    public async Task MostPermissiveGroupOverrideWins()
    {
        var (owner, slug, orgId) = await OrgAsync("ovr-multi", new { editPastEntriesMode = Allowed });
        var (member, memberId) = await MemberAsync(owner, slug, "ovr-multi-m@ovr.test");
        await GroupAsync(owner, slug, "Strict", new { editPastEntriesMode = Disabled }, (memberId, "Member"));
        await GroupAsync(owner, slug, "Trusted", new { editPastEntriesMode = Allowed }, (memberId, "Member"));

        var entry = await TrackedEntryAsync(member, orgId);
        (await member.PutAsJsonAsync($"/api/v1/TimeTracking/{entry}", new { description = "ok" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task DeletingPastEntry_FollowsEditRule()
    {
        var (owner, slug, orgId) = await OrgAsync("ovr-delete", new { editPastEntriesMode = Disabled });
        var (member, _) = await MemberAsync(owner, slug, "ovr-delete-m@ovr.test");
        var entry = await TrackedEntryAsync(member, orgId);

        (await member.DeleteAsync($"/api/v1/TimeTracking/{entry}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task VacationVisibility_GroupCanOpenUpOrLockDown()
    {
        // Org default: vacations visible to all members
        var (owner, slug, _) = await OrgAsync("ovr-vis", new { vacationVisibility = AllMembers, sickDayVisibility = Private });
        var (viewer, viewerId) = await MemberAsync(owner, slug, "ovr-vis-viewer@ovr.test");
        var (open, openId) = await MemberAsync(owner, slug, "ovr-vis-open@ovr.test");
        var (hidden, hiddenId) = await MemberAsync(owner, slug, "ovr-vis-hidden@ovr.test");
        var (lead, leadId) = await MemberAsync(owner, slug, "ovr-vis-lead@ovr.test");

        // "Board" hides its members' vacations from ordinary members; its group admin still sees them
        await GroupAsync(owner, slug, "Board", new { vacationVisibility = AdminOnly }, (hiddenId, "Member"), (leadId, "Admin"));
        // "Team" shares even sick days among its members
        await GroupAsync(owner, slug, "Team", new { sickDayVisibility = AllMembers }, (openId, "Member"), (viewerId, "Member"));

        foreach (var (client, date) in new[] { (open, "2026-04-01"), (hidden, "2026-04-02") })
            (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences/admin",
                new { userId = client == open ? openId : hiddenId, date, type = 1 })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences/admin",
            new { userId = openId, date = "2026-04-03", type = 0 })).EnsureSuccessStatusCode();

        async Task<List<(int UserId, int Type)>> SeenBy(HttpClient c) =>
            (await c.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/absences?limit=200"))
                .GetProperty("items").EnumerateArray()
                .Select(a => (a.GetProperty("userId").GetInt32(), a.GetProperty("type").ValueKind == JsonValueKind.String
                    ? (a.GetProperty("type").GetString() == "Vacation" ? 1 : a.GetProperty("type").GetString() == "SickDay" ? 0 : 2)
                    : a.GetProperty("type").GetInt32()))
                .ToList();

        var viewerSees = await SeenBy(viewer);
        viewerSees.Should().Contain((openId, 1));          // org default AllMembers
        viewerSees.Should().NotContain(a => a.UserId == hiddenId); // stricter group override
        viewerSees.Should().Contain((openId, 0));          // looser group override (shared group)

        (await SeenBy(lead)).Should().Contain((hiddenId, 1)); // AdminOnly → the group's admin
    }
}
