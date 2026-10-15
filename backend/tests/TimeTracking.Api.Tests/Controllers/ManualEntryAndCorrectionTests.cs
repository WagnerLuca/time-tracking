using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Manual entries (direct / as request / forbidden), admin corrections of members' entries,
/// the edit history and the Working Time Act warnings.
/// </summary>
public class ManualEntryAndCorrectionTests : IClassFixture<TimeTrackingApiFactory>
{
    private const int Disabled = 0, RequiresApproval = 1;
    private readonly TimeTrackingApiFactory _factory;

    public ManualEntryAndCorrectionTests(TimeTrackingApiFactory factory) => _factory = factory;

    private static DateTime Utc(string s) => DateTime.Parse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);

    private async Task<(HttpClient Owner, HttpClient Member, int MemberId, string Slug, int OrgId)> SetupAsync(string prefix, object? settings = null)
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-owner@manual.test", "Olga", "Owner");
        var org = await TestHelpers.CreateOrganizationAsync(owner, $"{prefix} Org", $"{prefix}-org");
        if (settings != null)
            (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", settings)).EnsureSuccessStatusCode();
        var (member, m) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-m@manual.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, m)).EnsureSuccessStatusCode();
        return (owner, member, m.User.Id, org.Slug, org.Id);
    }

    private static Task<HttpResponseMessage> ManualAsync(HttpClient client, int? orgId, string start, string end, int? pause = null) =>
        client.PostAsJsonAsync("/api/v1/TimeTracking/manual",
            new { startTime = Utc(start), endTime = Utc(end), pauseDurationMinutes = pause, description = "Forgot to clock in", organizationId = orgId });

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task ManualEntry_Allowed_IsCreatedWithPauseRulesAndHistory_OverlapsAndFutureAreRefused()
    {
        var (_, member, _, _, orgId) = await SetupAsync("manual-ok");

        var created = await ManualAsync(member, orgId, "2026-09-14T06:00:00Z", "2026-09-14T14:00:00Z");
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var entry = (await JsonAsync(created)).GetProperty("entry");
        entry.GetProperty("pauseDurationMinutes").GetInt32().Should().Be(30);   // default 6 h rule
        entry.GetProperty("warnings").GetArrayLength().Should().Be(0);

        (await ManualAsync(member, orgId, "2026-09-14T13:00:00Z", "2026-09-14T15:00:00Z")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ManualAsync(member, orgId, DateTime.UtcNow.AddHours(1).ToString("o"), DateTime.UtcNow.AddHours(2).ToString("o")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var history = await JsonAsync(await member.GetAsync($"/api/v1/TimeTracking/{entry.GetProperty("id").GetInt32()}/history"));
        history.GetArrayLength().Should().Be(1);
        history[0].GetProperty("kind").GetString().Should().Be("Created");
        history[0].GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("field").GetString())
            .Should().Contain(["StartTime", "EndTime", "PauseDurationMinutes"]);
    }

    [Fact]
    public async Task ManualEntry_RequiresApproval_BecomesARequest_EntryCreatedOnApproval_DisabledIsForbidden()
    {
        var (owner, member, _, slug, orgId) = await SetupAsync("manual-req", new { editPastEntriesMode = RequiresApproval });

        var request = (await JsonAsync(await ManualAsync(member, orgId, "2026-09-15T06:00:00Z", "2026-09-15T10:00:00Z"))).GetProperty("request");
        request.GetProperty("type").GetString().Should().Be("AddManualEntry");
        var requestId = request.GetProperty("id").GetInt32();
        // A second, different forgotten day is a separate open request
        (await ManualAsync(member, orgId, "2026-09-16T06:00:00Z", "2026-09-16T10:00:00Z")).StatusCode.Should().Be(HttpStatusCode.Created);

        (await member.GetFromJsonAsync<PaginatedResponseDto<TimeEntryResponseDto>>("/api/v1/TimeTracking", TestHelpers.JsonOptions))!.Items.Should().BeEmpty();
        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{requestId}", new { accept = true })).EnsureSuccessStatusCode();

        var entries = (await member.GetFromJsonAsync<PaginatedResponseDto<TimeEntryResponseDto>>("/api/v1/TimeTracking", TestHelpers.JsonOptions))!.Items;
        var created = entries.Should().ContainSingle().Subject;
        created.Description.Should().Be("Forgot to clock in");
        var history = await JsonAsync(await member.GetAsync($"/api/v1/TimeTracking/{created.Id}/history"));
        history[0].GetProperty("kind").GetString().Should().Be("ApprovedRequest");
        history[0].GetProperty("changedByName").GetString().Should().Be("Olga Owner");

        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { editPastEntriesMode = Disabled })).EnsureSuccessStatusCode();
        (await ManualAsync(member, orgId, "2026-09-17T06:00:00Z", "2026-09-17T10:00:00Z")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // Personal entries are the member's own business
        (await ManualAsync(member, null, "2026-09-17T06:00:00Z", "2026-09-17T10:00:00Z")).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Admin_CorrectsFailureEntryOfMemberWhoMayNotEdit_HistoryShowsWhoChangedWhat()
    {
        var (owner, member, memberId, slug, orgId) = await SetupAsync("correct", new { editPastEntriesMode = Disabled, memberTimeEntryVisibility = true });
        int failureId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            var failure = new TimeEntry
            {
                UserId = memberId, OrganizationId = orgId, StartTime = Utc("2026-09-14T06:00:00Z"), EndTime = Utc("2026-09-14T22:00:00Z"),
                IsFailure = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            };
            db.TimeEntries.Add(failure);
            await db.SaveChangesAsync();
            failureId = failure.Id;
        }

        (await member.PutAsJsonAsync($"/api/v1/TimeTracking/{failureId}", new { endTime = Utc("2026-09-14T14:00:00Z") }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var corrected = await JsonAsync(await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/entries/{failureId}",
            new { endTime = Utc("2026-09-14T14:00:00Z") }));
        corrected.GetProperty("isFailure").GetBoolean().Should().BeFalse();
        corrected.GetProperty("pauseDurationMinutes").GetInt32().Should().Be(30);

        var added = await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/entries",
            new { startTime = Utc("2026-09-15T06:00:00Z"), endTime = Utc("2026-09-15T10:00:00Z"), description = "Added by admin" });
        added.StatusCode.Should().Be(HttpStatusCode.Created);
        var addedId = (await JsonAsync(added)).GetProperty("id").GetInt32();
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/entries",
            new { startTime = Utc("2026-09-15T09:00:00Z"), endTime = Utc("2026-09-15T11:00:00Z") })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await owner.DeleteAsync($"/api/v1/organizations/{slug}/members/{memberId}/entries/{addedId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // A plain member cannot use the admin endpoint (not even for their own entries)
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/entries",
            new { startTime = Utc("2026-09-16T06:00:00Z"), endTime = Utc("2026-09-16T10:00:00Z") })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The member and the admin see the history; the deleted entry keeps its history
        var history = await JsonAsync(await member.GetAsync($"/api/v1/TimeTracking/{failureId}/history"));
        var change = history.EnumerateArray().Single();
        change.GetProperty("kind").GetString().Should().Be("Updated");
        change.GetProperty("changedByName").GetString().Should().Be("Olga Owner");
        change.GetProperty("changes").EnumerateArray().Should().Contain(c =>
            c.GetProperty("field").GetString() == "EndTime" && c.GetProperty("oldValue").GetString() == "2026-09-14T22:00:00Z"
            && c.GetProperty("newValue").GetString() == "2026-09-14T14:00:00Z");
        change.GetProperty("changes").EnumerateArray().Should().Contain(c =>
            c.GetProperty("field").GetString() == "IsFailure" && c.GetProperty("newValue").GetString() == "false");
        (await JsonAsync(await owner.GetAsync($"/api/v1/TimeTracking/{addedId}/history"))).EnumerateArray()
            .Select(c => c.GetProperty("kind").GetString()).Should().Equal("Created", "Deleted");

        // Without "admins may see members' entries" the admin may still correct, but not browse the history
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { memberTimeEntryVisibility = false })).EnsureSuccessStatusCode();
        (await owner.GetAsync($"/api/v1/TimeTracking/{failureId}/history")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GroupAdmin_WithTimeEditApproval_CorrectsGroupMembersOnly()
    {
        var (owner, member, memberId, slug, _) = await SetupAsync("grp-correct");
        var (groupAdmin, ga) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "grp-correct-ga@manual.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, ga)).EnsureSuccessStatusCode();

        var body = new { startTime = Utc("2026-09-14T06:00:00Z"), endTime = Utc("2026-09-14T10:00:00Z") };
        (await groupAdmin.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/entries", body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var group = await JsonAsync(await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/groups", new { name = "Kitchen" }));
        var groupId = group.GetProperty("id").GetInt32();
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/groups/{groupId}/members", new { userId = memberId, role = "Member" })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/groups/{groupId}/members", new { userId = ga.User.Id, role = "Admin" })).EnsureSuccessStatusCode();
        (await groupAdmin.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/entries", body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // The org detail tells the UI the same: the group admin sees the member, but no correction controls
        (await CanCorrectAsync(groupAdmin, slug, memberId)).Should().BeFalse();
        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/groups/{groupId}/permissions", new { groupAdminCanApproveTimeEdits = true })).EnsureSuccessStatusCode();

        (await groupAdmin.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/entries", body)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await CanCorrectAsync(groupAdmin, slug, memberId)).Should().BeTrue();
        // ...but not for themselves (that would bypass their own edit rule)
        (await groupAdmin.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{ga.User.Id}/entries", body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CanCorrectAsync(groupAdmin, slug, ga.User.Id)).Should().BeFalse();
        // Org admins may correct everyone but themselves (the UI never offers corrections of one's own entries)
        (await CanCorrectAsync(owner, slug, memberId)).Should().BeTrue();
        (await CanCorrectAsync(owner, slug, ga.User.Id)).Should().BeTrue();
        (await CanCorrectAsync(member, slug, ga.User.Id)).Should().BeFalse();
    }

    private static async Task<bool> CanCorrectAsync(HttpClient caller, string slug, int memberId) =>
        (await JsonAsync(await caller.GetAsync($"/api/v1/organizations/{slug}"))).GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("id").GetInt32() == memberId).GetProperty("canCorrectEntries").GetBoolean();

    [Fact]
    public async Task WorkingTimeActWarnings_OnManualEntryAndInComplianceList()
    {
        var (owner, member, memberId, slug, orgId) = await SetupAsync("arbzg", new { memberTimeEntryVisibility = true });

        // Mon 14 Sep 2026: 06:00–18:00 UTC = 12 h gross, 45 min pause → 11.25 h net
        var longDay = (await JsonAsync(await ManualAsync(member, orgId, "2026-09-14T06:00:00Z", "2026-09-14T18:00:00Z"))).GetProperty("entry");
        longDay.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).Should().Equal("dailyMax");

        // Next day starts 02:00 UTC (04:00 local): only 8 h rest
        var early = (await JsonAsync(await ManualAsync(member, orgId, "2026-09-15T02:00:00Z", "2026-09-15T05:00:00Z"))).GetProperty("entry");
        early.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).Should().Equal("restPeriod");

        var stopped = await member.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationId = orgId });
        stopped.EnsureSuccessStatusCode();
        (await JsonAsync(await member.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null))).TryGetProperty("warnings", out _).Should().BeTrue();

        var days = await JsonAsync(await owner.GetAsync($"/api/v1/organizations/{slug}/members/{memberId}/compliance?from=2026-09-01&to=2026-09-30"));
        days.EnumerateArray().Select(d => (d.GetProperty("date").GetString(), d.GetProperty("dailyMax").GetBoolean(), d.GetProperty("restPeriod").GetBoolean()))
            .Should().Equal(("2026-09-14", true, false), ("2026-09-15", false, true));
        days[1].GetProperty("restHoursBefore").GetDouble().Should().Be(8);
    }
}
