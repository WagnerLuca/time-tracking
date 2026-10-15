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
/// Closed months (issue #57): closing/reopening with its audit list, and the one write guard on
/// every path — members refused, admins with a reason (flagged in the history), group leads
/// unable to approve, the terminal refused, the business-day-end auto-stop unaffected.
/// </summary>
public class ClosedPeriodTests : IClassFixture<TimeTrackingApiFactory>
{
    private const int Allowed = 2;
    private readonly TimeTrackingApiFactory _factory;

    public ClosedPeriodTests(TimeTrackingApiFactory factory) => _factory = factory;

    /// <summary>First day of the month before the current one (Berlin) — it has ended, so it can be closed.</summary>
    private static readonly DateOnly LastMonth = ClosedPeriods.MonthOf(OrgClock.For((string?)null).Today()).AddMonths(-1);
    private static readonly string LastMonthKey = LastMonth.ToString("yyyy-MM");

    /// <summary>06:00–14:00 UTC (same day in Berlin) on the given day of last month.</summary>
    private static (DateTime Start, DateTime End) Day(int day)
    {
        var d = LastMonth.AddDays(day - 1).ToDateTime(new TimeOnly(6, 0), DateTimeKind.Utc);
        return (d, d.AddHours(8));
    }

    private Task<(HttpClient Owner, HttpClient Member, string Slug, int OrgId, int MemberId)> SetupAsync(string prefix) =>
        TestHelpers.OrgWithMemberAsync(_factory, prefix,
            new { editPastEntriesMode = Allowed, csvImportMode = Allowed, workScheduleChangeMode = Allowed, memberTimeEntryVisibility = true },
            "closed.test");

    private static Task<HttpResponseMessage> CloseAsync(HttpClient admin, string slug, DateOnly from, DateOnly to, string? reason = null) =>
        admin.PostAsJsonAsync($"/api/v1/organizations/{slug}/closed-periods", new { from, to, reason });

    private static async Task<int> ManualAsync(HttpClient client, int orgId, int day)
    {
        var (start, end) = Day(day);
        var response = await client.PostAsJsonAsync("/api/v1/TimeTracking/manual", new { startTime = start, endTime = end, organizationId = orgId });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("entry").GetProperty("id").GetInt32();
    }

    private static async Task ShouldBeClosedAsync(HttpResponseMessage response, string code = "period.closed")
    {
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be(code);
        problem.GetProperty("month").GetString().Should().Be(LastMonthKey);
    }

    private static HttpRequestMessage WithReason(HttpMethod method, string url, object? body, string reason)
    {
        var message = new HttpRequestMessage(method, url) { Content = body == null ? null : JsonContent.Create(body) };
        message.Headers.Add("X-Change-Reason", Uri.EscapeDataString(reason));
        return message;
    }

    [Fact]
    public async Task CloseAndReopen_OnlyEndedMonths_AdminsOnly_AuditListNamesWhoWhenWhy()
    {
        var (owner, member, slug, _, _) = await SetupAsync("cp-audit");
        var thisMonth = LastMonth.AddMonths(1);

        (await CloseAsync(member, slug, LastMonth, LastMonth)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var current = await CloseAsync(owner, slug, thisMonth, thisMonth);
        current.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await current.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("period.notEnded");
        (await CloseAsync(owner, slug, LastMonth, LastMonth.AddMonths(-1))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CloseAsync(owner, slug, LastMonth.AddMonths(-30), LastMonth)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Two consecutive months at once (days inside the month are fine); closing one again changes nothing
        var closed = await CloseAsync(owner, slug, LastMonth.AddMonths(-1).AddDays(9), LastMonth.AddDays(3), "Payroll");
        closed.StatusCode.Should().Be(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync());
        (await CloseAsync(owner, slug, LastMonth, LastMonth)).EnsureSuccessStatusCode();

        var forMember = await member.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/closed-periods");
        forMember.GetProperty("months").EnumerateArray().Select(m => m.GetString())
            .Should().Equal(LastMonth.AddMonths(-1).ToString("yyyy-MM-dd"), LastMonth.ToString("yyyy-MM-dd"));
        forMember.GetProperty("events").GetArrayLength().Should().Be(0);
        (await member.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}"))
            .GetProperty("closedMonths").GetArrayLength().Should().Be(2);

        var noReason = await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/closed-periods/reopen", new { month = LastMonth, reason = " " });
        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/closed-periods/reopen", new { month = LastMonth.AddMonths(-5), reason = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/closed-periods/reopen", new { month = LastMonth, reason = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var reopened = await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/closed-periods/reopen", new { month = LastMonth, reason = "Late sick note" });
        var state = await reopened.Content.ReadFromJsonAsync<JsonElement>();
        state.GetProperty("months").GetArrayLength().Should().Be(1);
        var events = state.GetProperty("events").EnumerateArray().ToList();
        events.Select(e => e.GetProperty("kind").GetString()).Should().Equal("Reopened", "Closed", "Closed");
        events[0].GetProperty("reason").GetString().Should().Be("Late sick note");
        events[0].GetProperty("userName").GetString().Should().Be("Test User");
        events[1].GetProperty("reason").GetString().Should().Be("Payroll");
    }

    [Fact]
    public async Task Member_IsRefusedOnEveryWritePath_InsideAClosedMonth()
    {
        var (owner, member, slug, orgId, _) = await SetupAsync("cp-member");
        var entryId = await ManualAsync(member, orgId, 14);
        (await CloseAsync(owner, slug, LastMonth, LastMonth)).EnsureSuccessStatusCode();

        var (start, end) = Day(15);
        await ShouldBeClosedAsync(await member.PostAsJsonAsync("/api/v1/TimeTracking/manual", new { startTime = start, endTime = end, organizationId = orgId }));
        await ShouldBeClosedAsync(await member.PutAsJsonAsync($"/api/v1/TimeTracking/{entryId}", new { endTime = Day(14).End.AddHours(-1) }));
        await ShouldBeClosedAsync(await member.DeleteAsync($"/api/v1/TimeTracking/{entryId}"));
        // Moving an entry out of the closed month is a change of it too
        await ShouldBeClosedAsync(await member.PutAsJsonAsync($"/api/v1/TimeTracking/{entryId}",
            new { startTime = DateTime.UtcNow.AddHours(-3), endTime = DateTime.UtcNow.AddHours(-2) }));

        await ShouldBeClosedAsync(await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences",
            new { date = LastMonth.AddDays(20).ToString("yyyy-MM-dd"), type = 0 }));
        await ShouldBeClosedAsync(await member.PostAsJsonAsync($"/api/v1/ExportImport/import/confirm?orgSlug={slug}", new[]
        {
            new { date = LastMonth.AddDays(21).ToString("yyyy-MM-dd"), startTime = "08:00", endTime = "16:00", pauseMinutes = 30, description = "Imported" }
        }));
        await ShouldBeClosedAsync(await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/work-schedules",
            new { validFrom = LastMonth.AddDays(1).ToString("yyyy-MM-dd"), weeklyWorkHours = 30.0, distributeEvenly = true }));
        // A schedule from next month on leaves the closed month alone
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/work-schedules",
            new { validFrom = LastMonth.AddMonths(2).ToString("yyyy-MM-dd"), weeklyWorkHours = 30.0, distributeEvenly = true })).EnsureSuccessStatusCode();

        // Asking instead does not work either
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { editPastEntriesMode = 1, absenceSelfServiceMode = 1 })).EnsureSuccessStatusCode();
        await ShouldBeClosedAsync(await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests", new
        {
            type = 1, relatedEntityId = entryId, requestData = JsonSerializer.Serialize(new { endTime = Day(14).End.AddHours(-1) })
        }));
        await ShouldBeClosedAsync(await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests", new
        {
            type = 6, requestData = JsonSerializer.Serialize(new { dates = new[] { LastMonth.AddDays(22) }, type = 1 })
        }));
        await ShouldBeClosedAsync(await member.PostAsJsonAsync("/api/v1/TimeTracking/manual", new { startTime = start, endTime = end, organizationId = orgId }));

        // Nothing changed; outside the month everything still works
        (await member.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/absences"))
            .GetProperty("items").GetArrayLength().Should().Be(0);
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests", new
        {
            type = 6, requestData = JsonSerializer.Serialize(new { dates = new[] { LastMonth.AddMonths(2).AddDays(3) }, type = 1 })
        })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Admin_ChangesAClosedMonthOnlyWithAReason_FlaggedInHistoryAndAuditList()
    {
        var (owner, member, slug, orgId, memberId) = await SetupAsync("cp-admin");
        var entryId = await ManualAsync(member, orgId, 14);
        (await CloseAsync(owner, slug, LastMonth, LastMonth)).EnsureSuccessStatusCode();
        var url = $"/api/v1/organizations/{slug}/members/{memberId}/entries/{entryId}";
        var body = new { endTime = Day(14).End.AddHours(1) };

        await ShouldBeClosedAsync(await owner.PutAsJsonAsync(url, body), "period.reasonRequired");
        (await owner.SendAsync(WithReason(HttpMethod.Put, url, body, "Korrektur nach Lohnabrechnung"))).EnsureSuccessStatusCode();

        var history = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/TimeTracking/{entryId}/history");
        var last = history.EnumerateArray().Last();
        last.GetProperty("inClosedPeriod").GetBoolean().Should().BeTrue();
        last.GetProperty("reason").GetString().Should().Be("Korrektur nach Lohnabrechnung");
        history[0].GetProperty("inClosedPeriod").GetBoolean().Should().BeFalse();

        // An absence for the member, and the admin's own manual entry, the same way
        var absence = WithReason(HttpMethod.Post, $"/api/v1/organizations/{slug}/absences/admin",
            new { userId = memberId, date = LastMonth.AddDays(20).ToString("yyyy-MM-dd"), type = 0 }, "Krankmeldung nachgereicht");
        (await owner.SendAsync(absence)).EnsureSuccessStatusCode();
        var (start, end) = Day(16);
        (await owner.SendAsync(WithReason(HttpMethod.Post, "/api/v1/TimeTracking/manual",
            new { startTime = start, endTime = end, organizationId = orgId }, "Own day"))).StatusCode.Should().Be(HttpStatusCode.Created);

        var events = (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/closed-periods"))
            .GetProperty("events").EnumerateArray().ToList();
        events.Select(e => e.GetProperty("kind").GetString()).Should().Equal("AdminChange", "AdminChange", "AdminChange", "Closed");
        events[2].GetProperty("reason").GetString().Should().Be("Korrektur nach Lohnabrechnung");
        events[2].GetProperty("memberName").GetString().Should().Be("Test User");
        events[2].GetProperty("summary").GetString().Should().Be("TimeEntry:1");
        events[1].GetProperty("summary").GetString().Should().Be("AbsenceDay:1");
    }

    [Fact]
    public async Task GroupLead_CannotApproveAPendingRequestIntoAClosedMonth_AdminCanWithAReason()
    {
        var (owner, member, slug, orgId, memberId) = await SetupAsync("cp-lead");
        var (lead, leadLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "cp-lead-l@closed.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, leadLogin)).EnsureSuccessStatusCode();
        await TestHelpers.CreateGroupAsync(owner, slug, "Team", new { groupAdminCanApproveTimeEdits = true },
            (memberId, "Member"), (leadLogin.User.Id, "Admin"));
        var entryId = await ManualAsync(member, orgId, 14);
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { editPastEntriesMode = 1 })).EnsureSuccessStatusCode();
        var created = await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests", new
        {
            type = 1, relatedEntityId = entryId, requestData = JsonSerializer.Serialize(new { endTime = Day(14).End.AddHours(-1) })
        });
        created.EnsureSuccessStatusCode();
        var requestId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await CloseAsync(owner, slug, LastMonth, LastMonth)).EnsureSuccessStatusCode();

        // The inbox hints at the closed month
        var inbox = await lead.GetFromJsonAsync<JsonElement>("/api/v1/organizations/notifications");
        inbox.GetProperty("requests").EnumerateArray().Single(r => r.GetProperty("id").GetInt32() == requestId)
            .GetProperty("closedMonth").GetString().Should().Be(LastMonth.ToString("yyyy-MM-dd"));

        var url = $"/api/v1/organizations/{slug}/requests/{requestId}";
        await ShouldBeClosedAsync(await lead.PutAsJsonAsync(url, new { accept = true }));
        await ShouldBeClosedAsync(await lead.SendAsync(WithReason(HttpMethod.Put, url, new { accept = true }, "I lead")));
        var list = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/requests");
        list.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetInt32() == requestId)
            .GetProperty("status").GetString().Should().Be("Pending");

        await ShouldBeClosedAsync(await owner.PutAsJsonAsync(url, new { accept = true }), "period.reasonRequired");
        (await owner.SendAsync(WithReason(HttpMethod.Put, url, new { accept = true }, "Approved after payroll"))).EnsureSuccessStatusCode();
        var history = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/TimeTracking/{entryId}/history");
        var last = history.EnumerateArray().Last();
        last.GetProperty("kind").GetString().Should().Be("ApprovedRequest");
        last.GetProperty("inClosedPeriod").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task WorkSchedules_OnlyChangesReachingIntoAClosedMonthAreRefused()
    {
        var (owner, member, slug, _, memberId) = await SetupAsync("cp-sched");
        var schedules = $"/api/v1/organizations/{slug}/work-schedules";
        var old = await member.PostAsJsonAsync(schedules, new { validFrom = LastMonth.AddMonths(-2).ToString("yyyy-MM-dd"), weeklyWorkHours = 40.0, distributeEvenly = true });
        old.EnsureSuccessStatusCode();
        var oldId = (await old.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await CloseAsync(owner, slug, LastMonth, LastMonth)).EnsureSuccessStatusCode();

        await ShouldBeClosedAsync(await member.PutAsJsonAsync($"{schedules}/{oldId}", new { weeklyWorkHours = 20.0, distributeEvenly = true }));
        await ShouldBeClosedAsync(await member.PutAsJsonAsync($"{schedules}/{oldId}", new { validTo = LastMonth.AddDays(9).ToString("yyyy-MM-dd") }));
        await ShouldBeClosedAsync(await member.DeleteAsync($"{schedules}/{oldId}"));
        // Ending the old period after the closed month (a new one from then on) is fine
        (await member.PostAsJsonAsync(schedules, new { validFrom = LastMonth.AddMonths(2).ToString("yyyy-MM-dd"), weeklyWorkHours = 30.0, distributeEvenly = true }))
            .EnsureSuccessStatusCode();
        // Moving the start of the old period inside the months before the closed one too
        (await member.PutAsJsonAsync($"{schedules}/{oldId}", new { validFrom = LastMonth.AddMonths(-2).AddDays(3).ToString("yyyy-MM-dd") }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        // An admin with a reason may
        (await owner.SendAsync(WithReason(HttpMethod.Put, $"/api/v1/organizations/{slug}/members/{memberId}/work-schedules/{oldId}",
            new { weeklyWorkHours = 20.0, distributeEvenly = true }, "Vertrag rückwirkend"))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Terminal_RefusesScansIntoAClosedMonth_StopsARunningTimer_AutoStopStillWorks()
    {
        var (owner, member, slug, orgId, memberId) = await SetupAsync("cp-term");
        var key = (await (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/api-keys", new { name = "Door" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString()!;
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/cards", new { cardId = "CP-CARD-1" })).EnsureSuccessStatusCode();
        Task<HttpResponseMessage> Punch(DateTime? scannedAt = null)
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", key);
            return client.PostAsJsonAsync("/api/v1/terminal/punch", new { badgeId = "CP-CARD-1", scannedAt });
        }

        // A timer runs; then this month is closed (the API only closes ended months, so seed the row)
        (await Punch(DateTime.UtcNow.AddMinutes(-90))).EnsureSuccessStatusCode();
        var thisMonth = ClosedPeriods.MonthOf(OrgClock.For((string?)null).Today());
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            db.ClosedPeriods.Add(new ClosedPeriod { OrganizationId = orgId, Month = thisMonth, ClosedAt = DateTime.UtcNow });
            db.ClosedPeriods.Add(new ClosedPeriod { OrganizationId = orgId, Month = thisMonth.AddMonths(-1), ClosedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        (await Punch(DateTime.UtcNow.AddMinutes(-60))).StatusCode.Should().Be(HttpStatusCode.OK);   // stopping is fine
        var refused = await Punch(DateTime.UtcNow.AddMinutes(-30));                                  // a queued start is not
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("period.closed");
        (await Punch()).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var punches = await owner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/organizations/{slug}/terminal/punches");
        punches!.Select(p => p.GetProperty("result").GetString()).Should().Equal("Rejected", "Rejected", "Stopped", "Started");

        // The business-day-end auto-stop is system work: a timer in a closed day still stops
        int runningId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            var start = DateTime.UtcNow.AddDays(-2);
            var running = new TimeEntry { UserId = memberId, OrganizationId = orgId, StartTime = start, IsRunning = true, CreatedAt = start, UpdatedAt = start };
            db.TimeEntries.Add(running);
            await db.SaveChangesAsync();
            runningId = running.Id;
            await FailureEntryService.CloseOverdueEntriesAsync(db, DateTime.UtcNow);
        }
        using (var scope = _factory.Services.CreateScope())
        {
            var stopped = await scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>().TimeEntries.AsNoTracking().FirstAsync(e => e.Id == runningId);
            stopped.IsRunning.Should().BeFalse();
            stopped.IsFailure.Should().BeTrue();
        }
        // … and the member cannot correct it there any more
        await ShouldBeClosedOrThisMonthAsync(await member.PutAsJsonAsync($"/api/v1/TimeTracking/{runningId}",
            new { endTime = DateTime.UtcNow.AddDays(-2).AddHours(8) }), thisMonth);
    }

    [Fact]
    public async Task RunningEntry_OnlyAPureStopPassesTheGuard_EditRequestsAreRefused()
    {
        var (owner, member, slug, orgId, memberId) = await SetupAsync("cp-running");
        var start = DateTime.UtcNow.AddMinutes(-30);
        int runningId, pendingId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            var running = new TimeEntry { UserId = memberId, OrganizationId = orgId, StartTime = start, IsRunning = true, CreatedAt = start, UpdatedAt = start };
            db.TimeEntries.Add(running);
            await db.SaveChangesAsync();
            runningId = running.Id;
            // A request from before the timer was (re)started, still pending
            var pending = new OrgRequest
            {
                UserId = memberId, OrganizationId = orgId, Type = RequestType.EditPastEntry, RelatedEntityId = runningId,
                RequestData = JsonSerializer.Serialize(new { endTime = DateTime.UtcNow }), Status = RequestStatus.Pending
            };
            db.OrgRequests.Add(pending);
            await db.SaveChangesAsync();
            pendingId = pending.Id;
        }
        (await CloseAsync(owner, slug, LastMonth, LastMonth)).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { editPastEntriesMode = 1 })).EnsureSuccessStatusCode();

        // A running entry is stopped, not edited: no new request, and the pending one cannot be applied
        var created = await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests", new
        {
            type = 1, relatedEntityId = runningId, requestData = JsonSerializer.Serialize(new { startTime = Day(14).Start })
        });
        created.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("entry.running");
        var applied = await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{pendingId}", new { accept = true });
        applied.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await applied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("request.cannotApply");

        // The guard: stopping while moving the start into the closed month is a change, a pure stop is not
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<RlsContext>().ActAsUser(memberId);
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            var entry = await db.TimeEntries.FirstAsync(e => e.Id == runningId);
            (entry.StartTime, entry.EndTime, entry.IsRunning) = (Day(14).Start, Day(14).End, false);
            (await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<PeriodClosedException>())
                .Which.Month.Should().Be(LastMonth);
            (entry.StartTime, entry.EndTime) = (start, DateTime.UtcNow);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Guard_UsesTheOrgTimeZone_AtTheMonthBoundary()
    {
        var (owner, member, slug, orgId, _) = await TestHelpers.OrgWithMemberAsync(_factory, "cp-ny",
            new { editPastEntriesMode = Allowed, timeZone = "America/New_York" }, "closed.test");
        (await CloseAsync(owner, slug, LastMonth, LastMonth)).EnsureSuccessStatusCode();
        var ny = OrgClock.For("America/New_York");

        // 23:30 in New York on the last day of the closed month is already the next month in UTC (and in Berlin)
        var lateStart = ny.ToUtc(LastMonth.AddMonths(1).AddDays(-1).ToDateTime(new TimeOnly(23, 30)));
        lateStart.Month.Should().Be(LastMonth.AddMonths(1).Month);
        await ShouldBeClosedAsync(await member.PostAsJsonAsync("/api/v1/TimeTracking/manual",
            new { startTime = lateStart, endTime = lateStart.AddMinutes(20), organizationId = orgId }));

        // 00:30 on the first day of the next month is open
        var nextStart = ny.ToUtc(LastMonth.AddMonths(1).ToDateTime(new TimeOnly(0, 30)));
        (await member.PostAsJsonAsync("/api/v1/TimeTracking/manual",
            new { startTime = nextStart, endTime = nextStart.AddMinutes(20), organizationId = orgId })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task ShouldBeClosedOrThisMonthAsync(HttpResponseMessage response, DateOnly month)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("period.closed");
        problem.GetProperty("month").GetString().Should().BeOneOf(month.ToString("yyyy-MM"), month.AddMonths(-1).ToString("yyyy-MM"));
    }
}
