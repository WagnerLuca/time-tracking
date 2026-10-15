using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Sub-groups (issue #59): settings inherit along the parent chain (an override in the middle wins,
/// most permissive across branches), parent admins act for sub-group members, routing and
/// notifications go up the chain, the matrix's "own groups" spans the hierarchy, and the tree's
/// structure rules (cycles, 3 levels, deleting a parent).
/// </summary>
public class SubGroupTests : IClassFixture<TimeTrackingApiFactory>
{
    private const int Disabled = 0, RequiresApproval = 1, Allowed = 2;
    private const int RequestAbsence = 6;
    private readonly TimeTrackingApiFactory _factory;

    public SubGroupTests(TimeTrackingApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Owner, string Slug, int OrgId)> OrgAsync(string prefix, object settings)
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-owner@sub.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, $"{prefix} Org", $"{prefix}-org");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", settings)).EnsureSuccessStatusCode();
        return (owner, org.Slug, org.Id);
    }

    private async Task<(HttpClient Client, int Id)> MemberAsync(HttpClient owner, string slug, string email, string first = "Test", string last = "User")
    {
        var (client, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, email, first, last);
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, login)).EnsureSuccessStatusCode();
        return (client, login.User.Id);
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string slug, string name, int? parentGroupId) =>
        client.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups", new { name, parentGroupId });

    private static async Task<int> GroupAsync(HttpClient owner, string slug, string name, int? parentGroupId, object permissions, params (int UserId, string Role)[] members)
    {
        var created = await CreateAsync(owner, slug, name, parentGroupId);
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        foreach (var (userId, role) in members)
            (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{id}/members", new { userId, role })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{id}/permissions", permissions)).EnsureSuccessStatusCode();
        return id;
    }

    private static async Task<string> EffectiveAsync(HttpClient member, string slug, string key) =>
        (await member.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/effective-permissions"))!.GetProperty(key).ToString();

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static Task<HttpResponseMessage> RequestAbsenceAsync(HttpClient member, string slug, string date) =>
        member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests",
            new { type = RequestAbsence, requestData = JsonSerializer.Serialize(new { dates = new[] { date }, type = 1 }) });

    // ── Pure tree rules ─────────────────────────────────────────────────

    private static Group G(int id, int? parent, Action<Group>? set = null, params (int UserId, GroupRole Role)[] members)
    {
        var g = new Group { Id = id, Name = $"g{id}", ParentGroupId = parent };
        foreach (var (userId, role) in members) g.Members.Add(new GroupMember { GroupId = id, UserId = userId, Role = role });
        set?.Invoke(g);
        return g;
    }

    [Fact]
    public void Tree_ResolvesEveryKindOfSetting_AlongTheChain()
    {
        var tree = new GroupTree([
            G(1, null, g =>
            {
                g.EditPauseMode = RuleMode.Allowed;
                g.VisibilityAllowInGroups = MemberInfo.Vacation | MemberInfo.SickDays;
                g.VisibilityDenyOutsideGroups = MemberInfo.Email;
                g.CoreHoursMode = CoreHoursMode.Own; g.CoreHoursStart = new TimeOnly(9, 0); g.CoreHoursEnd = new TimeOnly(14, 0);
                g.GroupAdminCanExportData = true;
            }),
            G(2, 1, g => { g.VisibilityDenyInGroups = MemberInfo.SickDays; g.GroupAdminCanExportData = false; }),
            G(3, 2, g => g.EditPauseMode = RuleMode.Disabled, (7, GroupRole.Member)),
        ]);

        var leaf = tree.Effective(3);
        leaf.EditPauseMode.Should().Be(RuleMode.Disabled);
        tree.Effective(2).EditPauseMode.Should().Be(RuleMode.Allowed);
        leaf.VisibilityAllowInGroups.Should().Be(MemberInfo.Vacation);          // sick days: denied in the middle
        leaf.VisibilityDenyInGroups.Should().Be(MemberInfo.SickDays);
        leaf.VisibilityDenyOutsideGroups.Should().Be(MemberInfo.Email);
        (leaf.CoreHoursMode, leaf.CoreHoursEnd).Should().Be((CoreHoursMode.Own, new TimeOnly(14, 0)));
        leaf.GroupAdminCanExportData.Should().BeFalse();
        tree.InheritedFrom(3)!.Id.Should().Be(2);
        tree.InheritedFrom(1).Should().BeNull();
        tree.Chain(3).Should().Equal(3, 2, 1);
        tree.Subtree(1).Should().BeEquivalentTo([1, 2, 3]);
        tree.ChainGroupsOf(7).Should().BeEquivalentTo([1, 2, 3]);
        (tree.Level(3), tree.Height(1)).Should().Be((3, 3));
        EffectiveRuleResolver.CombineCoreHours(new Organization { Name = "o", Slug = "o" }, tree.EffectiveGroupsOf(7))
            .Should().Be(new CoreWindow(new TimeOnly(9, 0), new TimeOnly(14, 0)));
    }

    [Fact]
    public void Tree_StructureChecks_AndACycleInTheData_IsCut()
    {
        var tree = new GroupTree([G(1, null), G(2, 1), G(3, 2), G(4, null), G(5, 4)]);
        tree.CheckPlacement(null, 3).Should().Be(GroupTree.TooDeepMessage);
        tree.CheckPlacement(null, 2).Should().BeNull();
        tree.CheckPlacement(1, 3).Should().Be(GroupTree.CycleMessage);
        tree.CheckPlacement(1, 1).Should().Be(GroupTree.CycleMessage);
        tree.CheckPlacement(4, 2).Should().Be(GroupTree.TooDeepMessage);   // a two-level subtree below level 2
        tree.CheckPlacement(4, 1).Should().BeNull();

        var broken = new GroupTree([G(1, 2), G(2, 1), G(3, 99)]);
        broken.Chain(1).Count().Should().BeLessThan(3);
        broken.ParentOf(3).Should().BeNull();                              // unknown/inactive parent = top level
        broken.Effective(1).Should().NotBeNull();
    }

    [Fact]
    public void Tree_Routing_TakesTheNearestCapableGroupWithAnAdmin()
    {
        var tree = new GroupTree([
            G(1, null, g => g.GroupAdminCanApproveAbsences = true, (100, GroupRole.Admin)),
            G(2, 1, null, (200, GroupRole.Admin)),                                                    // inherits approval, has an admin
            G(3, 2, g => g.GroupAdminCanApproveAbsences = false, (300, GroupRole.Admin), (7, GroupRole.Member)),
            G(4, null, g => g.GroupAdminCanApproveTimeEdits = true, (8, GroupRole.Member)),          // no admin
        ]);
        tree.RoutingGroupFor(7, RequestType.RequestAbsence).Should().Be(2);
        tree.RoutingGroupFor(7, RequestType.WorkScheduleChange).Should().BeNull();
        tree.RoutingGroupFor(8, RequestType.EditPastEntry).Should().BeNull();
        tree.RoutingGroupFor(7, RequestType.JoinOrganization).Should().BeNull();
        tree.ApproversOf(3, RequestType.RequestAbsence).Should().BeEquivalentTo([200, 100]);
        tree.ActsFor(100, 7, g => g.GroupAdminCanApproveAbsences == true).Should().BeTrue();
        tree.ActsFor(300, 7, g => g.GroupAdminCanApproveAbsences == true).Should().BeFalse();
        tree.ActsFor(300, 8, _ => true).Should().BeFalse();
    }

    // ── Rules ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Rules_InheritThroughThreeLevels_AndAnOverrideInTheMiddleWins()
    {
        var (owner, slug, _) = await OrgAsync("sub-inherit", new { editPastEntriesMode = Disabled });
        var (member, memberId) = await MemberAsync(owner, slug, "sub-inherit-m@sub.test");
        var dept = await GroupAsync(owner, slug, "Dept", null, new { editPastEntriesMode = Allowed });
        var team = await GroupAsync(owner, slug, "Team", dept, new { });
        await GroupAsync(owner, slug, "Squad", team, new { }, (memberId, "Member"));

        (await EffectiveAsync(member, slug, "editPastEntriesMode")).Should().Be("Allowed");

        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{team}/permissions", new { editPastEntriesMode = RequiresApproval }))
            .EnsureSuccessStatusCode();
        (await EffectiveAsync(member, slug, "editPastEntriesMode")).Should().Be("RequiresApproval");

        var perms = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}/groups/permissions");
        perms.EnumerateArray().Single(p => p.GetProperty("groupName").GetString() == "Squad")
            .GetProperty("parentGroupId").GetInt32().Should().Be(team);
    }

    [Fact]
    public async Task MostPermissiveAcrossBranches_UsesEachGroupsEffectiveValue()
    {
        var (owner, slug, _) = await OrgAsync("sub-branches", new { editPauseMode = Allowed });
        var (both, bothId) = await MemberAsync(owner, slug, "sub-branches-b@sub.test");
        var (one, oneId) = await MemberAsync(owner, slug, "sub-branches-o@sub.test");
        var strict = await GroupAsync(owner, slug, "Strict", null, new { editPauseMode = Disabled });
        var looser = await GroupAsync(owner, slug, "Looser", null, new { editPauseMode = RequiresApproval });
        await GroupAsync(owner, slug, "Strict team", strict, new { }, (bothId, "Member"), (oneId, "Member"));
        await GroupAsync(owner, slug, "Looser team", looser, new { }, (bothId, "Member"));

        (await EffectiveAsync(one, slug, "editPauseMode")).Should().Be("Disabled");
        (await EffectiveAsync(both, slug, "editPauseMode")).Should().Be("RequiresApproval");
    }

    [Fact]
    public async Task CapabilityFlags_CanBeResetToInherit()
    {
        var (owner, slug, _) = await OrgAsync("sub-caps", new { });
        var (lead, leadId) = await MemberAsync(owner, slug, "sub-caps-l@sub.test");
        var dept = await GroupAsync(owner, slug, "Dept", null, new { groupAdminCanExportData = true });
        var team = await GroupAsync(owner, slug, "Team", dept, new { groupAdminCanExportData = false }, (leadId, "Admin"));

        (await EffectiveAsync(lead, slug, "canExportData")).Should().Be("False");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{team}/permissions",
            new { inheritCapabilities = new[] { "groupAdminCanExportData" } })).EnsureSuccessStatusCode();
        (await EffectiveAsync(lead, slug, "canExportData")).Should().Be("True");

        var bad = await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{team}/permissions", new { inheritCapabilities = new[] { "nope" } });
        (await CodeAsync(bad)).Should().Be("group.unknownCapability");
    }

    // ── Structure ──────────────────────────────────────────────────────

    [Fact]
    public async Task CyclesAndMoreThanThreeLevels_AreRejected_WithErrorCodes()
    {
        var (owner, slug, _) = await OrgAsync("sub-struct", new { });
        var dept = await GroupAsync(owner, slug, "Dept", null, new { });
        var team = await GroupAsync(owner, slug, "Team", dept, new { });
        var squad = await GroupAsync(owner, slug, "Squad", team, new { });
        var other = await GroupAsync(owner, slug, "Other", null, new { });
        await GroupAsync(owner, slug, "Other team", other, new { });

        var tooDeep = await CreateAsync(owner, slug, "Too deep", squad);
        tooDeep.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(tooDeep)).Should().Be("group.tooDeep");

        var cycle = await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{dept}", new { name = "Dept", parentGroupId = squad });
        (await CodeAsync(cycle)).Should().Be("group.cycle");
        (await CodeAsync(await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{dept}", new { name = "Dept", parentGroupId = dept })))
            .Should().Be("group.cycle");
        // a two-level subtree below a second-level group would make four levels
        (await CodeAsync(await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{other}", new { name = "Other", parentGroupId = team })))
            .Should().Be("group.tooDeep");
        (await CodeAsync(await CreateAsync(owner, slug, "Nowhere", 999999))).Should().Be("group.parentNotFound");

        // A rename without parentGroupId keeps the parent; the top level is an explicit choice (#63)
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{team}", new { name = "Team" })).EnsureSuccessStatusCode();
        (await CodeAsync(await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{squad}", new { name = "Squad", parentGroupId = dept, moveToTopLevel = true })))
            .Should().Be("group.parentOrTopLevel");

        // a valid move: Squad up to the top level, then Other below Dept
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{squad}", new { name = "Squad", moveToTopLevel = true })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{other}", new { name = "Other", parentGroupId = dept })).EnsureSuccessStatusCode();
        var groups = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}/groups");
        int? Parent(int id) => groups.EnumerateArray().Single(g => g.GetProperty("id").GetInt32() == id).GetProperty("parentGroupId") is
            { ValueKind: JsonValueKind.Number } p ? p.GetInt32() : null;
        (Parent(squad), Parent(other), Parent(team)).Should().Be((null, dept, dept));
    }

    [Fact]
    public async Task DeletingAParent_AsksForTheSubGroups_MovesThemUp_OrDeletesThem()
    {
        var (owner, slug, _) = await OrgAsync("sub-delete", new { });
        var dept = await GroupAsync(owner, slug, "Dept", null, new { });
        var team = await GroupAsync(owner, slug, "Team", dept, new { });
        var squad = await GroupAsync(owner, slug, "Squad", team, new { });
        var lab = await GroupAsync(owner, slug, "Lab", null, new { });
        var labTeam = await GroupAsync(owner, slug, "Lab team", lab, new { });
        await GroupAsync(owner, slug, "Lab squad", labTeam, new { });

        var ask = await owner.DeleteAsync($"/api/v1/Organizations/{slug}/groups/{team}");
        ask.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(ask)).Should().Be("group.hasSubGroups");

        (await owner.DeleteAsync($"/api/v1/Organizations/{slug}/groups/{team}?subGroups=MoveUp")).EnsureSuccessStatusCode();
        (await owner.DeleteAsync($"/api/v1/Organizations/{slug}/groups/{lab}?subGroups=Delete")).EnsureSuccessStatusCode();

        var groups = (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}/groups")).EnumerateArray()
            .ToDictionary(g => g.GetProperty("name").GetString()!, g => g.GetProperty("parentGroupId"));
        groups.Keys.Should().BeEquivalentTo(["Dept", "Squad"]);
        groups["Squad"].GetInt32().Should().Be(dept);
        _ = squad;
    }

    // ── Group admins along the chain ───────────────────────────────────

    [Fact]
    public async Task Routing_GoesToTheNearestCapableAdmin_ThenUpTheChain_ThenTheOrgAdmins()
    {
        var (owner, slug, _) = await OrgAsync("sub-route", new { absenceSelfServiceMode = RequiresApproval });
        var (deptLead, deptLeadId) = await MemberAsync(owner, slug, "sub-route-d@sub.test");
        var (teamLead, teamLeadId) = await MemberAsync(owner, slug, "sub-route-t@sub.test");
        var (member, memberId) = await MemberAsync(owner, slug, "sub-route-m@sub.test", "Mia", "Member");
        var dept = await GroupAsync(owner, slug, "Dept", null, new { groupAdminCanApproveAbsences = true }, (deptLeadId, "Admin"));
        var team = await GroupAsync(owner, slug, "Team", dept, new { }, (memberId, "Member"));

        // No admin in the team: up to the department, whose admin sees and approves it
        var first = await (await RequestAbsenceAsync(member, slug, "2026-11-02")).Content.ReadFromJsonAsync<JsonElement>();
        first.GetProperty("groupId").GetInt32().Should().Be(dept);
        var listed = await deptLead.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/requests");
        listed.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetInt32()).Should().Contain(first.GetProperty("id").GetInt32());
        (await deptLead.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{first.GetProperty("id").GetInt32()}", new { accept = true }))
            .EnsureSuccessStatusCode();

        // A team admin (inheriting the approval): routed to the team; both admins along the chain are mailed once
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{team}/members", new { userId = teamLeadId, role = "Admin" })).EnsureSuccessStatusCode();
        var mailsBefore = _factory.Mail.Sent.Count;
        var second = await (await RequestAbsenceAsync(member, slug, "2026-11-03")).Content.ReadFromJsonAsync<JsonElement>();
        second.GetProperty("groupId").GetInt32().Should().Be(team);
        var mails = _factory.Mail.Sent.Skip(mailsBefore).Where(m => m.Body.Contains("Mia Member")).Select(m => m.To).ToList();
        mails.Should().Contain(["sub-route-d@sub.test", "sub-route-t@sub.test", "sub-route-owner@sub.test"]).And.OnlyHaveUniqueItems();
        var pending = await deptLead.GetFromJsonAsync<JsonElement>("/api/v1/organizations/notifications");
        pending.GetProperty("requests").EnumerateArray().Select(r => r.GetProperty("id").GetInt32()).Should().Contain(second.GetProperty("id").GetInt32());
        (await deptLead.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{second.GetProperty("id").GetInt32()}", new { accept = false }))
            .EnsureSuccessStatusCode();

        // The team turns approval off: back up to the department; the team admin may not decide
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{team}/permissions", new { groupAdminCanApproveAbsences = false })).EnsureSuccessStatusCode();
        var third = await (await RequestAbsenceAsync(member, slug, "2026-11-04")).Content.ReadFromJsonAsync<JsonElement>();
        third.GetProperty("groupId").GetInt32().Should().Be(dept);
        (await teamLead.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{third.GetProperty("id").GetInt32()}", new { accept = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Nobody in the chain may approve: the org admins
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{dept}/permissions", new { groupAdminCanApproveAbsences = false })).EnsureSuccessStatusCode();
        var fourth = await (await RequestAbsenceAsync(member, slug, "2026-11-05")).Content.ReadFromJsonAsync<JsonElement>();
        fourth.GetProperty("groupId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ParentAdmin_ActsForSubGroupMembers_PerTheParentsCapabilities()
    {
        var (owner, slug, orgId) = await OrgAsync("sub-lead", new { });
        var (deptLead, deptLeadId) = await MemberAsync(owner, slug, "sub-lead-d@sub.test");
        var (teamLead, teamLeadId) = await MemberAsync(owner, slug, "sub-lead-t@sub.test");
        var (member, memberId) = await MemberAsync(owner, slug, "sub-lead-m@sub.test");
        var (_, deptOnlyId) = await MemberAsync(owner, slug, "sub-lead-x@sub.test");
        var (_, newcomerId) = await MemberAsync(owner, slug, "sub-lead-n@sub.test");
        var dept = await GroupAsync(owner, slug, "Dept", null, new
        {
            groupAdminCanViewAllTimeEntries = true, groupAdminCanExportData = true, groupAdminCanApproveTimeEdits = true, groupAdminCanManageMembers = true
        }, (deptLeadId, "Admin"), (deptOnlyId, "Member"));
        var team = await GroupAsync(owner, slug, "Team", dept, new { groupAdminCanManageMembers = false }, (teamLeadId, "Admin"), (memberId, "Member"));

        var entry = await (await member.PostAsJsonAsync("/api/v1/TimeTracking/manual", new
        {
            organizationId = orgId, startTime = new DateTime(2026, 3, 2, 7, 0, 0, DateTimeKind.Utc), endTime = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc)
        })).Content.ReadFromJsonAsync<JsonElement>();

        var org = $"/api/v1/organizations/{slug}";
        (await deptLead.GetAsync($"/api/v1/Organizations/{slug}/member-entries/{memberId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await deptLead.GetAsync($"{org}/members/{memberId}/balance")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await deptLead.PostAsJsonAsync("/api/v1/ExportImport/export", new { type = "entries", organizationSlug = slug, targetUserId = memberId }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await deptLead.PutAsJsonAsync($"{org}/members/{memberId}/entries/{entry.GetProperty("entry").GetProperty("id").GetInt32()}", new { description = "fixed by dept" }))
            .EnsureSuccessStatusCode();
        var overview = await deptLead.GetFromJsonAsync<JsonElement>($"{org}/team-overview");
        overview.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("userId").GetInt32() == memberId)
            .GetProperty("groupIds").EnumerateArray().Select(g => g.GetInt32()).Should().Equal(team);

        // Member management through the parent: add to the team, create a sub-group below it
        (await deptLead.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{team}/members", new { userId = newcomerId })).EnsureSuccessStatusCode();
        (await CreateAsync(deptLead, slug, "Dept squad", team)).EnsureSuccessStatusCode();
        var groups = await deptLead.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}/groups");
        groups.EnumerateArray().Single(g => g.GetProperty("id").GetInt32() == team).GetProperty("canManageMembers").GetBoolean().Should().BeTrue();

        // The team admin (no member management, no viewing rights) and the child's admin over the parent's members
        (await CreateAsync(teamLead, slug, "Team squad", team)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CreateAsync(deptLead, slug, "Top", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await teamLead.GetAsync($"/api/v1/Organizations/{slug}/member-entries/{deptOnlyId}")).StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await EffectiveAsync(deptLead, slug, "canViewAllTimeEntries")).Should().Be("True");
        (await EffectiveAsync(teamLead, slug, "canManageGroupMembers")).Should().Be("False");
        (await EffectiveAsync(teamLead, slug, "canViewAllTimeEntries")).Should().Be("True");   // inherited from Dept
    }

    [Fact]
    public async Task Matrix_OwnGroups_SpanTheHierarchy_AndGroupOverridesInherit()
    {
        var (owner, slug, _) = await OrgAsync("sub-matrix", new { visibility = TestHelpers.Matrix(("Email", true, false), ("Vacation", true, false)) });
        var (viewer, viewerId) = await MemberAsync(owner, slug, "sub-matrix-v@sub.test");
        var (_, colleagueId) = await MemberAsync(owner, slug, "sub-matrix-c@sub.test");
        var (_, outsiderId) = await MemberAsync(owner, slug, "sub-matrix-o@sub.test");
        var dept = await GroupAsync(owner, slug, "Dept", null, new { visibility = TestHelpers.Matrix(("Vacation", false, null)) });
        await GroupAsync(owner, slug, "Kitchen", dept, new { }, (viewerId, "Member"));
        var service = await GroupAsync(owner, slug, "Service", dept, new { }, (colleagueId, "Member"));
        await GroupAsync(owner, slug, "Elsewhere", null, new { }, (outsiderId, "Member"));

        var detail = await viewer.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}");
        JsonElement Person(int id) => detail.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("id").GetInt32() == id);
        Person(colleagueId).GetProperty("email").GetString().Should().Be("sub-matrix-c@sub.test");      // shared ancestor Dept
        Person(outsiderId).GetProperty("email").ValueKind.Should().Be(JsonValueKind.Null);
        Person(colleagueId).GetProperty("visibleInfo").EnumerateArray().Select(k => k.GetString()).Should().NotContain("Vacation"); // Dept's deny, inherited

        var groups = Person(colleagueId).GetProperty("groups").EnumerateArray()
            .Select(g => (g.GetProperty("name").GetString(), g.GetProperty("viaGroupName").ToString())).ToList();
        groups.Should().Equal(("Service", ""), ("Dept", "Service"));

        var deptDetail = await viewer.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}/groups/{dept}");
        deptDetail.GetProperty("members").EnumerateArray()
            .Select(m => (m.GetProperty("userId").GetInt32(), m.GetProperty("viaGroupName").GetString()))
            .Should().BeEquivalentTo([(viewerId, "Kitchen"), (colleagueId, "Service")]);
        _ = service;
    }

    [Fact]
    public async Task CoreHoursViolation_NotifiesTheAdminsAlongTheChain_Once()
    {
        var (owner, slug, orgId) = await OrgAsync("sub-core", new { timeZone = "Europe/Berlin", memberTimeEntryVisibility = true });
        var (_, leadId) = await MemberAsync(owner, slug, "sub-core-l@sub.test");
        var (_, memberId) = await MemberAsync(owner, slug, "sub-core-m@sub.test");
        var dept = await GroupAsync(owner, slug, "Dept", null, new { coreHoursMode = 1, coreHoursStart = "09:00", coreHoursEnd = "15:00" }, (leadId, "Admin"));
        await GroupAsync(owner, slug, "Team", dept, new { }, (leadId, "Admin"), (memberId, "Member"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
        // As if the core hours and memberships had been in place before the checked day (the API stamps "now")
        (await db.Organizations.SingleAsync(o => o.Id == orgId)).CoreHoursChangedAt = null;
        foreach (var m in await db.UserOrganizations.Where(uo => uo.OrganizationId == orgId).ToListAsync())
            m.JoinedAt = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc);   // that day, not the catch-up days before
        await db.SaveChangesAsync();
        (await CoreHours.CheckDueAsync(db, new DateTime(2026, 3, 3, 15, 0, 0, DateTimeKind.Utc), _factory.Mail, "https://app.test"))
            .Should().BeGreaterThan(0);   // the window comes from Dept, inherited by Team
        var notices = await db.Notifications.AsNoTracking()
            .Where(n => n.OrganizationId == orgId && n.RelatedEntityId == memberId && n.Title == "Core working hours missed")
            .Select(n => n.UserId).ToListAsync();
        notices.Count(id => id == leadId).Should().Be(1);
        notices.Should().Contain(memberId);
    }
}
