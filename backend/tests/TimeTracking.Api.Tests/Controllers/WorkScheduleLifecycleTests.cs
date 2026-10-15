using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Validity periods of work schedules (one at a time, the open predecessor is closed), updates,
/// the admin endpoints for a member's schedules, and schedule changes through approved requests.
/// </summary>
public class WorkScheduleLifecycleTests : IClassFixture<TimeTrackingApiFactory>
{
    private const int Disabled = 0, RequiresApproval = 1, Allowed = 2;
    private const int SetInitialOvertime = 3, WorkScheduleChange = 4;

    private readonly TimeTrackingApiFactory _factory;

    public WorkScheduleLifecycleTests(TimeTrackingApiFactory factory) => _factory = factory;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    private Task<(HttpClient Owner, HttpClient Member, string Slug, int OrgId, int MemberId)> SetupAsync(string prefix, object settings) =>
        TestHelpers.OrgWithMemberAsync(_factory, prefix, settings, "wsl.test");

    private static async Task<WorkScheduleResponseDto> CreateAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<WorkScheduleResponseDto>(TestHelpers.JsonOptions))!;
    }

    private static async Task<List<WorkScheduleResponseDto>> ListAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<List<WorkScheduleResponseDto>>(url, TestHelpers.JsonOptions))!;

    [Fact]
    public async Task NewSchedule_ClosesTheOpenPredecessor_AndBecomesCurrent()
    {
        var (_, member, slug, _, _) = await SetupAsync("wsl-close", new { workScheduleChangeMode = Allowed });
        var url = $"/api/v1/organizations/{slug}/work-schedules";

        // Without any schedule the current one is an empty placeholder carrying the org's modes
        var none = await member.GetFromJsonAsync<WorkScheduleResponseDto>($"/api/v1/organizations/{slug}/work-schedule", TestHelpers.JsonOptions);
        none!.Id.Should().Be(0);
        none.WeeklyWorkHours.Should().Be(0);
        none.WorkScheduleChangeMode.Should().Be("Allowed");

        var first = await CreateAsync(member, url, new { validFrom = D(Today.AddMonths(-6)), weeklyWorkHours = 40, distributeEvenly = true });
        var second = await CreateAsync(member, url, new { validFrom = D(Today.AddMonths(-1)), weeklyWorkHours = 30, distributeEvenly = true });

        var all = await ListAsync(member, url);
        all.Select(s => s.Id).Should().Equal(first.Id, second.Id);   // ordered by ValidFrom
        all[0].ValidTo.Should().Be(D(Today.AddMonths(-1).AddDays(-1)));
        all[1].ValidTo.Should().BeNull();
        all[1].TargetMon.Should().Be(6);

        var current = await member.GetFromJsonAsync<WorkScheduleResponseDto>($"/api/v1/organizations/{slug}/work-schedule", TestHelpers.JsonOptions);
        current!.Id.Should().Be(second.Id);
        current.WeeklyWorkHours.Should().Be(30);
    }

    [Fact]
    public async Task DeletingTheNewestPeriod_ReopensThePredecessor()
    {
        var (_, member, slug, _, _) = await SetupAsync("wsl-reopen", new { workScheduleChangeMode = Allowed });
        var url = $"/api/v1/organizations/{slug}/work-schedules";
        var first = await CreateAsync(member, url, new { validFrom = D(Today.AddMonths(-6)), weeklyWorkHours = 40, distributeEvenly = true });
        var future = await CreateAsync(member, url, new { validFrom = D(Today.AddMonths(2)), weeklyWorkHours = 20, distributeEvenly = true });

        (await member.DeleteAsync($"{url}/{future.Id}")).EnsureSuccessStatusCode();

        // The schedule does not silently end on the day before the deleted period
        var remaining = (await ListAsync(member, url)).Single();
        remaining.Id.Should().Be(first.Id);
        remaining.ValidTo.Should().BeNull();
    }

    [Fact]
    public async Task OverlappingPeriods_AreRejected()
    {
        var (_, member, slug, _, _) = await SetupAsync("wsl-overlap", new { workScheduleChangeMode = Allowed });
        var url = $"/api/v1/organizations/{slug}/work-schedules";
        await CreateAsync(member, url, new { validFrom = "2026-01-01", validTo = "2026-03-31", weeklyWorkHours = 40, distributeEvenly = true });
        await CreateAsync(member, url, new { validFrom = "2026-09-01", weeklyWorkHours = 20, distributeEvenly = true });

        // Into a closed period
        var intoClosed = await member.PostAsJsonAsync(url, new { validFrom = "2026-03-01", validTo = "2026-04-30", weeklyWorkHours = 10, distributeEvenly = true });
        intoClosed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await intoClosed.Content.ReadAsStringAsync()).Should().Contain("2026-01-01").And.Contain("2026-03-31");

        // An open schedule that starts *later* is not closed automatically: running into it is an overlap
        var intoLaterOpen = await member.PostAsJsonAsync(url, new { validFrom = "2026-06-01", weeklyWorkHours = 10, distributeEvenly = true });
        intoLaterOpen.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await intoLaterOpen.Content.ReadAsStringAsync()).Should().Contain("ongoing");

        // The gap between them is free
        await CreateAsync(member, url, new { validFrom = "2026-04-01", validTo = "2026-08-31", weeklyWorkHours = 25, distributeEvenly = true });
        (await ListAsync(member, url)).Should().HaveCount(3);
    }

    [Fact]
    public async Task PeriodsSharingABoundaryDay_AreRejected()
    {
        var (_, member, slug, _, _) = await SetupAsync("wsl-boundary", new { workScheduleChangeMode = Allowed });
        var url = $"/api/v1/organizations/{slug}/work-schedules";
        await CreateAsync(member, url, new { validFrom = "2026-01-01", validTo = "2026-03-31", weeklyWorkHours = 40, distributeEvenly = true });

        var sameDay = await member.PostAsJsonAsync(url, new { validFrom = "2026-03-31", validTo = "2026-05-31", weeklyWorkHours = 20, distributeEvenly = true });
        sameDay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await sameDay.Content.ReadAsStringAsync()).Should().Contain("schedule.overlap");

        // The day after is fine
        await CreateAsync(member, url, new { validFrom = "2026-04-01", validTo = "2026-05-31", weeklyWorkHours = 20, distributeEvenly = true });
    }

    [Fact]
    public async Task Update_CanClearTheEndDate_AndIsCheckedForOverlaps()
    {
        var (_, member, slug, _, _) = await SetupAsync("wsl-clear", new { workScheduleChangeMode = Allowed });
        var url = $"/api/v1/organizations/{slug}/work-schedules";
        var first = await CreateAsync(member, url, new { validFrom = "2026-01-01", validTo = "2026-03-31", weeklyWorkHours = 40, distributeEvenly = true });

        // A missing validTo keeps the end date
        var kept = await member.PutAsJsonAsync($"{url}/{first.Id}", new { weeklyWorkHours = 30 });
        (await kept.Content.ReadFromJsonAsync<WorkScheduleResponseDto>(TestHelpers.JsonOptions))!.ValidTo.Should().Be("2026-03-31");

        // clearValidTo makes the period open-ended again
        var cleared = await member.PutAsJsonAsync($"{url}/{first.Id}", new { clearValidTo = true });
        cleared.EnsureSuccessStatusCode();
        (await cleared.Content.ReadFromJsonAsync<WorkScheduleResponseDto>(TestHelpers.JsonOptions))!.ValidTo.Should().BeNull();

        // Close it again, add a successor: re-opening or stretching onto its first day now collides
        (await member.PutAsJsonAsync($"{url}/{first.Id}", new { validTo = "2026-03-31" })).EnsureSuccessStatusCode();
        await CreateAsync(member, url, new { validFrom = "2026-04-01", weeklyWorkHours = 20, distributeEvenly = true });
        (await member.PutAsJsonAsync($"{url}/{first.Id}", new { clearValidTo = true })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await member.PutAsJsonAsync($"{url}/{first.Id}", new { validTo = "2026-04-01" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // ...and the end must not be before the start
        var backwards = await member.PutAsJsonAsync($"{url}/{first.Id}", new { validTo = "2025-12-31" });
        backwards.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await backwards.Content.ReadAsStringAsync()).Should().Contain("range.endBeforeStart");
    }

    [Fact]
    public async Task Update_ManualTargetsKeepUnsentDays_DistributeEvenlyClearsTheWeekend()
    {
        var (_, member, slug, _, _) = await SetupAsync("wsl-update", new { workScheduleChangeMode = Allowed });
        var url = $"/api/v1/organizations/{slug}/work-schedules";
        var created = await CreateAsync(member, url, new
        {
            validFrom = "2026-01-01", weeklyWorkHours = 40, distributeEvenly = false,
            targetMon = 8, targetTue = 8, targetWed = 8, targetThu = 8, targetFri = 4, targetSat = 4, targetSun = 0
        });
        created.TargetSat.Should().Be(4);

        var manual = await member.PutAsJsonAsync($"{url}/{created.Id}", new { weeklyWorkHours = 36, distributeEvenly = false, targetFri = 0, targetSun = 4 });
        manual.EnsureSuccessStatusCode();
        var m = (await manual.Content.ReadFromJsonAsync<WorkScheduleResponseDto>(TestHelpers.JsonOptions))!;
        (m.WeeklyWorkHours, m.TargetMon, m.TargetFri, m.TargetSat, m.TargetSun).Should().Be((36, 8, 0, 4, 4));

        var even = await member.PutAsJsonAsync($"{url}/{created.Id}", new { weeklyWorkHours = 30, distributeEvenly = true, validTo = "2026-12-31" });
        var e = (await even.Content.ReadFromJsonAsync<WorkScheduleResponseDto>(TestHelpers.JsonOptions))!;
        (e.TargetMon, e.TargetFri, e.TargetSat, e.TargetSun).Should().Be((6, 6, 0, 0));
        e.ValidTo.Should().Be("2026-12-31");

        // Without weekly hours only the period changes, the targets stay
        var period = await member.PutAsJsonAsync($"{url}/{created.Id}", new { validFrom = "2026-02-01", targetMon = 1 });
        var p = (await period.Content.ReadFromJsonAsync<WorkScheduleResponseDto>(TestHelpers.JsonOptions))!;
        (p.ValidFrom, p.TargetMon, p.ValidTo).Should().Be(("2026-02-01", 6, "2026-12-31"));
    }

    [Fact]
    public async Task SomeoneElsesSchedule_IsNotFound_ForSelfServiceEndpoints()
    {
        var (owner, member, slug, _, _) = await SetupAsync("wsl-foreign", new { workScheduleChangeMode = Allowed });
        var ownersSchedule = await CreateAsync(owner, $"/api/v1/organizations/{slug}/work-schedules", new { validFrom = "2026-01-01", weeklyWorkHours = 40, distributeEvenly = true });

        (await member.PutAsJsonAsync($"/api/v1/organizations/{slug}/work-schedules/{ownersSchedule.Id}", new { weeklyWorkHours = 1, distributeEvenly = true }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await member.DeleteAsync($"/api/v1/organizations/{slug}/work-schedules/{ownersSchedule.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await member.DeleteAsync($"/api/v1/organizations/{slug}/work-schedules/999999"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DisabledMode_BlocksUpdateAndDeleteToo_ButNotTheAdmin()
    {
        var (owner, member, slug, _, memberId) = await SetupAsync("wsl-disabled", new { workScheduleChangeMode = Disabled });
        var adminUrl = $"/api/v1/organizations/{slug}/members/{memberId}/work-schedules";
        var byAdmin = await CreateAsync(owner, adminUrl, new { validFrom = "2026-01-01", weeklyWorkHours = 40, distributeEvenly = true });

        (await member.PutAsJsonAsync($"/api/v1/organizations/{slug}/work-schedules/{byAdmin.Id}", new { weeklyWorkHours = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.DeleteAsync($"/api/v1/organizations/{slug}/work-schedules/{byAdmin.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // ...and a schedule request is pointless while the feature is off
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests",
                new { type = WorkScheduleChange, requestData = JsonSerializer.Serialize(new { validFrom = "2026-06-01", weeklyWorkHours = 20 }) }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The member still sees it
        (await ListAsync(member, $"/api/v1/organizations/{slug}/work-schedules")).Single().Id.Should().Be(byAdmin.Id);
    }

    [Fact]
    public async Task Admin_ManagesAMembersSchedules_EndToEnd()
    {
        var (owner, _, slug, _, memberId) = await SetupAsync("wsl-admin", new { workScheduleChangeMode = Disabled });
        var url = $"/api/v1/organizations/{slug}/members/{memberId}/work-schedules";

        var old = await CreateAsync(owner, url, new { validFrom = D(Today.AddMonths(-3)), weeklyWorkHours = 40, distributeEvenly = true });
        var current = await CreateAsync(owner, url, new { validFrom = D(Today.AddDays(-7)), weeklyWorkHours = 20, distributeEvenly = true });

        var list = await ListAsync(owner, url);
        list.Single(s => s.Id == old.Id).ValidTo.Should().Be(D(Today.AddDays(-8)));
        list.Should().OnlyContain(s => s.UserId == memberId);

        var active = await owner.GetFromJsonAsync<WorkScheduleResponseDto>($"/api/v1/organizations/{slug}/members/{memberId}/work-schedule", TestHelpers.JsonOptions);
        active!.Id.Should().Be(current.Id);

        var updated = await owner.PutAsJsonAsync($"{url}/{current.Id}", new { weeklyWorkHours = 25, distributeEvenly = true });
        (await updated.Content.ReadFromJsonAsync<WorkScheduleResponseDto>(TestHelpers.JsonOptions))!.TargetMon.Should().Be(5);

        (await owner.DeleteAsync($"{url}/{current.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.DeleteAsync($"{url}/{current.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PutAsJsonAsync($"{url}/{current.Id}", new { weeklyWorkHours = 1 })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ListAsync(owner, url)).Select(s => s.Id).Should().Equal(old.Id);
    }

    [Fact]
    public async Task MemberEndpoints_AreAdminOnly_AndScopedToTheMember()
    {
        var (owner, member, slug, _, memberId) = await SetupAsync("wsl-authz", new { workScheduleChangeMode = Allowed });
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/Auth/me")).GetProperty("id").GetInt32();
        var ownersSchedule = await CreateAsync(owner, $"/api/v1/organizations/{slug}/work-schedules", new { validFrom = "2026-01-01", weeklyWorkHours = 40, distributeEvenly = true });
        var ownerUrl = $"/api/v1/organizations/{slug}/members/{ownerId}";

        (await member.GetAsync($"{ownerUrl}/work-schedule")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.GetAsync($"{ownerUrl}/work-schedules")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PutAsJsonAsync($"{ownerUrl}/work-schedules/{ownersSchedule.Id}", new { weeklyWorkHours = 1 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.DeleteAsync($"{ownerUrl}/work-schedules/{ownersSchedule.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // A schedule id only counts under its own member
        (await owner.DeleteAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedules/{ownersSchedule.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Someone outside the organization has no schedules here
        var (_, stranger) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "wsl-authz-x@wsl.test");
        (await owner.GetAsync($"/api/v1/organizations/{slug}/members/{stranger.User.Id}/work-schedule")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/organizations/{slug}/members/{stranger.User.Id}/work-schedules")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StricterGroupOverride_TurnsDirectChangesIntoRequests()
    {
        var (owner, member, slug, _, memberId) = await SetupAsync("wsl-strict", new { workScheduleChangeMode = Allowed });
        await TestHelpers.CreateGroupAsync(owner, slug, "Probation", new { workScheduleChangeMode = RequiresApproval }, (memberId, "Member"));

        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/work-schedules", new { validFrom = "2026-01-01", weeklyWorkHours = 40, distributeEvenly = true }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests",
                new { type = WorkScheduleChange, requestData = JsonSerializer.Serialize(new { validFrom = "2026-01-01", weeklyWorkHours = 40 }) }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // The owner, outside the group, still changes directly
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/work-schedules", new { validFrom = "2026-01-01", weeklyWorkHours = 40, distributeEvenly = true }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ApprovedScheduleChange_AppliesManualTargets_AndClosesTheOpenSchedule()
    {
        var (owner, member, slug, _, memberId) = await SetupAsync("wsl-approve", new { workScheduleChangeMode = RequiresApproval });
        var previous = await CreateAsync(owner, $"/api/v1/organizations/{slug}/members/{memberId}/work-schedules",
            new { validFrom = "2026-01-01", weeklyWorkHours = 40, distributeEvenly = true });

        var data = JsonSerializer.Serialize(new
        {
            validFrom = "2026-07-01", weeklyWorkHours = 24, distributeEvenly = false,
            targetMon = 8, targetTue = 8, targetSat = 8
        });
        var created = await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests", new { type = WorkScheduleChange, requestData = data });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // The identical request again is a duplicate, a different one is fine
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests", new { type = WorkScheduleChange, requestData = data }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{id}", new { accept = true })).EnsureSuccessStatusCode();

        var schedules = await ListAsync(member, $"/api/v1/organizations/{slug}/work-schedules");
        schedules.Should().HaveCount(2);
        schedules[0].Id.Should().Be(previous.Id);
        schedules[0].ValidTo.Should().Be("2026-06-30");
        var applied = schedules[1];
        (applied.ValidFrom, applied.ValidTo, applied.WeeklyWorkHours).Should().Be(("2026-07-01", (string?)null, (double?)24));
        (applied.TargetMon, applied.TargetTue, applied.TargetWed, applied.TargetFri, applied.TargetSat).Should().Be((8, 8, 0, 0, 8));
    }

    [Fact]
    public async Task ApprovedScheduleChange_ThatOverlaps_IsNotApplied_AndStaysPending()
    {
        var (owner, member, slug, _, memberId) = await SetupAsync("wsl-approve-overlap", new { workScheduleChangeMode = RequiresApproval });
        var adminUrl = $"/api/v1/organizations/{slug}/members/{memberId}/work-schedules";
        await CreateAsync(owner, adminUrl, new { validFrom = "2026-01-01", validTo = "2026-03-31", weeklyWorkHours = 40, distributeEvenly = true });

        async Task<int> AskAsync(object body)
        {
            var created = await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests",
                new { type = WorkScheduleChange, requestData = JsonSerializer.Serialize(body) });
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        }

        // Shares the boundary day 2026-03-31 with the closed period: same rule as a direct creation
        var boundary = await AskAsync(new { validFrom = "2026-03-31", weeklyWorkHours = 20, distributeEvenly = true });
        var refused = await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{boundary}", new { accept = true });
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("request.cannotApply");
        var open = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/requests");
        open.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetInt32() == boundary)
            .GetProperty("status").GetString().Should().Be("Pending");
        (await ListAsync(owner, adminUrl)).Should().ContainSingle();

        // An open-ended predecessor is still closed automatically
        await CreateAsync(owner, adminUrl, new { validFrom = "2026-04-01", weeklyWorkHours = 30, distributeEvenly = true });
        var later = await AskAsync(new { validFrom = "2026-09-01", weeklyWorkHours = 20, distributeEvenly = true });
        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{later}", new { accept = true })).EnsureSuccessStatusCode();
        (await ListAsync(owner, adminUrl)).Select(s => (s.ValidFrom, s.ValidTo))
            .Should().Equal(("2026-01-01", "2026-03-31"), ("2026-04-01", "2026-08-31"), ("2026-09-01", (string?)null));
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
