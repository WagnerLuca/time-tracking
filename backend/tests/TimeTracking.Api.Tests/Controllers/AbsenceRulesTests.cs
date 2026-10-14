using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Absence error paths and rules: ranges, duplicates, custom types, who may delete what,
/// who sees whose absences, the self-service rule and absence requests.
/// </summary>
public class AbsenceRulesTests : IClassFixture<TimeTrackingApiFactory>
{
    private const int Disabled = 0, RequiresApproval = 1, Allowed = 2;
    private const int SickDay = 0, Vacation = 1;
    private const int RequestAbsence = 6;

    private readonly TimeTrackingApiFactory _factory;

    public AbsenceRulesTests(TimeTrackingApiFactory factory) => _factory = factory;

    private Task<(HttpClient Owner, HttpClient Member, string Slug, int OrgId, int MemberId)> SetupAsync(string prefix, object settings) =>
        TestHelpers.OrgWithMemberAsync(_factory, prefix, settings, "abs.test");

    private static async Task<List<AbsenceDayResponseDto>> AbsencesAsync(HttpClient client, string slug, string query = "") =>
        (await client.GetFromJsonAsync<PaginatedResponseDto<AbsenceDayResponseDto>>(
            $"/api/v1/organizations/{slug}/absences?limit=200{query}", TestHelpers.JsonOptions))!.Items;

    [Fact]
    public async Task Ranges_RejectReversedTooLongAndIncompleteRequests()
    {
        var (owner, _, slug, _, memberId) = await SetupAsync("abs-range", new { absenceSelfServiceMode = Allowed });
        var self = $"/api/v1/organizations/{slug}/absences/range";
        var admin = $"/api/v1/organizations/{slug}/absences/admin/range";

        (await owner.PostAsJsonAsync(self, new { from = "2026-03-10", to = "2026-03-09", type = Vacation })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync(self, new { from = "2026-01-01", to = "2027-01-03", type = Vacation })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync(admin, new { from = "2026-03-09", to = "2026-03-10", type = Vacation })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync(admin, new { userId = memberId, from = "2026-03-14", to = "2026-03-15", type = Vacation }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);   // weekend only

        var (_, stranger) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "abs-range-x@abs.test");
        (await owner.PostAsJsonAsync(admin, new { userId = stranger.User.Id, from = "2026-03-09", to = "2026-03-10", type = Vacation }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences/admin", new { userId = stranger.User.Id, date = "2026-03-09", type = Vacation }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var ok = await owner.PostAsJsonAsync(admin, new { userId = memberId, from = "2026-03-13", to = "2026-03-16", type = SickDay, note = "Flu" });
        ok.EnsureSuccessStatusCode();
        var days = (await ok.Content.ReadFromJsonAsync<List<AbsenceDayResponseDto>>(TestHelpers.JsonOptions))!;
        days.Select(d => d.Date).Should().Equal("2026-03-13", "2026-03-16");
        days.Should().OnlyContain(d => d.UserId == memberId && d.Type == "SickDay" && d.Note == "Flu");

        var conflict = await owner.PostAsJsonAsync(admin, new { userId = memberId, from = "2026-03-12", to = "2026-03-16", type = Vacation });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflict.Content.ReadAsStringAsync()).Should().Contain("2026-03-13, 2026-03-16");
    }

    [Fact]
    public async Task CustomTypes_ValidateNamesAndUsage()
    {
        var (owner, member, slug, _, _) = await SetupAsync("abs-types", new { absenceSelfServiceMode = Allowed });
        var url = $"/api/v1/organizations/{slug}/absence-types";

        (await member.PostAsJsonAsync(url, new { name = "Seminar", creditMode = 0 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var seminar = (await (await owner.PostAsJsonAsync(url, new { name = "Seminar", color = "#123456", creditMode = 2 }))
            .Content.ReadFromJsonAsync<AbsenceTypeDefinitionResponseDto>(TestHelpers.JsonOptions))!;
        var trip = (await (await owner.PostAsJsonAsync(url, new { name = "Trip", creditMode = 1 }))
            .Content.ReadFromJsonAsync<AbsenceTypeDefinitionResponseDto>(TestHelpers.JsonOptions))!;
        (await owner.PostAsJsonAsync(url, new { name = "Seminar", creditMode = 0 })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Rename onto another active name → conflict; a real update works
        (await owner.PutAsJsonAsync($"{url}/{trip.Id}", new { name = "Seminar", creditMode = 1 })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var updated = await owner.PutAsJsonAsync($"{url}/{trip.Id}", new { name = "Business trip", color = "#abcdef", creditMode = 1, countsAsVacation = true });
        updated.EnsureSuccessStatusCode();
        var u = (await updated.Content.ReadFromJsonAsync<AbsenceTypeDefinitionResponseDto>(TestHelpers.JsonOptions))!;
        (u.Name, u.Color, u.CreditMode, u.CountsAsVacation).Should().Be(("Business trip", "#abcdef", "FillToTarget", true));

        // A member books the custom type: stored as Other with the type attached
        var booked = await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences", new { date = "2026-05-04", type = Vacation, customTypeId = seminar.Id });
        booked.EnsureSuccessStatusCode();
        var b = (await booked.Content.ReadFromJsonAsync<AbsenceDayResponseDto>(TestHelpers.JsonOptions))!;
        (b.Type, b.CustomTypeId, b.CustomTypeName, b.CreditMode).Should().Be(("Other", seminar.Id, "Seminar", "FullCredit"));

        // Deleting deactivates: the type disappears from the list and cannot be booked any more,
        // existing absences keep it
        (await owner.DeleteAsync($"{url}/{seminar.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.DeleteAsync($"{url}/{seminar.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PutAsJsonAsync($"{url}/{seminar.Id}", new { name = "X", creditMode = 0 })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await member.GetFromJsonAsync<List<AbsenceTypeDefinitionResponseDto>>(url, TestHelpers.JsonOptions))!
            .Select(t => t.Name).Should().Equal("Business trip");
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences", new { date = "2026-05-05", type = Vacation, customTypeId = seminar.Id }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences", new { date = "2026-05-05", type = Vacation, customTypeId = 999999 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AbsencesAsync(member, slug)).Single().CustomTypeName.Should().Be("Seminar");
    }

    [Fact]
    public async Task Members_DeleteOnlyTheirOwnSelfEnteredAbsences()
    {
        var (owner, member, slug, _, memberId) = await SetupAsync("abs-delete", new { absenceSelfServiceMode = Allowed, visibility = TestHelpers.Matrix(("Vacation", true, true)) });
        var url = $"/api/v1/organizations/{slug}/absences";

        var own = (await (await member.PostAsJsonAsync(url, new { date = "2026-06-01", type = Vacation })).Content.ReadFromJsonAsync<AbsenceDayResponseDto>(TestHelpers.JsonOptions))!;
        var byAdmin = (await (await owner.PostAsJsonAsync($"{url}/admin", new { userId = memberId, date = "2026-06-02", type = Vacation }))
            .Content.ReadFromJsonAsync<AbsenceDayResponseDto>(TestHelpers.JsonOptions))!;
        var ownersOwn = (await (await owner.PostAsJsonAsync(url, new { date = "2026-06-03", type = Vacation })).Content.ReadFromJsonAsync<AbsenceDayResponseDto>(TestHelpers.JsonOptions))!;

        var denied = await member.DeleteAsync($"{url}/{byAdmin.Id}");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().Contain("Ask an admin");
        (await member.DeleteAsync($"{url}/{ownersOwn.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await member.DeleteAsync($"{url}/{own.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.DeleteAsync($"{url}/{byAdmin.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await AbsencesAsync(owner, slug)).Select(a => a.Id).Should().Equal(ownersOwn.Id);
    }

    [Fact]
    public async Task AbsenceFromAnApprovedRequest_CanOnlyBeRemovedByAnAdmin()
    {
        var (owner, member, slug, _, _) = await SetupAsync("abs-req-del", new { absenceSelfServiceMode = RequiresApproval });

        var created = await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests",
            new { type = RequestAbsence, requestData = JsonSerializer.Serialize(new { dates = new[] { "2026-07-06" }, type = Vacation }) });
        var requestId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{requestId}", new { accept = true })).EnsureSuccessStatusCode();

        var absence = (await AbsencesAsync(member, slug)).Single();
        absence.CreatedViaRequestId.Should().Be(requestId);
        absence.CreatedByName.Should().Be("Test User");

        (await member.DeleteAsync($"/api/v1/organizations/{slug}/absences/{absence.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.DeleteAsync($"/api/v1/organizations/{slug}/absences/{absence.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Visibility_OwnAlways_OthersPerSetting_AdminsEverything()
    {
        var (owner, member, slug, _, memberId) = await SetupAsync("abs-vis", new { absenceSelfServiceMode = Allowed, visibility = TestHelpers.Matrix(("Vacation", true, true), ("SickDays", false, false)) });
        var (colleague, c) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "abs-vis-c@abs.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, c)).EnsureSuccessStatusCode();
        var url = $"/api/v1/organizations/{slug}/absences";

        (await member.PostAsJsonAsync(url, new { date = "2026-08-03", type = Vacation })).EnsureSuccessStatusCode();
        (await member.PostAsJsonAsync(url, new { date = "2026-08-04", type = SickDay })).EnsureSuccessStatusCode();

        // The member sees both of their own, filtered or not
        (await AbsencesAsync(member, slug, $"&userId={memberId}")).Should().HaveCount(2);
        // A colleague sees the vacation but not the private sick day — also when filtering for the member
        (await AbsencesAsync(colleague, slug, $"&userId={memberId}")).Select(a => a.Type).Should().Equal("Vacation");
        (await AbsencesAsync(colleague, slug)).Select(a => a.Type).Should().Equal("Vacation");
        // Date filters apply to everyone
        (await AbsencesAsync(colleague, slug, "&from=2026-08-04&to=2026-08-31")).Should().BeEmpty();
        // Admins see everything
        (await AbsencesAsync(owner, slug, $"&userId={memberId}")).Should().HaveCount(2);
    }

    [Fact]
    public async Task SelfServiceRule_DisabledForbids_GroupAdminWithApprovalRightBypasses()
    {
        var (owner, member, slug, _, memberId) = await SetupAsync("abs-self", new { absenceSelfServiceMode = Disabled });
        var url = $"/api/v1/organizations/{slug}/absences";

        var disabled = await member.PostAsJsonAsync(url, new { date = "2026-09-07", type = Vacation });
        disabled.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await disabled.Content.ReadAsStringAsync()).Should().Contain("disabled");
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests",
                new { type = RequestAbsence, requestData = JsonSerializer.Serialize(new { dates = new[] { "2026-09-07" }, type = Vacation }) }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // As admin of a group that approves absences the member enters absences directly
        await TestHelpers.CreateGroupAsync(owner, slug, "Leads", new { groupAdminCanApproveAbsences = true }, (memberId, "Admin"));
        (await member.PostAsJsonAsync(url, new { date = "2026-09-07", type = Vacation })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AbsenceRequests_DuplicateDatesConflict_RangesKeepWorkingDays_CustomTypesApply()
    {
        var (owner, member, slug, _, _) = await SetupAsync("abs-requests", new { absenceSelfServiceMode = RequiresApproval });
        var url = $"/api/v1/organizations/{slug}/requests";
        var type = (await (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/absence-types", new { name = "Training", creditMode = 2 }))
            .Content.ReadFromJsonAsync<AbsenceTypeDefinitionResponseDto>(TestHelpers.JsonOptions))!;

        static object Absence(int type, int? customTypeId, params string[] dates) =>
            new { type = RequestAbsence, requestData = JsonSerializer.Serialize(new { dates, type, customTypeId, isHalfDay = true, note = "n" }) };

        // Fri 2 Oct – Mon 5 Oct 2026: the weekend in between is dropped on approval
        var first = await member.PostAsJsonAsync(url, Absence(Vacation, null, "2026-10-02", "2026-10-03", "2026-10-04", "2026-10-05"));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var overlapping = await member.PostAsJsonAsync(url, Absence(Vacation, null, "2026-10-05", "2026-10-06"));
        overlapping.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await overlapping.Content.ReadAsStringAsync()).Should().Contain("2026-10-05");

        var training = await member.PostAsJsonAsync(url, Absence(Vacation, type.Id, "2026-10-07"));
        training.StatusCode.Should().Be(HttpStatusCode.Created);

        foreach (var r in new[] { first, training })
        {
            var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            (await owner.PutAsJsonAsync($"{url}/{id}", new { accept = true })).EnsureSuccessStatusCode();
        }

        var absences = await AbsencesAsync(member, slug);
        absences.Select(a => a.Date).Should().Equal("2026-10-02", "2026-10-05", "2026-10-07");
        absences.Should().OnlyContain(a => a.IsHalfDay && a.Note == "n");
        absences[2].Should().Match<AbsenceDayResponseDto>(a => a.Type == "Other" && a.CustomTypeId == type.Id);
    }
}
