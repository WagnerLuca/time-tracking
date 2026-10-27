using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Work schedule periods under the issue #82 model (a change from a day ends the period before it, admins correct the current
/// one and cancel a future one), target validation, the admin endpoints for a member's schedules, the rule modes and schedule
/// changes through approved requests. <see cref="WorkSchedulePlanTests"/> covers the model's core flows.
/// </summary>
public class WorkScheduleLifecycleTests : IClassFixture<TimeTrackingApiFactory>
{
    private const int Disabled = 0, RequiresApproval = 1, Allowed = 2;
    private const int SetInitialOvertime = 3, WorkScheduleChange = 4;

    private readonly TimeTrackingApiFactory _factory;

    public WorkScheduleLifecycleTests(TimeTrackingApiFactory factory) => _factory = factory;

    private static DateOnly Today => OrgClock.For((string?)null).Today();
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    private Task<(HttpClient Owner, HttpClient Member, string Slug, int OrgId, int MemberId)> SetupAsync(string prefix, object settings) =>
        TestHelpers.OrgWithMemberAsync(_factory, prefix, settings, "wsl.test");

    private static async Task<WorkSchedulePlanResponseDto> OkAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<WorkSchedulePlanResponseDto>(TestHelpers.JsonOptions))!;
    }

    private static async Task ShouldFailAsync(Task<HttpResponseMessage> request, HttpStatusCode status, string code)
    {
        var response = await request;
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be(code);
    }

    private static async Task<List<WorkScheduleResponseDto>> ListAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<List<WorkScheduleResponseDto>>(url, TestHelpers.JsonOptions))!;

    private static object Week(double hours, DateOnly? from = null) =>
        new { validFrom = from is { } f ? D(f) : null, weeklyWorkHours = hours, distributeEvenly = true };

    [Fact]
    public async Task Change_ClosesThePeriodBefore_AndBecomesCurrent()
    {
        var (owner, member, slug, _, memberId) = await SetupAsync("wsl-close", new { workScheduleChangeMode = Allowed });
        var self = $"/api/v1/organizations/{slug}/work-schedule";

        // Without any schedule the current one is an empty placeholder carrying the org's modes; the plan is empty
        var none = await member.GetFromJsonAsync<WorkScheduleResponseDto>(self, TestHelpers.JsonOptions);
        none!.Id.Should().Be(0);
        none.WeeklyWorkHours.Should().Be(0);
        none.WorkScheduleChangeMode.Should().Be("Allowed");
        var empty = await OkAsync(member.GetAsync($"{self}/plan"));
        empty.Current.Should().BeNull();
        empty.Future.Should().BeNull();
        empty.History.Should().BeEmpty();

        await OkAsync(owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedule/change", Week(40, Today.AddMonths(-6))));
        var plan = await OkAsync(member.PostAsJsonAsync($"{self}/change", Week(30)));

        var all = await ListAsync(member, $"/api/v1/organizations/{slug}/work-schedules");
        all.Select(s => s.ValidFrom).Should().Equal(D(Today.AddMonths(-6)), D(Today));   // ordered by ValidFrom
        all[0].ValidTo.Should().Be(D(Today.AddDays(-1)));
        all[1].ValidTo.Should().BeNull();
        all[1].TargetMon.Should().Be(6);
        plan.History.Single().Id.Should().Be(all[0].Id);

        var current = await member.GetFromJsonAsync<WorkScheduleResponseDto>(self, TestHelpers.JsonOptions);
        current!.Id.Should().Be(all[1].Id);
        current.WeeklyWorkHours.Should().Be(30);
    }

    [Fact]
    public async Task Change_OnOrBeforeAnExistingPeriodsFirstDay_IsRefused_EvenForAdmins()
    {
        var (owner, _, slug, _, memberId) = await SetupAsync("wsl-before", new { workScheduleChangeMode = Allowed });
        var url = $"/api/v1/organizations/{slug}/members/{memberId}/work-schedule/change";
        await OkAsync(owner.PostAsJsonAsync(url, Week(40, Today.AddMonths(-6))));
        await OkAsync(owner.PostAsJsonAsync(url, Week(30, Today.AddMonths(-2))));

        // Rewriting the past is not a change: not on the current one's first day, nor before it or a history period's
        await ShouldFailAsync(owner.PostAsJsonAsync(url, Week(20, Today.AddMonths(-2))), HttpStatusCode.BadRequest, "schedule.beforeCurrent");
        await ShouldFailAsync(owner.PostAsJsonAsync(url, Week(20, Today.AddMonths(-3))), HttpStatusCode.BadRequest, "schedule.beforeCurrent");
        await ShouldFailAsync(owner.PostAsJsonAsync(url, Week(20, Today.AddMonths(-7))), HttpStatusCode.BadRequest, "schedule.beforeCurrent");
        (await ListAsync(owner, $"/api/v1/organizations/{slug}/members/{memberId}/work-schedules")).Should().HaveCount(2);

        // The day after the current one's first day is fine
        var plan = await OkAsync(owner.PostAsJsonAsync(url, Week(20, Today.AddMonths(-2).AddDays(1))));
        plan.History.Select(h => h.ValidTo).Should().Equal(D(Today.AddMonths(-2)), D(Today.AddMonths(-2).AddDays(-1)));
    }

    [Fact]
    public async Task Targets_AreValidated_CorrectionReplacesThemAll()
    {
        var (owner, _, slug, _, memberId) = await SetupAsync("wsl-targets", new { workScheduleChangeMode = Allowed });
        var url = $"/api/v1/organizations/{slug}/members/{memberId}/work-schedule";
        var created = await OkAsync(owner.PostAsJsonAsync($"{url}/change", new
        {
            validFrom = D(Today.AddMonths(-1)), weeklyWorkHours = 40, distributeEvenly = false,
            targetMon = 8, targetTue = 8, targetWed = 8, targetThu = 8, targetFri = 4, targetSat = 4, targetSun = 0
        }));
        created.Current!.TargetSat.Should().Be(4);

        await ShouldFailAsync(owner.PostAsJsonAsync($"{url}/change", Week(81, Today)), HttpStatusCode.BadRequest, "schedule.weeklyRange");
        await ShouldFailAsync(owner.PutAsJsonAsync($"{url}/current", new { distributeEvenly = false, targetMon = 25 }), HttpStatusCode.BadRequest, "schedule.dailyRange");

        // A correction sets every target: unsent days are 0; the first day stays
        var manual = (await OkAsync(owner.PutAsJsonAsync($"{url}/current", new { weeklyWorkHours = 36, distributeEvenly = false, targetMon = 8, targetSun = 4 }))).Current!;
        (manual.WeeklyWorkHours, manual.TargetMon, manual.TargetFri, manual.TargetSat, manual.TargetSun).Should().Be((36, 8, 0, 0, 4));
        manual.ValidFrom.Should().Be(D(Today.AddMonths(-1)));

        var even = (await OkAsync(owner.PutAsJsonAsync($"{url}/current", Week(30)))).Current!;
        (even.TargetMon, even.TargetFri, even.TargetSat, even.TargetSun).Should().Be((6, 6, 0, 0));
        even.Id.Should().Be(created.Current.Id);
    }

    [Fact]
    public async Task DisabledMode_BlocksTheMembersChangesAndRequests_ButNotTheAdmin()
    {
        var (owner, member, slug, _, memberId) = await SetupAsync("wsl-disabled", new { workScheduleChangeMode = Disabled });
        var byAdmin = await OkAsync(owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedule/change", Week(40, Today.AddMonths(-1))));

        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/work-schedule/change", Week(20, Today.AddDays(5))))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.DeleteAsync($"/api/v1/organizations/{slug}/work-schedule/future"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // ...and a schedule request is pointless while the feature is off
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests",
                new { type = WorkScheduleChange, requestData = JsonSerializer.Serialize(new { validFrom = D(Today.AddDays(5)), weeklyWorkHours = 20 }) }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The member still sees it
        (await ListAsync(member, $"/api/v1/organizations/{slug}/work-schedules")).Single().Id.Should().Be(byAdmin.Current!.Id);
        (await OkAsync(member.GetAsync($"/api/v1/organizations/{slug}/work-schedule/plan"))).Current!.Id.Should().Be(byAdmin.Current.Id);
    }

    [Fact]
    public async Task Admin_ManagesAMembersSchedules_EndToEnd()
    {
        var (owner, _, slug, _, memberId) = await SetupAsync("wsl-admin", new { workScheduleChangeMode = Disabled });
        var url = $"/api/v1/organizations/{slug}/members/{memberId}/work-schedule";

        await OkAsync(owner.PostAsJsonAsync($"{url}/change", Week(40, Today.AddMonths(-3))));
        var plan = await OkAsync(owner.PostAsJsonAsync($"{url}/change", Week(20, Today.AddDays(-7))));
        var current = plan.Current!;
        plan.History.Single().ValidTo.Should().Be(D(Today.AddDays(-8)));

        var list = await ListAsync(owner, $"{url}s");
        list.Should().HaveCount(2).And.OnlyContain(s => s.UserId == memberId);
        var active = await owner.GetFromJsonAsync<WorkScheduleResponseDto>(url, TestHelpers.JsonOptions);
        active!.Id.Should().Be(current.Id);

        (await OkAsync(owner.PutAsJsonAsync($"{url}/current", Week(25)))).Current!.TargetMon.Should().Be(5);

        // A future one, then cancelled: the current one runs on
        (await OkAsync(owner.PostAsJsonAsync($"{url}/change", Week(30, Today.AddDays(14))))).Current!.ValidTo.Should().Be(D(Today.AddDays(13)));
        var cancelled = await OkAsync(owner.DeleteAsync($"{url}/future"));
        cancelled.Future.Should().BeNull();
        cancelled.Current!.ValidTo.Should().BeNull();
        await ShouldFailAsync(owner.DeleteAsync($"{url}/future"), HttpStatusCode.NotFound, "schedule.noFuture");
        (await ListAsync(owner, $"{url}s")).Select(s => s.Id).Should().Equal(list.Select(s => s.Id));
    }

    [Fact]
    public async Task MemberEndpoints_WritesAreAdminOnly_AndStrangersAreNotFound()
    {
        var (owner, member, slug, _, memberId) = await SetupAsync("wsl-authz", new { workScheduleChangeMode = Allowed });
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/Auth/me")).GetProperty("id").GetInt32();
        await OkAsync(owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/work-schedule/change", Week(40, Today.AddMonths(-1))));
        var ownerUrl = $"/api/v1/organizations/{slug}/members/{ownerId}/work-schedule";

        (await member.GetAsync(ownerUrl)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.GetAsync($"{ownerUrl}s")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.GetAsync($"{ownerUrl}/plan")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PostAsJsonAsync($"{ownerUrl}/change", Week(1, Today.AddDays(3)))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PutAsJsonAsync($"{ownerUrl}/current", Week(1))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.DeleteAsync($"{ownerUrl}/future")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // Not even on their own schedule: a member's writes are the self endpoints
        (await member.PutAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedule/current", Week(1)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Someone outside the organization has no schedules here
        var (_, stranger) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "wsl-authz-x@wsl.test");
        var strangerUrl = $"/api/v1/organizations/{slug}/members/{stranger.User.Id}/work-schedule";
        (await owner.GetAsync(strangerUrl)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync($"{strangerUrl}s")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync($"{strangerUrl}/plan")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PostAsJsonAsync($"{strangerUrl}/change", Week(40))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PutAsJsonAsync($"{strangerUrl}/current", Week(40))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.DeleteAsync($"{strangerUrl}/future")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StricterGroupOverride_TurnsDirectChangesIntoRequests()
    {
        var (owner, member, slug, _, memberId) = await SetupAsync("wsl-strict", new { workScheduleChangeMode = Allowed });
        await TestHelpers.CreateGroupAsync(owner, slug, "Probation", new { workScheduleChangeMode = RequiresApproval }, (memberId, "Member"));

        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/work-schedule/change", Week(40)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests",
                new { type = WorkScheduleChange, requestData = JsonSerializer.Serialize(new { validFrom = D(Today.AddDays(3)), weeklyWorkHours = 40 }) }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // The owner, outside the group, still changes directly
        await OkAsync(owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/work-schedule/change", Week(40, Today.AddMonths(-1))));
    }

    [Fact]
    public async Task ApprovedScheduleChange_AppliesManualTargets_AndReplacesAFutureOne()
    {
        var (owner, member, slug, _, memberId) = await SetupAsync("wsl-approve", new { workScheduleChangeMode = RequiresApproval });
        var adminUrl = $"/api/v1/organizations/{slug}/members/{memberId}/work-schedule";
        var previous = (await OkAsync(owner.PostAsJsonAsync($"{adminUrl}/change", Week(40, Today.AddMonths(-6))))).Current!;
        var day = Today.AddDays(14);

        var data = JsonSerializer.Serialize(new
        {
            validFrom = D(day), weeklyWorkHours = 24, distributeEvenly = false,
            targetMon = 8, targetTue = 8, targetSat = 8
        });
        var created = await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests", new { type = WorkScheduleChange, requestData = data });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // The identical request again is a duplicate
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests", new { type = WorkScheduleChange, requestData = data }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Meanwhile an admin planned another future one: approving replaces it
        await OkAsync(owner.PostAsJsonAsync($"{adminUrl}/change", Week(30, Today.AddDays(7))));
        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{id}", new { accept = true })).EnsureSuccessStatusCode();

        var schedules = await ListAsync(member, $"/api/v1/organizations/{slug}/work-schedules");
        schedules.Should().HaveCount(2);
        schedules[0].Id.Should().Be(previous.Id);
        schedules[0].ValidTo.Should().Be(D(day.AddDays(-1)));
        var applied = schedules[1];
        (applied.ValidFrom, applied.ValidTo, applied.WeeklyWorkHours).Should().Be((D(day), (string?)null, (double?)24));
        (applied.TargetMon, applied.TargetTue, applied.TargetWed, applied.TargetFri, applied.TargetSat).Should().Be((8, 8, 0, 0, 8));
    }

    [Fact]
    public async Task ApprovedScheduleChange_ThatNoLongerFits_IsNotApplied_AndStaysPending()
    {
        var (owner, member, slug, _, memberId) = await SetupAsync("wsl-approve-late", new { workScheduleChangeMode = RequiresApproval });
        var adminUrl = $"/api/v1/organizations/{slug}/members/{memberId}/work-schedule";
        await OkAsync(owner.PostAsJsonAsync($"{adminUrl}/change", Week(40, Today.AddMonths(-1))));

        var created = await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests",
            new { type = WorkScheduleChange, requestData = JsonSerializer.Serialize(new { validFrom = D(Today), weeklyWorkHours = 20, distributeEvenly = true }) });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // An admin changed the schedule from today in the meantime: the request would start on the current one's first day
        await OkAsync(owner.PostAsJsonAsync($"{adminUrl}/change", Week(30, Today)));
        await ShouldFailAsync(owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{id}", new { accept = true }),
            HttpStatusCode.BadRequest, "request.cannotApply");
        var open = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/requests");
        open.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetInt32() == id)
            .GetProperty("status").GetString().Should().Be("Pending");
        (await OkAsync(owner.GetAsync($"{adminUrl}/plan"))).Current!.WeeklyWorkHours.Should().Be(30);
    }

    [Fact]
    public async Task InitialOvertimeRequest_IsSingleOpen_AndApprovalSetsTheHours()
    {
        var (owner, member, slug, _, _) = await SetupAsync("wsl-iot", new { initialOvertimeMode = RequiresApproval });
        var url = $"/api/v1/organizations/{slug}/requests";

        var first = await member.PostAsJsonAsync(url, new { type = SetInitialOvertime, requestData = "12.5" });
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var second = await member.PostAsJsonAsync(url, new { type = SetInitialOvertime, requestData = "3" });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.Content.ReadAsStringAsync()).Should().Contain("of this type");

        var id = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await owner.PutAsJsonAsync($"{url}/{id}", new { accept = true })).EnsureSuccessStatusCode();

        var mine = await member.GetFromJsonAsync<WorkScheduleResponseDto>($"/api/v1/organizations/{slug}/work-schedule", TestHelpers.JsonOptions);
        mine!.InitialOvertimeHours.Should().Be(12.5);
        mine.InitialOvertimeMode.Should().Be("RequiresApproval");

        // Once answered, a new request may follow
        (await member.PostAsJsonAsync(url, new { type = SetInitialOvertime, requestData = "3" })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task GroupOverride_AllowsSelfServiceInitialOvertime()
    {
        var (owner, member, slug, _, memberId) = await SetupAsync("wsl-iot-grp", new { initialOvertimeMode = Disabled });
        await TestHelpers.CreateGroupAsync(owner, slug, "Seniors", new { initialOvertimeMode = Allowed }, (memberId, "Member"));

        var response = await member.PutAsJsonAsync($"/api/v1/organizations/{slug}/initial-overtime", new { initialOvertimeHours = -4.25 });
        response.EnsureSuccessStatusCode();
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("initialOvertimeHours").GetDouble().Should().Be(-4.25);

        // Allowed means directly — a request for it is refused
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests", new { type = SetInitialOvertime, requestData = "1" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
