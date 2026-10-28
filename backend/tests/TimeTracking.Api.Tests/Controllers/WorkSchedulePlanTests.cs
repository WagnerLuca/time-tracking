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
/// The work schedule model (issue #82): current + at most one future period, history read-only. Changing from today or a
/// later day, replacing the future one, the start rules (members from today, admins any open day, never in a closed
/// month), the admin's correction (never into closed months), cancelling the future one, the future one becoming current on
/// its day, schedule-change requests and the normalisation of old data (closed months unaffected, issue #81).
/// </summary>
public class WorkSchedulePlanTests : IClassFixture<TimeTrackingApiFactory>
{
    private const int Disabled = 0, RequiresApproval = 1, Allowed = 2;
    private readonly TimeTrackingApiFactory _factory;

    public WorkSchedulePlanTests(TimeTrackingApiFactory factory) => _factory = factory;

    private static DateOnly Today => OrgClock.For((string?)null).Today();
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    private Task<(HttpClient Owner, HttpClient Member, string Slug, int OrgId, int MemberId)> SetupAsync(string prefix, int mode = Allowed) =>
        TestHelpers.OrgWithMemberAsync(_factory, prefix, new { workScheduleChangeMode = mode, memberTimeEntryVisibility = true }, "plan.test");

    private static object Week(double hours, DateOnly? from = null, bool replaceFuture = false) =>
        new { validFrom = from is { } f ? D(f) : null, weeklyWorkHours = hours, distributeEvenly = true, replaceFuture };

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task ShouldFailAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be(code);
    }

    private static string? From(JsonElement plan, string which) =>
        plan.GetProperty(which).ValueKind == JsonValueKind.Null ? null : plan.GetProperty(which).GetProperty("validFrom").GetString();

    private static Task<HttpResponseMessage> AdminChange(HttpClient owner, string slug, int memberId, object body) =>
        owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedule/change", body);

    [Fact]
    public async Task Member_ChangesFromToday_ThenPlansAFutureOne_ReplacingNeedsConsent_CancelReopens()
    {
        var (owner, member, slug, _, memberId) = await SetupAsync("plan-member");
        var self = $"/api/v1/organizations/{slug}/work-schedule";
        // An admin set the first one months ago
        await OkAsync(await AdminChange(owner, slug, memberId, Week(40, Today.AddMonths(-3))));

        // Default start today: the old one ends yesterday and moves to the history
        var plan = await OkAsync(await member.PostAsJsonAsync($"{self}/change", Week(30)));
        From(plan, "current").Should().Be(D(Today));
        plan.GetProperty("current").GetProperty("targetMon").GetDouble().Should().Be(6);
        plan.GetProperty("future").ValueKind.Should().Be(JsonValueKind.Null);
        var history = plan.GetProperty("history").EnumerateArray().ToList();
        history.Should().ContainSingle();
        history[0].GetProperty("validTo").GetString().Should().Be(D(Today.AddDays(-1)));

        // Members start today at the earliest; a change never starts on/before the current one's first day
        await ShouldFailAsync(await member.PostAsJsonAsync($"{self}/change", Week(20, Today.AddDays(-1))), HttpStatusCode.BadRequest, "schedule.startBeforeToday");
        await ShouldFailAsync(await member.PostAsJsonAsync($"{self}/change", Week(20, Today)), HttpStatusCode.BadRequest, "schedule.beforeCurrent");

        // A future one: the current one runs until the day before
        var futureDay = Today.AddDays(20);
        plan = await OkAsync(await member.PostAsJsonAsync($"{self}/change", Week(20, futureDay)));
        From(plan, "future").Should().Be(D(futureDay));
        plan.GetProperty("current").GetProperty("validTo").GetString().Should().Be(D(futureDay.AddDays(-1)));

        // A second future one is refused, replacing it needs consent
        await ShouldFailAsync(await member.PostAsJsonAsync($"{self}/change", Week(25, futureDay.AddDays(5))), HttpStatusCode.Conflict, "schedule.futureExists");
        plan = await OkAsync(await member.PostAsJsonAsync($"{self}/change", Week(25, futureDay.AddDays(5), replaceFuture: true)));
        From(plan, "future").Should().Be(D(futureDay.AddDays(5)));
        plan.GetProperty("future").GetProperty("weeklyWorkHours").GetDouble().Should().Be(25);
        plan.GetProperty("current").GetProperty("validTo").GetString().Should().Be(D(futureDay.AddDays(4)));

        // Cancelling the future one: the current one runs on, open-ended
        plan = await OkAsync(await member.DeleteAsync($"{self}/future"));
        plan.GetProperty("future").ValueKind.Should().Be(JsonValueKind.Null);
        plan.GetProperty("current").GetProperty("validTo").ValueKind.Should().Be(JsonValueKind.Null);
        await ShouldFailAsync(await member.DeleteAsync($"{self}/future"), HttpStatusCode.NotFound, "schedule.noFuture");

        // The read-only list keeps every period (calendars)
        var all = await member.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/work-schedules");
        all.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task FutureSchedule_BecomesCurrentOnItsDay_TargetsOfThatDay()
    {
        var (owner, _, slug, orgId, memberId) = await SetupAsync("plan-switch");
        await OkAsync(await AdminChange(owner, slug, memberId, Week(40, Today.AddMonths(-1))));
        var day = Today.AddDays(7);
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) day = day.AddDays(1);
        await OkAsync(await AdminChange(owner, slug, memberId, Week(20, day)));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
        var periods = await db.WorkSchedules.Where(s => s.OrganizationId == orgId && s.UserId == memberId).ToListAsync();
        WorkSchedulePlan.Split(periods, day.AddDays(-1)).Future!.ValidFrom.Should().Be(day);
        var onTheDay = WorkSchedulePlan.Split(periods, day);
        onTheDay.Current!.WeeklyWorkHours.Should().Be(20);
        onTheDay.Future.Should().BeNull();
        onTheDay.History.Should().ContainSingle();

        var rows = await DailyReportAggregator.BuildAsync(db, memberId, orgId, day.AddDays(-1), day, 0, includeFuture: true);
        rows.Single(r => r.Date == day).TargetHours.Should().Be(4);
    }

    [Fact]
    public async Task Admin_StartsOnAnyOpenDay_NotInAClosedMonth_CorrectionNeverIntoClosedMonths()
    {
        var (owner, member, slug, _, memberId) = await SetupAsync("plan-admin");
        var lastMonth = ClosedPeriods.MonthOf(Today).AddMonths(-1);
        await OkAsync(await AdminChange(owner, slug, memberId, Week(40, lastMonth.AddMonths(-2))));

        // A correction of the current one while nothing is closed: targets and first day, retroactively
        var plan = await OkAsync(await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedule/current",
            new { validFrom = D(lastMonth.AddMonths(-2).AddDays(3)), weeklyWorkHours = 35, distributeEvenly = true }));
        From(plan, "current").Should().Be(D(lastMonth.AddMonths(-2).AddDays(3)));
        plan.GetProperty("current").GetProperty("weeklyWorkHours").GetDouble().Should().Be(35);
        plan.GetProperty("currentInClosedMonth").GetBoolean().Should().BeFalse();
        // Members cannot correct
        (await member.PutAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedule/current", Week(1)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/closed-periods", new { from = lastMonth, to = lastMonth })).EnsureSuccessStatusCode();

        // The current one reaches into the closed month: no correction, also not with a reason
        plan = await OkAsync(await owner.GetAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedule/plan"));
        plan.GetProperty("currentInClosedMonth").GetBoolean().Should().BeTrue();
        var correct = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/organizations/{slug}/members/{memberId}/work-schedule/current")
        {
            Content = JsonContent.Create(new { weeklyWorkHours = 30, distributeEvenly = true })
        };
        correct.Headers.Add("X-Change-Reason", "typo");
        await ShouldFailAsync(await owner.SendAsync(correct), HttpStatusCode.Conflict, "schedule.correctionClosed");

        // A change may start on an open past day, never in the closed month
        await ShouldFailAsync(await AdminChange(owner, slug, memberId, Week(30, lastMonth.AddDays(10))), HttpStatusCode.Conflict, "schedule.startInClosedMonth");
        plan = await OkAsync(await AdminChange(owner, slug, memberId, Week(30, lastMonth.AddMonths(1))));
        From(plan, "current").Should().Be(D(lastMonth.AddMonths(1)));
        plan.GetProperty("currentInClosedMonth").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task RuleModes_Disabled_RequiresApprovalViaRequest_ApprovalAppliesTheSameModel()
    {
        var (owner, member, slug, _, memberId) = await SetupAsync("plan-rules", RequiresApproval);
        var self = $"/api/v1/organizations/{slug}/work-schedule";
        (await member.PostAsJsonAsync($"{self}/change", Week(30))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await member.DeleteAsync($"{self}/future")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await OkAsync(await AdminChange(owner, slug, memberId, Week(40, Today.AddMonths(-1))));

        // A request is "new schedule from a day": not before today
        var past = await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests", new
        {
            type = "WorkScheduleChange", requestData = JsonSerializer.Serialize(new { validFrom = D(Today.AddDays(-1)), weeklyWorkHours = 30, distributeEvenly = true })
        });
        await ShouldFailAsync(past, HttpStatusCode.BadRequest, "schedule.startBeforeToday");
        var day = Today.AddDays(10);
        var created = await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests", new
        {
            type = "WorkScheduleChange", requestData = JsonSerializer.Serialize(new { validFrom = D(day), weeklyWorkHours = 30, distributeEvenly = true })
        });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{id}", new { accept = true })).EnsureSuccessStatusCode();

        var plan = await OkAsync(await member.GetAsync($"{self}/plan"));
        From(plan, "future").Should().Be(D(day));
        plan.GetProperty("future").GetProperty("weeklyWorkHours").GetDouble().Should().Be(30);
        plan.GetProperty("current").GetProperty("validTo").GetString().Should().Be(D(day.AddDays(-1)));

        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { workScheduleChangeMode = Disabled })).EnsureSuccessStatusCode();
        (await member.PostAsJsonAsync($"{self}/change", Week(30, Today.AddDays(40)))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Normalisation_ResolvesOverlapsDuplicatesGapsAndExtraFutures_Idempotent_ClosedMonthsUnchanged()
    {
        var (owner, _, slug, orgId, memberId) = await SetupAsync("plan-normalise");
        var lastMonth = ClosedPeriods.MonthOf(Today).AddMonths(-1);
        var monthBefore = lastMonth.AddMonths(-1);
        await OkAsync(await AdminChange(owner, slug, memberId, Week(40, monthBefore.AddMonths(-1))));
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/closed-periods", new { from = monthBefore, to = monthBefore })).EnsureSuccessStatusCode();
        var summaryUrl = $"/api/v1/organizations/{slug}/accounting/summary?year={monthBefore.Year}&month={monthBefore.Month}";
        var before = (await owner.GetFromJsonAsync<JsonElement>(summaryUrl)).GetProperty("members").ToString();

        // Messy old data, written directly (system work, as the old free editing could leave it)
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            WorkSchedule P(DateOnly from, DateOnly? to, double hours) => new()
            {
                UserId = memberId, OrganizationId = orgId, ValidFrom = from, ValidTo = to, WeeklyWorkHours = hours,
                TargetMon = hours / 5, TargetTue = hours / 5, TargetWed = hours / 5, TargetThu = hours / 5, TargetFri = hours / 5
            };
            db.WorkSchedules.RemoveRange(db.WorkSchedules.Where(s => s.OrganizationId == orgId && s.UserId == memberId));
            db.WorkSchedules.AddRange(
                P(monthBefore.AddMonths(-1), lastMonth.AddDays(-1), 40),         // overlapped by the next one
                P(monthBefore.AddDays(5), monthBefore.AddDays(9), 10),            // inside the closed month: splits the first
                P(lastMonth, lastMonth.AddDays(9), 20),                           // ends, then a gap
                P(lastMonth, lastMonth.AddDays(2), 25),                           // same first day: a duplicate (higher id)
                P(lastMonth.AddDays(15), null, 30),                               // after the gap
                P(Today.AddDays(10), null, 35),                                   // future
                P(Today.AddDays(20), null, 36));                                  // a second future one
            await db.SaveChangesAsync();
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            var report = await WorkSchedulePlan.NormalizeAllAsync(db);
            report.Duplicates.Should().BeGreaterThanOrEqualTo(1);
            report.Gaps.Should().BeGreaterThanOrEqualTo(1);
            report.ExtraFutures.Should().BeGreaterThanOrEqualTo(1);
            report.Overlaps.Should().BeGreaterThanOrEqualTo(2);

            var periods = await db.WorkSchedules.AsNoTracking().Where(s => s.OrganizationId == orgId && s.UserId == memberId)
                .OrderBy(s => s.ValidFrom).ToListAsync();
            periods.Select(p => (p.ValidFrom, p.ValidTo, p.WeeklyWorkHours)).Should().Equal(
                (monthBefore.AddMonths(-1), monthBefore.AddDays(4), 40),
                (monthBefore.AddDays(5), monthBefore.AddDays(9), 10),
                (monthBefore.AddDays(10), lastMonth.AddDays(-1), 40),
                (lastMonth, lastMonth.AddDays(14), 20),
                (lastMonth.AddDays(15), Today.AddDays(9), 30),
                (Today.AddDays(10), (DateOnly?)null, 35));
            var state = WorkSchedulePlan.Split(periods, Today);
            state.Current!.WeeklyWorkHours.Should().Be(30);
            state.Future!.WeeklyWorkHours.Should().Be(35);

            // Idempotent
            (await WorkSchedulePlan.NormalizeAllAsync(db)).Changed.Should().BeFalse();
        }

        // The closed month reads its frozen figures: unchanged
        (await owner.GetFromJsonAsync<JsonElement>(summaryUrl)).GetProperty("members").ToString().Should().Be(before);
    }
}
