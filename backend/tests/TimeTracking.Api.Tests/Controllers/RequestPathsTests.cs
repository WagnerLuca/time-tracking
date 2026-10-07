using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Request service paths: who may ask, duplicate detection per type, declining with a reason,
/// approvals that cannot be applied, and routing to group admins.
/// </summary>
public class RequestPathsTests : IClassFixture<TimeTrackingApiFactory>
{
    private const int RequiresApproval = 1, Allowed = 2;
    private const int EditPastEntry = 1, EditPause = 2, AddManualEntry = 7, RequestAbsence = 6, WorkScheduleChange = 4;

    private readonly TimeTrackingApiFactory _factory;

    public RequestPathsTests(TimeTrackingApiFactory factory) => _factory = factory;

    private Task<(HttpClient Owner, HttpClient Member, string Slug, int OrgId, int MemberId)> SetupAsync(string prefix, object settings) =>
        TestHelpers.OrgWithMemberAsync(_factory, prefix, settings, "rp.test");

    private static async Task<int> TrackedEntryAsync(HttpClient client, int orgId)
    {
        (await client.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationId = orgId })).EnsureSuccessStatusCode();
        var stopped = await (await client.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null)).Content.ReadFromJsonAsync<JsonElement>();
        return stopped.GetProperty("id").GetInt32();
    }

    private static Task<HttpResponseMessage> AskAsync(HttpClient client, string slug, int type, string? requestData, int? relatedEntityId = null) =>
        client.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests", new { type, requestData, relatedEntityId });

    private static async Task<int> IdOf(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private static async Task<List<TimeEntryResponseDto>> EntriesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<PaginatedResponseDto<TimeEntryResponseDto>>("/api/v1/TimeTracking", TestHelpers.JsonOptions))!.Items;

    [Fact]
    public async Task OnlyMembers_AndOnlyForRulesThatNeedApproval()
    {
        var (_, member, slug, _, _) = await SetupAsync("rp-who", new { editPastEntriesMode = Allowed, editPauseMode = RequiresApproval });
        var (stranger, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "rp-who-x@rp.test");

        var notMember = await AskAsync(stranger, slug, EditPause, "10", 1);
        notMember.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await notMember.Content.ReadAsStringAsync()).Should().Contain("must be a member");

        var direct = await AskAsync(member, slug, EditPastEntry, "{}", 1);
        direct.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await direct.Content.ReadAsStringAsync()).Should().Contain("directly");

        (await member.PostAsJsonAsync("/api/v1/organizations/no-such-org/requests", new { type = EditPause, requestData = "1" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Duplicates_AreDetectedPerEntity_AndPerData()
    {
        var (_, member, slug, orgId, _) = await SetupAsync("rp-dup", new { editPauseMode = RequiresApproval, workScheduleChangeMode = RequiresApproval });
        var a = await TrackedEntryAsync(member, orgId);
        var b = await TrackedEntryAsync(member, orgId);

        await IdOf(await AskAsync(member, slug, EditPause, "15", a));
        (await AskAsync(member, slug, EditPause, "30", a)).StatusCode.Should().Be(HttpStatusCode.Conflict);   // same entry, other value
        await IdOf(await AskAsync(member, slug, EditPause, "15", b));                                          // other entry

        var schedule = JsonSerializer.Serialize(new { validFrom = "2026-01-01", weeklyWorkHours = 20 });
        await IdOf(await AskAsync(member, slug, WorkScheduleChange, schedule));
        (await AskAsync(member, slug, WorkScheduleChange, schedule)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await IdOf(await AskAsync(member, slug, WorkScheduleChange, JsonSerializer.Serialize(new { validFrom = "2026-02-01", weeklyWorkHours = 20 })));
    }

    [Fact]
    public async Task Decline_WithReason_IsStoredNotifiedAndMailed()
    {
        var (owner, member, slug, orgId, _) = await SetupAsync("rp-decline", new { editPauseMode = RequiresApproval });
        var entry = await TrackedEntryAsync(member, orgId);
        var id = await IdOf(await AskAsync(member, slug, EditPause, "45", entry));

        var declined = await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{id}", new { accept = false, reason = "  Pause was logged correctly.  " });
        declined.EnsureSuccessStatusCode();
        var body = await declined.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Declined");
        body.GetProperty("responseReason").GetString().Should().Be("Pause was logged correctly.");
        body.GetProperty("respondedByName").GetString().Should().Be("Test User");

        (await member.GetStringAsync("/api/v1/Notifications")).Should().Contain("Reason: Pause was logged correctly.");
        _factory.Mail.Sent.Should().Contain(m => m.To == "rp-decline-m@rp.test" && m.Subject.Contains("abgelehnt") && m.Body.Contains("Pause was logged correctly."));
        (await EntriesAsync(member)).Single(e => e.Id == entry).PauseDurationMinutes.Should().Be(0);

        // Answered once is answered
        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{id}", new { accept = true }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/999999", new { accept = true }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ApprovedPauseAndManualEntry_AreApplied()
    {
        var (owner, member, slug, orgId, _) = await SetupAsync("rp-apply", new { editPauseMode = RequiresApproval, editPastEntriesMode = RequiresApproval });
        var entry = await TrackedEntryAsync(member, orgId);
        var pause = await IdOf(await AskAsync(member, slug, EditPause, "20", entry));

        var start = new DateTime(2026, 3, 2, 8, 0, 0, DateTimeKind.Utc);
        var manual = await IdOf(await AskAsync(member, slug, AddManualEntry,
            JsonSerializer.Serialize(new { startTime = start, endTime = start.AddHours(4), pauseDurationMinutes = 10, description = "Workshop" })));

        foreach (var id in new[] { pause, manual })
            (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{id}", new { accept = true })).EnsureSuccessStatusCode();

        var entries = await EntriesAsync(member);
        entries.Single(e => e.Id == entry).PauseDurationMinutes.Should().Be(20);
        var added = entries.Single(e => e.Description == "Workshop");
        (added.StartTime.ToUniversalTime(), added.PauseDurationMinutes, added.NetDurationMinutes).Should().Be((start, 10, 230.0));
    }

    [Fact]
    public async Task ApprovalThatCannotBeApplied_LeavesTheRequestPending()
    {
        var (owner, member, slug, orgId, memberId) = await SetupAsync("rp-cannot", new { editPastEntriesMode = RequiresApproval });
        var url = $"/api/v1/organizations/{slug}/requests";
        var start = new DateTime(2026, 3, 3, 8, 0, 0, DateTimeKind.Utc);

        var overlapping = new[]
        {
            await IdOf(await AskAsync(member, slug, AddManualEntry, JsonSerializer.Serialize(new { startTime = start, endTime = start.AddHours(2) }))),
            await IdOf(await AskAsync(member, slug, AddManualEntry, JsonSerializer.Serialize(new { startTime = start.AddHours(1), endTime = start.AddHours(3) })))
        };
        (await owner.PutAsJsonAsync($"{url}/{overlapping[0]}", new { accept = true })).EnsureSuccessStatusCode();
        var refused = await owner.PutAsJsonAsync($"{url}/{overlapping[1]}", new { accept = true });
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("overlaps");

        var garbage = await IdOf(await AskAsync(member, slug, AddManualEntry, "not json"));
        (await (await owner.PutAsJsonAsync($"{url}/{garbage}", new { accept = true })).Content.ReadAsStringAsync()).Should().Contain("data is invalid");
        var tooLong = await IdOf(await AskAsync(member, slug, AddManualEntry,
            JsonSerializer.Serialize(new { startTime = start.AddDays(1), endTime = start.AddDays(1).AddHours(1), pauseDurationMinutes = 5000 })));
        (await owner.PutAsJsonAsync($"{url}/{tooLong}", new { accept = true })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The entry an edit request refers to was deleted meanwhile
        var entry = await TrackedEntryAsync(member, orgId);
        var edit = await IdOf(await AskAsync(member, slug, EditPastEntry, JsonSerializer.Serialize(new { description = "x" }), entry));
        (await owner.DeleteAsync($"/api/v1/organizations/{slug}/members/{memberId}/entries/{entry}")).IsSuccessStatusCode.Should().BeTrue();
        (await (await owner.PutAsJsonAsync($"{url}/{edit}", new { accept = true })).Content.ReadAsStringAsync()).Should().Contain("no longer exists");

        var pending = await owner.GetFromJsonAsync<PaginatedResponseDto<OrgRequestResponseDto>>($"{url}?status=0", TestHelpers.JsonOptions);
        pending!.Items.Select(r => r.Id).Should().BeEquivalentTo(new[] { overlapping[1], garbage, tooLong, edit });
        // ...and declining them still works
        (await owner.PutAsJsonAsync($"{url}/{edit}", new { accept = false })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Requests_AreRoutedToTheGroupAdminWithTheMatchingCapability()
    {
        var (owner, member, slug, orgId, memberId) = await SetupAsync("rp-route", new { editPauseMode = RequiresApproval, absenceSelfServiceMode = RequiresApproval });
        var (lead, leadLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "rp-route-lead@rp.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, leadLogin)).EnsureSuccessStatusCode();
        var groupId = await TestHelpers.CreateGroupAsync(owner, slug, "Team", new { groupAdminCanApproveAbsences = true },
            (memberId, "Member"), (leadLogin.User.Id, "Admin"));
        var url = $"/api/v1/organizations/{slug}/requests";

        var absence = await AskAsync(member, slug, RequestAbsence, JsonSerializer.Serialize(new { dates = new[] { "2026-11-02" }, type = 1 }));
        absence.StatusCode.Should().Be(HttpStatusCode.Created);
        var routed = await absence.Content.ReadFromJsonAsync<JsonElement>();
        var absenceId = routed.GetProperty("id").GetInt32();
        routed.GetProperty("groupId").GetInt32().Should().Be(groupId);
        routed.GetProperty("groupName").GetString().Should().Be("Team");
        _factory.Mail.Sent.Should().Contain(m => m.To == "rp-route-lead@rp.test" && m.Subject.Contains("New request"));

        // The pause request is not covered by the group's capability: it stays with the org admins
        var pauseId = await IdOf(await AskAsync(member, slug, EditPause, "5", await TrackedEntryAsync(member, orgId)));

        var leadSees = await lead.GetFromJsonAsync<PaginatedResponseDto<OrgRequestResponseDto>>(url, TestHelpers.JsonOptions);
        leadSees!.Items.Select(r => r.Id).Should().Equal(absenceId);
        var leadInbox = await lead.GetFromJsonAsync<AdminNotificationResponseDto>("/api/v1/organizations/notifications", TestHelpers.JsonOptions);
        leadInbox!.Requests.Select(r => r.Id).Should().Equal(absenceId);
        (await member.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var forbidden = await lead.PutAsJsonAsync($"{url}/{pauseId}", new { accept = true });
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await forbidden.Content.ReadAsStringAsync()).Should().Contain("request.cannotRespond");

        var approved = await lead.PutAsJsonAsync($"{url}/{absenceId}", new { accept = true });
        approved.EnsureSuccessStatusCode();
        (await approved.Content.ReadFromJsonAsync<OrgRequestResponseDto>(TestHelpers.JsonOptions))!.RespondedByName.Should().Be("Test User");

        // Losing the capability ends the group admin's say over new requests
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/permissions", new { groupAdminCanApproveAbsences = false })).EnsureSuccessStatusCode();
        var later = await IdOf(await AskAsync(member, slug, RequestAbsence, JsonSerializer.Serialize(new { dates = new[] { "2026-11-09" }, type = 1 })));
        (await lead.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await lead.PutAsJsonAsync($"{url}/{later}", new { accept = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PutAsJsonAsync($"{url}/{later}", new { accept = true })).EnsureSuccessStatusCode();
    }
}
