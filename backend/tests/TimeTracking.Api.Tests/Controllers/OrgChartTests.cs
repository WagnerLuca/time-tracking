using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// The Organigramm (issue #66): groups with parents and who may manage them, the people the caller may see
/// (hidden people left out, hidden kinds null), presence — and the team calendar's <c>groupId</c> absence filter.
/// </summary>
public class OrgChartTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public OrgChartTests(TimeTrackingApiFactory factory) => _factory = factory;

    /// <summary>
    /// Dept → Area → Team (3 levels) and a top-level "Other". Lea leads Area (may manage members); Ana is in Team and
    /// Other and runs a timer; Ben is in Team and has a failure entry; Dora is in Dept; Xaver in Other; Cleo in no group.
    /// Everyone but the owner has one vacation day.
    /// </summary>
    private sealed record Setup(HttpClient Owner, int OwnerId, string Slug, int OrgId,
        HttpClient Lea, int LeaId, int AnaId, HttpClient Ben, int BenId, int CleoId, int DoraId, int XaverId,
        int Dept, int Area, int Team, int Other);

    private async Task<Setup> SetupAsync(string prefix)
    {
        var (owner, o) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-owner@chart.test", "Olga", "Owner");
        var org = await TestHelpers.CreateOrganizationAsync(owner, $"{prefix} Org", $"{prefix}-org");
        async Task<(HttpClient, int)> Member(string name)
        {
            var (client, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-{name.ToLowerInvariant()}@chart.test", name, "Test");
            (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, login)).EnsureSuccessStatusCode();
            return (client, login.User.Id);
        }
        var (lea, leaId) = await Member("Lea");
        var (ana, anaId) = await Member("Ana");
        var (ben, benId) = await Member("Ben");
        var (_, cleoId) = await Member("Cleo");
        var (_, doraId) = await Member("Dora");
        var (_, xaverId) = await Member("Xaver");

        async Task<int> Group(string name, int? parentGroupId, object permissions, params (int UserId, string Role)[] members)
        {
            var created = await owner.PostAsJsonAsync($"/api/v1/Organizations/{org.Slug}/groups", new { name, parentGroupId });
            created.EnsureSuccessStatusCode();
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            foreach (var (userId, role) in members)
                (await owner.PostAsJsonAsync($"/api/v1/Organizations/{org.Slug}/groups/{id}/members", new { userId, role })).EnsureSuccessStatusCode();
            (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/groups/{id}/permissions", permissions)).EnsureSuccessStatusCode();
            return id;
        }
        var dept = await Group("Dept", null, new { }, (doraId, "Member"));
        var area = await Group("Area", dept, new { groupAdminCanManageMembers = true }, (leaId, "Admin"));
        var team = await Group("Team", area, new { }, (anaId, "Member"), (benId, "Member"));
        var other = await Group("Other", null, new { }, (anaId, "Member"), (xaverId, "Member"));

        var clock = OrgClock.For("Europe/Berlin");
        var today = clock.Today();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            foreach (var id in new[] { leaId, anaId, benId, cleoId, doraId, xaverId })
                db.AbsenceDays.Add(new AbsenceDay { UserId = id, OrganizationId = org.Id, Date = today.AddDays(-3), Type = AbsenceType.Vacation });
            var failure = clock.ToUtc(today.AddDays(-5).ToDateTime(new TimeOnly(9, 0)));
            db.TimeEntries.Add(new TimeEntry
            {
                UserId = benId, OrganizationId = org.Id, StartTime = failure, EndTime = failure.AddHours(15), IsFailure = true,
                CreatedAt = failure, UpdatedAt = failure
            });
            await db.SaveChangesAsync();
        }
        (await ana.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationId = org.Id })).EnsureSuccessStatusCode();

        return new Setup(owner, o.User.Id, org.Slug, org.Id, lea, leaId, anaId, ben, benId, cleoId, doraId, xaverId, dept, area, team, other);
    }

    private static async Task SettingsAsync(Setup s, params (string Kind, bool? In, bool? Out)[] rows) =>
        (await s.Owner.PutAsJsonAsync($"/api/v1/Organizations/{s.Slug}/settings", new { visibility = TestHelpers.Matrix(rows) })).EnsureSuccessStatusCode();

    private static async Task GroupVisibilityAsync(Setup s, int groupId, params (string Kind, bool? In, bool? Out)[] rows) =>
        (await s.Owner.PutAsJsonAsync($"/api/v1/Organizations/{s.Slug}/groups/{groupId}/permissions", new { visibility = TestHelpers.Matrix(rows) })).EnsureSuccessStatusCode();

    private static async Task<(Dictionary<int, JsonElement> Groups, Dictionary<int, JsonElement> People)> ChartAsync(HttpClient client, string slug)
    {
        var response = await client.GetAsync($"/api/v1/Organizations/{slug}/org-chart");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("groups").EnumerateArray().ToDictionary(g => g.GetProperty("id").GetInt32()),
                body.GetProperty("people").EnumerateArray().ToDictionary(p => p.GetProperty("userId").GetInt32()));
    }

    private static bool IsNull(JsonElement e, string property) => e.GetProperty(property).ValueKind == JsonValueKind.Null;

    private static List<(int GroupId, bool IsLead)> Memberships(JsonElement person) =>
        person.GetProperty("groups").EnumerateArray().Select(m => (m.GetProperty("groupId").GetInt32(), m.GetProperty("isLead").GetBoolean())).ToList();

    private static async Task<List<int>> AbsenceUsersAsync(HttpClient client, string slug, int? groupId = null)
    {
        var url = $"/api/v1/Organizations/{slug}/absences?limit=200" + (groupId is { } g ? $"&groupId={g}" : "");
        var body = await client.GetFromJsonAsync<JsonElement>(url);
        return body.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("userId").GetInt32()).ToList();
    }

    // ── The chart ──────────────────────────────────────────────────────

    [Fact]
    public async Task Owner_SeesEveryone_WithEmails_GroupsOverThreeLevels_LeadsAndPresence()
    {
        var s = await SetupAsync("chart-owner");
        var (groups, people) = await ChartAsync(s.Owner, s.Slug);

        groups.Keys.Should().BeEquivalentTo([s.Dept, s.Area, s.Team, s.Other]);
        IsNull(groups[s.Dept], "parentGroupId").Should().BeTrue();
        groups[s.Area].GetProperty("parentGroupId").GetInt32().Should().Be(s.Dept);
        groups[s.Team].GetProperty("parentGroupId").GetInt32().Should().Be(s.Area);
        IsNull(groups[s.Other], "parentGroupId").Should().BeTrue();
        groups.Values.Should().OnlyContain(g => g.GetProperty("canManageMembers").GetBoolean(), "the owner manages every group");

        people.Keys.Should().BeEquivalentTo([s.OwnerId, s.LeaId, s.AnaId, s.BenId, s.CleoId, s.DoraId, s.XaverId]);
        people[s.AnaId].GetProperty("email").GetString().Should().Be("chart-owner-ana@chart.test");
        people[s.OwnerId].GetProperty("role").GetString().Should().Be("Owner");
        people[s.AnaId].GetProperty("role").GetString().Should().Be("Member");
        people.Values.Should().OnlyContain(p => !IsNull(p, "email") && !IsNull(p, "groups"));

        Memberships(people[s.LeaId]).Should().Equal((s.Area, true));
        Memberships(people[s.AnaId]).Should().BeEquivalentTo([(s.Team, false), (s.Other, false)]);
        Memberships(people[s.BenId]).Should().Equal((s.Team, false));
        Memberships(people[s.DoraId]).Should().Equal((s.Dept, false));
        Memberships(people[s.CleoId]).Should().BeEmpty();

        // Ana's timer runs; Ben's forgotten one was closed as a failure entry — not presence
        people[s.AnaId].GetProperty("present").GetBoolean().Should().BeTrue();
        people[s.AnaId].GetProperty("presentSince").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        people[s.BenId].GetProperty("present").GetBoolean().Should().BeFalse();
        IsNull(people[s.BenId], "presentSince").Should().BeTrue();
        people[s.AnaId].GetProperty("visibleInfo").EnumerateArray().Select(k => k.GetString())
            .Should().Contain(["Member", "Email", "Groups", "Presence"]).And.NotContain("TrackedTime", "admins see hours only with MemberTimeEntryVisibility");
    }

    [Fact]
    public async Task Member_WithRestrictedMatrix_GetsNoHiddenPeople_AndNullWhereKindsAreHidden()
    {
        var s = await SetupAsync("chart-member");
        await SettingsAsync(s, ("Member", true, false), ("Email", false, false), ("Groups", true, false));
        // Team hides its people's groups from colleagues (an override wins over Ana's "Other", which has none)
        await GroupVisibilityAsync(s, s.Team, ("Groups", false, null));

        var (groups, people) = await ChartAsync(s.Ben, s.Slug);

        groups.Should().HaveCount(4, "the structure is open to every member");
        // Ben shares the Dept chain with Lea, Ana and Dora; Cleo (no group), Xaver (Other only) and the owner are outside
        people.Should().HaveCount(4);
        people.Keys.Should().BeEquivalentTo([s.BenId, s.LeaId, s.AnaId, s.DoraId]);

        people[s.BenId].GetProperty("email").GetString().Should().Be("chart-member-ben@chart.test", "members always see themselves");
        foreach (var id in new[] { s.LeaId, s.AnaId, s.DoraId })
            IsNull(people[id], "email").Should().BeTrue();
        Memberships(people[s.BenId]).Should().Equal((s.Team, false));
        Memberships(people[s.LeaId]).Should().Equal((s.Area, true));
        Memberships(people[s.DoraId]).Should().Equal((s.Dept, false));
        IsNull(people[s.AnaId], "groups").Should().BeTrue();
        people[s.AnaId].GetProperty("visibleInfo").EnumerateArray().Select(k => k.GetString()).Should().Contain("Member").And.NotContain(["Email", "Groups", "Presence"]);
    }

    [Fact]
    public async Task Presence_OnlyWhereTheMatrixShowsIt()
    {
        var s = await SetupAsync("chart-presence");
        var (_, people) = await ChartAsync(s.Ben, s.Slug);
        IsNull(people[s.AnaId], "present").Should().BeTrue("the default matrix hides presence");
        IsNull(people[s.AnaId], "presentSince").Should().BeTrue();
        people[s.BenId].GetProperty("present").GetBoolean().Should().BeFalse("own presence, failure entry is not presence");

        await SettingsAsync(s, ("Presence", true, false));
        (_, people) = await ChartAsync(s.Ben, s.Slug);
        people[s.AnaId].GetProperty("present").GetBoolean().Should().BeTrue();
        people[s.AnaId].GetProperty("presentSince").ValueKind.Should().Be(JsonValueKind.String);
        people[s.LeaId].GetProperty("present").GetBoolean().Should().BeFalse();
        IsNull(people[s.CleoId], "present").Should().BeTrue("Cleo is outside Ben's groups");
    }

    [Fact]
    public async Task CanManageMembers_AdminEverything_LeadTheirSubtree_PlainMemberNothing()
    {
        var s = await SetupAsync("chart-manage");

        var (groups, _) = await ChartAsync(s.Lea, s.Slug);
        groups.Where(g => g.Value.GetProperty("canManageMembers").GetBoolean()).Select(g => g.Key)
            .Should().BeEquivalentTo([s.Area, s.Team], "Lea leads Area with member management, Team is below it");

        (groups, _) = await ChartAsync(s.Ben, s.Slug);
        groups.Values.Should().OnlyContain(g => !g.GetProperty("canManageMembers").GetBoolean());

        // Without the capability, leading is not managing
        (await s.Owner.PutAsJsonAsync($"/api/v1/Organizations/{s.Slug}/groups/{s.Area}/permissions", new { groupAdminCanManageMembers = false }))
            .EnsureSuccessStatusCode();
        (groups, _) = await ChartAsync(s.Lea, s.Slug);
        groups.Values.Should().OnlyContain(g => !g.GetProperty("canManageMembers").GetBoolean());

        // An org admin (not owner) manages every group
        (await s.Owner.PutAsJsonAsync($"/api/v1/Organizations/{s.Slug}/members/{s.LeaId}", new { role = 1 })).EnsureSuccessStatusCode();
        (groups, _) = await ChartAsync(s.Lea, s.Slug);
        groups.Values.Should().OnlyContain(g => g.GetProperty("canManageMembers").GetBoolean());
    }

    [Fact]
    public async Task DeactivatedUser_IsLeftOut()
    {
        var s = await SetupAsync("chart-inactive");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            db.Users.Single(u => u.Id == s.XaverId).IsActive = false;
            await db.SaveChangesAsync();
        }
        var (_, people) = await ChartAsync(s.Owner, s.Slug);
        people.Should().HaveCount(6).And.NotContainKey(s.XaverId);
    }

    [Fact]
    public async Task NonMember_IsForbidden()
    {
        var s = await SetupAsync("chart-outsider");
        var (outsider, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "chart-outsider-x@chart.test");
        var response = await outsider.GetAsync($"/api/v1/Organizations/{s.Slug}/org-chart");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("org.notMember");
        (await outsider.GetAsync("/api/v1/Organizations/chart-no-such-org/org-chart")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Absences ?groupId= ─────────────────────────────────────────────

    [Fact]
    public async Task AbsencesByGroup_Admin_GroupAndSubGroups_NotTheOthers()
    {
        var s = await SetupAsync("chart-abs-admin");

        (await AbsenceUsersAsync(s.Owner, s.Slug)).Should().HaveCount(6);
        (await AbsenceUsersAsync(s.Owner, s.Slug, s.Dept)).Should().BeEquivalentTo([s.DoraId, s.LeaId, s.AnaId, s.BenId]);
        (await AbsenceUsersAsync(s.Owner, s.Slug, s.Area)).Should().BeEquivalentTo([s.LeaId, s.AnaId, s.BenId]);
        (await AbsenceUsersAsync(s.Owner, s.Slug, s.Team)).Should().BeEquivalentTo([s.AnaId, s.BenId]);
        (await AbsenceUsersAsync(s.Owner, s.Slug, s.Other)).Should().BeEquivalentTo([s.AnaId, s.XaverId]);
    }

    [Fact]
    public async Task AbsencesByGroup_Member_NeverHiddenPeople_NorPeopleWhoseGroupsAreHidden()
    {
        var s = await SetupAsync("chart-abs-member");
        await SettingsAsync(s, ("Member", true, false), ("Vacation", true, true), ("Groups", true, false));

        // Unfiltered: Xaver and Cleo are hidden people, whatever the vacation column says
        (await AbsenceUsersAsync(s.Ben, s.Slug)).Should().BeEquivalentTo([s.BenId, s.LeaId, s.AnaId, s.DoraId]);
        (await AbsenceUsersAsync(s.Ben, s.Slug, s.Other)).Should().BeEquivalentTo([s.AnaId], "Xaver is hidden");
        (await AbsenceUsersAsync(s.Ben, s.Slug, s.Dept)).Should().BeEquivalentTo([s.DoraId, s.LeaId, s.AnaId, s.BenId]);

        // Dept (and Area/Team, inheriting) hide their people's groups: the filter must not reveal anyone's membership
        // but Ben's own (Ana's "Other" has no override, so the inherited deny wins for her too)
        await GroupVisibilityAsync(s, s.Dept, ("Groups", false, null));
        (await AbsenceUsersAsync(s.Ben, s.Slug, s.Dept)).Should().BeEquivalentTo([s.BenId]);
        (await AbsenceUsersAsync(s.Ben, s.Slug, s.Area)).Should().BeEquivalentTo([s.BenId]);
        (await AbsenceUsersAsync(s.Ben, s.Slug)).Should().BeEquivalentTo([s.BenId, s.LeaId, s.AnaId, s.DoraId], "without the filter their absences stay visible");
    }

    [Fact]
    public async Task AbsencesByGroup_UnknownGroup_404()
    {
        var s = await SetupAsync("chart-abs-404");
        var (foreignOwner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "chart-abs-404-foreign@chart.test");
        var foreignOrg = await TestHelpers.CreateOrganizationAsync(foreignOwner, "Foreign", "chart-abs-404-foreign");
        var foreignGroup = await TestHelpers.CreateGroupAsync(foreignOwner, foreignOrg.Slug, "Elsewhere", new { });

        foreach (var id in new[] { 999999, foreignGroup })
        {
            var response = await s.Owner.GetAsync($"/api/v1/Organizations/{s.Slug}/absences?groupId={id}");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("group.notFound");
        }
    }
}
