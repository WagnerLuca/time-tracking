using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>Regression tests for the findings of the 2026-10-01 security review (issue #36).</summary>
public class SecurityRegressionTests : IClassFixture<TimeTrackingApiFactory>
{
    private const int Disabled = 0, Allowed = 2;

    private readonly TimeTrackingApiFactory _factory;

    public SecurityRegressionTests(TimeTrackingApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Owner, string Slug, int OrgId, HttpClient Member, LoginResponseDto MemberLogin)> OrgWithMemberAsync(
        string prefix, object? settings = null)
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-owner@sec2.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, $"{prefix} Org", $"{prefix}-org");
        if (settings != null)
            (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", settings)).EnsureSuccessStatusCode();
        var (member, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-m@sec2.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, login)).EnsureSuccessStatusCode();
        return (owner, org.Slug, org.Id, member, login);
    }

    private static async Task<int> TrackAsync(HttpClient client, int? orgId, string description = "work")
    {
        (await client.PostAsJsonAsync("/api/v1/TimeTracking/start", new { description, organizationId = orgId })).EnsureSuccessStatusCode();
        return (await (await client.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null)).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task PersonalEntry_CannotBeMovedIntoAnOrganizationThatForbidsEditing()
    {
        var (_, _, orgId, member, _) = await OrgWithMemberAsync("sec-move", new { editPastEntriesMode = Disabled });
        var personal = await TrackAsync(member, null);

        var move = await member.PutAsJsonAsync($"/api/v1/TimeTracking/{personal}", new
        {
            startTime = "2026-09-01T06:00:00Z", endTime = "2026-09-01T18:00:00Z", organizationId = orgId
        });
        move.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("0001-01-01T08:00:00Z", "0001-01-01T09:00:00Z")]   // absurd date → endless report loops
    [InlineData("2026-03-02T08:00:00Z", "2026-03-04T09:00:00Z")]   // longer than a day
    public async Task ImplausibleEntryTimes_AreRejected(string start, string end)
    {
        var (_, _, orgId, member, _) = await OrgWithMemberAsync($"sec-times{start[..4]}{end[8..10]}", new { editPastEntriesMode = Allowed });
        var id = await TrackAsync(member, orgId);
        (await member.PutAsJsonAsync($"/api/v1/TimeTracking/{id}", new { startTime = start, endTime = end }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task FormerMember_CannotChangeOldEntriesOfTheOrganization()
    {
        var (owner, slug, orgId, member, login) = await OrgWithMemberAsync("sec-former", new { editPastEntriesMode = Allowed });
        var id = await TrackAsync(member, orgId);
        (await owner.DeleteAsync($"/api/v1/Organizations/{slug}/members/{login.User.Id}")).EnsureSuccessStatusCode();

        (await member.PutAsJsonAsync($"/api/v1/TimeTracking/{id}", new { description = "rewritten" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.DeleteAsync($"/api/v1/TimeTracking/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CsvExport_NeutralisesSpreadsheetFormulas()
    {
        var (_, slug, orgId, member, _) = await OrgWithMemberAsync("sec-csv");
        await TrackAsync(member, orgId, "=HYPERLINK(\"https://evil.example\",\"x\")");

        var csv = await (await member.PostAsJsonAsync("/api/v1/ExportImport/export", new { type = "entries", organizationSlug = slug }))
            .Content.ReadAsStringAsync();
        csv.Should().Contain("'=HYPERLINK").And.NotContain(",=HYPERLINK");
    }

    [Fact]
    public async Task AdminExportOfOthers_FollowsTheTimeEntryVisibilitySetting()
    {
        var (owner, slug, orgId, member, login) = await OrgWithMemberAsync("sec-vis");
        await TrackAsync(member, orgId);
        var body = new { type = "entries", organizationSlug = slug, targetUserId = login.User.Id };

        (await owner.PostAsJsonAsync("/api/v1/ExportImport/export", body)).IsSuccessStatusCode.Should().BeFalse();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { memberTimeEntryVisibility = true })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync("/api/v1/ExportImport/export", body)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task TerminalOfOneOrganization_DoesNotTouchTimersElsewhere()
    {
        var (owner, slug, _, member, login) = await OrgWithMemberAsync("sec-punch");
        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/members/{login.User.Id}/badge", new { badgeId = "SEC-1" })).EnsureSuccessStatusCode();
        var key = (await (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/api-keys", new { name = "Door" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString();

        // A personal timer is running
        (await member.PostAsJsonAsync("/api/v1/TimeTracking/start", new { description = "private" })).EnsureSuccessStatusCode();

        var terminal = _factory.CreateClient();
        terminal.DefaultRequestHeaders.Add("X-Api-Key", key);
        var punch = await terminal.PostAsJsonAsync("/api/v1/terminal/punch", new { badgeId = "SEC-1" });
        punch.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await punch.Content.ReadAsStringAsync()).Should().NotContain("private");

        var current = await member.GetFromJsonAsync<JsonElement>("/api/v1/TimeTracking/current");
        current.GetProperty("description").GetString().Should().Be("private");
    }

    [Fact]
    public async Task RemovedGroupAdmin_LosesAccessToPendingRequests()
    {
        var (owner, slug, _, groupAdmin, gaLogin) = await OrgWithMemberAsync("sec-ga", new { editPastEntriesMode = 1 /* RequiresApproval */ });
        var (requester, reqLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "sec-ga-req@sec2.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, reqLogin)).EnsureSuccessStatusCode();

        var groupId = (await (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups", new { name = "Team" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/members", new { userId = gaLogin.User.Id, role = "Admin" })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/members", new { userId = reqLogin.User.Id })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/permissions", new { groupAdminCanApproveTimeEdits = true })).EnsureSuccessStatusCode();

        var request = await requester.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests",
            new { type = 1, relatedEntityId = 0, requestData = "{}" });
        request.EnsureSuccessStatusCode();
        var requestId = (await request.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        (await groupAdmin.GetFromJsonAsync<JsonElement>("/api/v1/organizations/notifications"))
            .GetProperty("pendingRequests").GetInt32().Should().Be(1);

        (await owner.DeleteAsync($"/api/v1/Organizations/{slug}/members/{gaLogin.User.Id}")).EnsureSuccessStatusCode();

        (await groupAdmin.GetFromJsonAsync<JsonElement>("/api/v1/organizations/notifications"))
            .GetProperty("pendingRequests").GetInt32().Should().Be(0);
        (await groupAdmin.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{requestId}", new { accept = false }))
            .IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task GroupAdmin_CannotPullOrgAdminsIntoTheirGroup()
    {
        var (owner, slug, _, groupAdmin, gaLogin) = await OrgWithMemberAsync("sec-pull");
        var (_, adminLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "sec-pull-admin@sec2.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, adminLogin, role: 1)).EnsureSuccessStatusCode();

        var groupId = (await (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups", new { name = "Spy" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/members", new { userId = gaLogin.User.Id, role = "Admin" })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/permissions", new { groupAdminCanManageMembers = true })).EnsureSuccessStatusCode();

        (await groupAdmin.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/members", new { userId = adminLogin.User.Id }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Member_CannotDeleteAnAbsenceAnAdminEntered()
    {
        var (owner, slug, _, member, login) = await OrgWithMemberAsync("sec-abs");
        var created = await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences/admin",
            new { userId = login.User.Id, date = "2026-05-04", type = 0 });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        (await member.DeleteAsync($"/api/v1/organizations/{slug}/absences/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.DeleteAsync($"/api/v1/organizations/{slug}/absences/{id}")).IsSuccessStatusCode.Should().BeTrue();
    }
}
