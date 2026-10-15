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
/// The member visibility matrix (issue #60): what a normal member sees about people in and outside
/// their groups, the data owner's group overrides, group admin rights, and that hidden people are
/// left out of every list and count and are a 404 when asked for directly.
/// </summary>
public class MemberVisibilityTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public MemberVisibilityTests(TimeTrackingApiFactory factory) => _factory = factory;

    /// <summary>Viewer and colleague share "Team"; the outsider is in "Other"; the owner in no group.</summary>
    private sealed record Setup(
        HttpClient Owner, int OwnerId, string Slug, int OrgId,
        HttpClient Viewer, int ViewerId, HttpClient Colleague, int ColleagueId, HttpClient Outsider, int OutsiderId,
        int TeamId, int OtherId);

    private async Task<Setup> SetupAsync(string prefix, params (string Kind, bool? In, bool? Out)[] matrix)
    {
        var (owner, o) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-owner@vis.test", "Olga", "Owner");
        var org = await TestHelpers.CreateOrganizationAsync(owner, $"{prefix} Org", $"{prefix}-org");
        async Task<(HttpClient, int)> Member(string name)
        {
            var (client, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-{name.ToLowerInvariant()}@vis.test", name, "Test");
            (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, login)).EnsureSuccessStatusCode();
            return (client, login.User.Id);
        }
        var (viewer, viewerId) = await Member("Viola");
        var (colleague, colleagueId) = await Member("Carl");
        var (outsider, outsiderId) = await Member("Xaver");
        var team = await TestHelpers.CreateGroupAsync(owner, org.Slug, "Team", new { }, (viewerId, "Member"), (colleagueId, "Member"));
        var other = await TestHelpers.CreateGroupAsync(owner, org.Slug, "Other", new { }, (outsiderId, "Member"));
        var setup = new Setup(owner, o.User.Id, org.Slug, org.Id, viewer, viewerId, colleague, colleagueId, outsider, outsiderId, team, other);
        if (matrix.Length > 0) await SettingsAsync(setup, matrix);
        return setup;
    }

    private static async Task SettingsAsync(Setup s, params (string Kind, bool? In, bool? Out)[] rows) =>
        (await s.Owner.PutAsJsonAsync($"/api/v1/Organizations/{s.Slug}/settings", new { visibility = TestHelpers.Matrix(rows) })).EnsureSuccessStatusCode();

    private static async Task GroupAsync(Setup s, int groupId, params (string Kind, bool? In, bool? Out)[] rows) =>
        (await s.Owner.PutAsJsonAsync($"/api/v1/Organizations/{s.Slug}/groups/{groupId}/permissions", new { visibility = TestHelpers.Matrix(rows) })).EnsureSuccessStatusCode();

    private async Task<MemberVisibility> VisibilityAsync(Setup s, int callerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
        var org = await db.Organizations.AsNoTracking().FirstAsync(o => o.Id == s.OrgId);
        return await MemberVisibility.ForCallerAsync(db, org, callerId);
    }

    private static async Task<OrganizationDetailResponseDto> DetailAsync(HttpClient client, string slug) =>
        (await client.GetFromJsonAsync<OrganizationDetailResponseDto>($"/api/v1/Organizations/{slug}", TestHelpers.JsonOptions))!;

    // ── The rule ───────────────────────────────────────────────────────

    [Fact]
    public void Combine_GroupOverrideReplacesOrgDefault_MostPermissiveWins_HiddenMemberHidesAll()
    {
        const MemberInfo org = MemberInfo.Member | MemberInfo.Vacation;
        MemberVisibility.Combine(org, []).Should().Be(org);                                                          // no override
        MemberVisibility.Combine(org, [(MemberInfo.None, MemberInfo.Vacation)]).Should().Be(MemberInfo.Member);      // stricter
        MemberVisibility.Combine(org, [(MemberInfo.SickDays, MemberInfo.None)]).Should().Be(org | MemberInfo.SickDays); // looser
        MemberVisibility.Combine(org, [(MemberInfo.None, MemberInfo.Vacation), (MemberInfo.Vacation, MemberInfo.None), (MemberInfo.None, MemberInfo.None)])
            .Should().Be(org);                                                                                       // most permissive group
        MemberVisibility.Combine(org, [(MemberInfo.None, MemberInfo.Member)]).Should().Be(MemberInfo.None);          // no person, no details
        MemberVisibility.Combine(MemberInfo.Vacation, []).Should().Be(MemberInfo.None);
    }

    [Fact]
    public async Task EveryKind_InAndOutsideGroups_OrgDefaultAndDataOwnersGroupOverride()
    {
        var s = await SetupAsync("vis-kinds", ("Member", true, true));
        foreach (var kind in MemberVisibility.Kinds.Where(k => k != MemberInfo.Member))
        {
            var name = kind.ToString();

            await SettingsAsync(s, (name, true, false));
            var v = await VisibilityAsync(s, s.ViewerId);
            v.Can(s.ColleagueId, kind).Should().BeTrue($"{name} is visible in own groups");
            v.Can(s.OutsiderId, kind).Should().BeFalse($"{name} is hidden outside own groups");

            await SettingsAsync(s, (name, false, true));
            v = await VisibilityAsync(s, s.ViewerId);
            v.Can(s.ColleagueId, kind).Should().BeFalse($"{name} is hidden in own groups");
            v.Can(s.OutsiderId, kind).Should().BeTrue($"{name} is visible outside own groups");

            // The data owner's groups decide: Team (the colleague's) allows, Other (the outsider's) denies
            await GroupAsync(s, s.TeamId, (name, true, null));
            await GroupAsync(s, s.OtherId, (name, null, false));
            v = await VisibilityAsync(s, s.ViewerId);
            v.Can(s.ColleagueId, kind).Should().BeTrue($"Team allows {name} in groups");
            v.Can(s.OutsiderId, kind).Should().BeFalse($"Other denies {name} outside groups");

            // Back to inherit
            await GroupAsync(s, s.TeamId, (name, null, null));
            await GroupAsync(s, s.OtherId, (name, null, null));
            v = await VisibilityAsync(s, s.ViewerId);
            v.Can(s.ColleagueId, kind).Should().BeFalse();
            v.Can(s.OutsiderId, kind).Should().BeTrue();
        }
    }

    [Fact]
    public async Task HiddenPerson_HidesAllDetails_SelfAndAdminsSeeEverything()
    {
        var s = await SetupAsync("vis-member", ("Member", true, false), ("Vacation", true, true), ("TrackedTime", true, true));
        var v = await VisibilityAsync(s, s.ViewerId);
        v.Of(s.OutsiderId).Should().Be(MemberInfo.None);
        v.Of(s.OwnerId).Should().Be(MemberInfo.None);
        v.Can(s.ColleagueId, MemberInfo.Vacation | MemberInfo.TrackedTime).Should().BeTrue();
        v.Of(s.ViewerId).Should().Be(MemberVisibility.AllKinds);

        // A group may make its members visible outside it
        await GroupAsync(s, s.OtherId, ("Member", null, true));
        (await VisibilityAsync(s, s.ViewerId)).Can(s.OutsiderId, MemberInfo.Member | MemberInfo.Vacation).Should().BeTrue();

        // Admins: everything, tracked times only while the org lets admins see members' hours
        var owner = await VisibilityAsync(s, s.OwnerId);
        owner.CallerIsOrgAdmin.Should().BeTrue();
        owner.Of(s.OutsiderId).Should().Be(MemberVisibility.AllKinds & ~MemberInfo.TrackedTime);
        (await s.Owner.PutAsJsonAsync($"/api/v1/Organizations/{s.Slug}/settings", new { memberTimeEntryVisibility = true })).EnsureSuccessStatusCode();
        (await VisibilityAsync(s, s.OwnerId)).Of(s.OutsiderId).Should().Be(MemberVisibility.AllKinds);
    }

    // ── Enforcement ────────────────────────────────────────────────────

    [Fact]
    public async Task MemberLists_OmitHiddenPeople_CountsFollow_AndDirectRequestsAre404()
    {
        var s = await SetupAsync("vis-lists", ("Member", true, false), ("Email", false, false));

        var detail = await DetailAsync(s.Viewer, s.Slug);
        detail.Members.Select(m => m.Id).Should().BeEquivalentTo([s.ViewerId, s.ColleagueId]);
        detail.Members.Single(m => m.Id == s.ColleagueId).Email.Should().BeNull();
        detail.Members.Single(m => m.Id == s.ViewerId).Email.Should().NotBeNull();
        detail.Members.Single(m => m.Id == s.ColleagueId).VisibleInfo.Should().Contain("Groups").And.NotContain("Email");
        (await DetailAsync(s.Owner, s.Slug)).Members.Should().HaveCount(4);

        var mine = await s.Viewer.GetFromJsonAsync<List<UserOrganizationResponseDto>>("/api/v1/Organizations/mine", TestHelpers.JsonOptions);
        mine!.Single(o => o.Slug == s.Slug).MemberCount.Should().Be(2);
        var directory = await s.Viewer.GetFromJsonAsync<JsonElement>("/api/v1/Organizations?limit=200");
        directory.GetProperty("items").EnumerateArray().Single(o => o.GetProperty("slug").GetString() == s.Slug)
            .GetProperty("memberCount").GetInt32().Should().Be(2);

        // Group lists: the outsider's group shows no members and counts none
        var groups = await s.Viewer.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{s.Slug}/groups");
        groups.EnumerateArray().Single(g => g.GetProperty("id").GetInt32() == s.OtherId).GetProperty("memberCount").GetInt32().Should().Be(0);
        var other = await s.Viewer.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{s.Slug}/groups/{s.OtherId}");
        other.GetProperty("members").GetArrayLength().Should().Be(0);
        var team = await s.Viewer.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{s.Slug}/groups/{s.TeamId}");
        team.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("userId").GetInt32() == s.ColleagueId)
            .GetProperty("email").ValueKind.Should().Be(JsonValueKind.Null);

        // Asking for a hidden person directly: 404, as if they were not there
        var org = $"/api/v1/organizations/{s.Slug}";
        foreach (var url in new[]
                 {
                     $"{org}/members/{s.OutsiderId}/balance",
                     $"{org}/members/{s.OutsiderId}/reports/monthly?year=2026&month=1",
                     $"{org}/members/{s.OutsiderId}/reports/yearly?year=2026",
                     $"{org}/members/{s.OutsiderId}/compliance",
                     $"{org}/members/{s.OutsiderId}/work-schedule",
                     $"{org}/members/{s.OutsiderId}/work-schedules",
                     $"/api/v1/Organizations/{s.Slug}/member-entries/{s.OutsiderId}",
                     $"{org}/absences?userId={s.OutsiderId}"
                 })
            (await s.Viewer.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound, url);
        (await s.Viewer.PostAsJsonAsync("/api/v1/ExportImport/export", new { type = "entries", organizationSlug = s.Slug, targetUserId = s.OutsiderId }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // A visible person whose tracked times / schedule are hidden: 403
        (await s.Viewer.GetAsync($"{org}/members/{s.ColleagueId}/balance")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.Viewer.GetAsync($"{org}/members/{s.ColleagueId}/work-schedule")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.Viewer.GetAsync($"/api/v1/Organizations/{s.Slug}/time-overview")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OrgRole_IsShownWithThePerson_DirectoryHidesForeignCounts()
    {
        var s = await SetupAsync("vis-role", ("Member", true, true), ("Groups", false, false));

        // Groups are hidden, the org role is not (#63)
        var colleague = (await DetailAsync(s.Viewer, s.Slug)).Members.Single(m => m.Id == s.ColleagueId);
        colleague.VisibleInfo.Should().NotContain("Groups");
        colleague.Groups.Should().BeEmpty();
        colleague.Role.Should().Be("Member");
        (await DetailAsync(s.Viewer, s.Slug)).Members.Single(m => m.Id == s.OwnerId).Role.Should().Be("Owner");

        // The directory shows member counts only for the caller's own organizations
        var (stranger, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "vis-role-stranger@vis.test");
        var directory = await stranger.GetFromJsonAsync<JsonElement>("/api/v1/Organizations?limit=200");
        directory.GetProperty("items").EnumerateArray().Single(o => o.GetProperty("slug").GetString() == s.Slug)
            .GetProperty("memberCount").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task TrackedTimesAndSchedule_OpenToGroupColleagues()
    {
        var s = await SetupAsync("vis-time", ("TrackedTime", true, false), ("WorkSchedule", true, false), ("Presence", true, true), ("Email", false, false));
        (await s.Colleague.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationId = s.OrgId })).EnsureSuccessStatusCode();
        (await s.Outsider.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationId = s.OrgId })).EnsureSuccessStatusCode();
        var org = $"/api/v1/organizations/{s.Slug}";

        (await s.Viewer.GetAsync($"{org}/members/{s.ColleagueId}/balance")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await s.Viewer.GetAsync($"{org}/members/{s.ColleagueId}/compliance")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await s.Viewer.GetAsync($"{org}/members/{s.ColleagueId}/work-schedule")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await s.Viewer.GetAsync($"{org}/members/{s.ColleagueId}/work-schedules")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await s.Viewer.GetAsync($"/api/v1/Organizations/{s.Slug}/member-entries/{s.ColleagueId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await s.Viewer.GetAsync($"{org}/members/{s.OutsiderId}/balance")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var overview = await s.Viewer.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{s.Slug}/time-overview");
        overview.EnumerateArray().Select(m => m.GetProperty("userId").GetInt32()).Should().BeEquivalentTo([s.ViewerId, s.ColleagueId]);
        overview.EnumerateArray().Single(m => m.GetProperty("userId").GetInt32() == s.ColleagueId)
            .GetProperty("email").ValueKind.Should().Be(JsonValueKind.Null);

        // Presence: running timers, visible in and outside here
        var members = (await DetailAsync(s.Viewer, s.Slug)).Members;
        members.Single(m => m.Id == s.ColleagueId).IsPresent.Should().BeTrue();
        members.Single(m => m.Id == s.OutsiderId).IsPresent.Should().BeTrue();
        members.Single(m => m.Id == s.ViewerId).IsPresent.Should().BeFalse();
        await SettingsAsync(s, ("Presence", true, false));
        (await DetailAsync(s.Viewer, s.Slug)).Members.Single(m => m.Id == s.OutsiderId).IsPresent.Should().BeNull();
    }

    [Fact]
    public async Task AbsencesCalendar_PerKind_NotesAndCreatorFollowTheMatrix()
    {
        var s = await SetupAsync("vis-abs", ("Member", true, false), ("Vacation", true, true), ("SickDays", false, false), ("AbsenceNotes", false, false));
        var url = $"/api/v1/organizations/{s.Slug}/absences";
        foreach (var (userId, date, type) in new[] { (s.ColleagueId, "2026-03-02", 1), (s.ColleagueId, "2026-03-03", 0), (s.OutsiderId, "2026-03-04", 1) })
            (await s.Owner.PostAsJsonAsync($"{url}/admin", new { userId, date, type, note = "secret" })).EnsureSuccessStatusCode();
        (await s.Viewer.PostAsJsonAsync(url, new { date = "2026-03-05", type = 0, note = "mine" })).EnsureSuccessStatusCode();

        var seen = (await s.Viewer.GetFromJsonAsync<PaginatedResponseDto<AbsenceDayResponseDto>>($"{url}?limit=200", TestHelpers.JsonOptions))!.Items;
        seen.Select(a => (a.UserId, a.Type)).Should().BeEquivalentTo([(s.ColleagueId, "Vacation"), (s.ViewerId, "SickDay")]);
        seen.Single(a => a.UserId == s.ColleagueId).Note.Should().BeNull();
        seen.Single(a => a.UserId == s.ColleagueId).CreatedByName.Should().BeNull();   // the owner is hidden from the viewer
        seen.Single(a => a.UserId == s.ViewerId).Note.Should().Be("mine");

        // Notes in own groups on; the owner shown again → their name too
        await SettingsAsync(s, ("AbsenceNotes", true, false), ("Member", true, true));
        seen = (await s.Viewer.GetFromJsonAsync<PaginatedResponseDto<AbsenceDayResponseDto>>($"{url}?userId={s.ColleagueId}", TestHelpers.JsonOptions))!.Items;
        seen.Single().Note.Should().Be("secret");
        seen.Single().CreatedByName.Should().Be("Olga Owner");

        // Admins see every absence with its note
        (await s.Owner.GetFromJsonAsync<PaginatedResponseDto<AbsenceDayResponseDto>>($"{url}?limit=200", TestHelpers.JsonOptions))!.Items
            .Should().HaveCount(4).And.OnlyContain(a => a.Note != null);
    }

    [Fact]
    public async Task GroupAdmins_KeepTheirCapabilityRights()
    {
        var s = await SetupAsync("vis-gadmin", ("Member", true, false), ("SickDays", false, false), ("TrackedTime", false, false));
        var (lead, l) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "vis-gadmin-lead@vis.test", "Lea", "Lead");
        (await TestHelpers.AddMemberAsync(_factory, s.Owner, s.Slug, l)).EnsureSuccessStatusCode();
        await TestHelpers.CreateGroupAsync(s.Owner, s.Slug, "Led",
            new { groupAdminCanApproveAbsences = true, groupAdminCanExportData = true, groupAdminCanManageMembers = true },
            (l.User.Id, "Admin"), (s.OutsiderId, "Member"));

        var v = await VisibilityAsync(s, l.User.Id);
        v.Can(s.OutsiderId, MemberInfo.Member | MemberInfo.Email | MemberInfo.Groups | MemberInfo.SickDays | MemberInfo.AbsenceNotes
                            | MemberInfo.TrackedTime | MemberInfo.WorkSchedule).Should().BeTrue();
        v.Can(s.OutsiderId, MemberInfo.Presence).Should().BeFalse();          // only with "view all time entries"
        v.Can(s.ColleagueId, MemberInfo.Member).Should().BeTrue();            // may manage members → sees whom to add
        v.Can(s.ColleagueId, MemberInfo.Email).Should().BeFalse();
        (await lead.GetAsync($"/api/v1/organizations/{s.Slug}/members/{s.OutsiderId}/balance")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Without member management the outsider-of-their-groups is hidden again
        var plain = await VisibilityAsync(s, s.ColleagueId);
        plain.Can(s.OutsiderId, MemberInfo.Member).Should().BeFalse();
    }

    [Fact]
    public async Task RequestAndHistoryTexts_DoNotNameHiddenPeople()
    {
        var s = await SetupAsync("vis-names", ("Member", true, false));
        (await s.Owner.PutAsJsonAsync($"/api/v1/Organizations/{s.Slug}/settings", new { initialOvertimeMode = 1, memberTimeEntryVisibility = true })).EnsureSuccessStatusCode();
        var request = await s.Viewer.PostAsJsonAsync($"/api/v1/organizations/{s.Slug}/requests", new { type = 3, requestData = "2" });
        var requestId = (await request.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await s.Owner.PutAsJsonAsync($"/api/v1/organizations/{s.Slug}/requests/{requestId}", new { accept = true })).EnsureSuccessStatusCode();

        var mine = await s.Viewer.GetFromJsonAsync<JsonElement>("/api/v1/organizations/my-requests");
        mine.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetInt32() == requestId)
            .GetProperty("respondedByName").ValueKind.Should().Be(JsonValueKind.Null);

        var added = await s.Owner.PostAsJsonAsync($"/api/v1/organizations/{s.Slug}/members/{s.ViewerId}/entries",
            new { startTime = new DateTime(2026, 2, 2, 7, 0, 0, DateTimeKind.Utc), endTime = new DateTime(2026, 2, 2, 11, 0, 0, DateTimeKind.Utc) });
        var entryId = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await s.Owner.PutAsJsonAsync($"/api/v1/organizations/{s.Slug}/members/{s.ViewerId}/entries/{entryId}", new { description = "fixed" })).EnsureSuccessStatusCode();
        var history = await s.Viewer.GetFromJsonAsync<JsonElement>($"/api/v1/TimeTracking/{entryId}/history");
        history.EnumerateArray().Should().NotBeEmpty()
            .And.OnlyContain(c => c.GetProperty("changedByName").ValueKind == JsonValueKind.Null);
        var ownerHistory = await s.Owner.GetFromJsonAsync<JsonElement>($"/api/v1/TimeTracking/{entryId}/history");
        ownerHistory.EnumerateArray().Should().Contain(c => c.GetProperty("changedByName").GetString() == "Olga Owner");
    }

    // ── Settings API ───────────────────────────────────────────────────

    [Fact]
    public async Task Settings_RoundTrip_GroupInheritAllowDeny_EffectivePermissions_AndUnknownKind()
    {
        var s = await SetupAsync("vis-api", ("Vacation", true, false));
        var detail = await s.Viewer.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{s.Slug}");
        var rows = detail.GetProperty("visibility").EnumerateArray().ToList();
        rows.Select(r => r.GetProperty("kind").GetString()).Should().Equal(MemberVisibility.Kinds.Select(k => k.ToString()));
        var vacation = rows.Single(r => r.GetProperty("kind").GetString() == "Vacation");
        (vacation.GetProperty("inGroups").GetBoolean(), vacation.GetProperty("outsideGroups").GetBoolean()).Should().Be((true, false));
        var member = rows.Single(r => r.GetProperty("kind").GetString() == "Member");
        (member.GetProperty("inGroups").GetBoolean(), member.GetProperty("outsideGroups").GetBoolean()).Should().Be((true, true));

        await GroupAsync(s, s.TeamId, ("SickDays", true, false));
        var perms = await s.Owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{s.Slug}/groups/{s.TeamId}/permissions");
        var sick = perms.GetProperty("visibility").EnumerateArray().Single(r => r.GetProperty("kind").GetString() == "SickDays");
        sick.GetProperty("inGroups").GetBoolean().Should().BeTrue();
        sick.GetProperty("outsideGroups").GetBoolean().Should().BeFalse();
        perms.GetProperty("visibility").EnumerateArray().Single(r => r.GetProperty("kind").GetString() == "Email")
            .GetProperty("inGroups").ValueKind.Should().Be(JsonValueKind.Null);

        // What normal members see about the colleague (the Team override applied)
        var effective = await s.Colleague.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{s.Slug}/effective-permissions");
        var effSick = effective.GetProperty("visibility").EnumerateArray().Single(r => r.GetProperty("kind").GetString() == "SickDays");
        (effSick.GetProperty("inGroups").GetBoolean(), effSick.GetProperty("outsideGroups").GetBoolean()).Should().Be((true, false));

        foreach (var bad in new[] { "Salary", "3", "Member, Email" })
        {
            var res = await s.Owner.PutAsJsonAsync($"/api/v1/Organizations/{s.Slug}/settings", new { visibility = TestHelpers.Matrix((bad, true, true)) });
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("visibility.unknownKind");
        }
        (await s.Owner.PutAsJsonAsync($"/api/v1/Organizations/{s.Slug}/groups/{s.TeamId}/permissions", new { visibility = TestHelpers.Matrix(("Nope", true, null)) }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await s.Viewer.PutAsJsonAsync($"/api/v1/Organizations/{s.Slug}/settings", new { visibility = TestHelpers.Matrix(("Vacation", true, true)) }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
