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
/// Core working hours: the override rule, detection (late/early/gap/missing, tolerance), the exemptions
/// (day off, holiday, absence, half day), the once-per-day background check and its recipients.
/// </summary>
public class CoreHoursTests : IClassFixture<TimeTrackingApiFactory>
{
    private static readonly OrgClock Berlin = OrgClock.For("Europe/Berlin");
    private readonly TimeTrackingApiFactory _factory;

    public CoreHoursTests(TimeTrackingApiFactory factory) => _factory = factory;

    private static readonly object CoreSettings = new
    {
        coreHoursEnabled = true, coreHoursStart = "09:00", coreHoursEnd = "15:00", coreHoursToleranceMinutes = 5, timeZone = "Europe/Berlin"
    };

    /// <summary>The same, with admins allowed to see members' hours — only then are org admins notified.</summary>
    private static readonly object CoreSettingsAdminsSeeTimes = new
    {
        coreHoursEnabled = true, coreHoursStart = "09:00", coreHoursEnd = "15:00", coreHoursToleranceMinutes = 5, timeZone = "Europe/Berlin",
        memberTimeEntryVisibility = true
    };

    // ── Pure rules ──────────────────────────────────────────────────────

    private static readonly DateTime From = new(2026, 3, 3, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 3, 3, 14, 0, 0, DateTimeKind.Utc);

    private static (DateTime, DateTime) Span(int fromMinute, int toMinute) => (From.AddMinutes(fromMinute), From.AddMinutes(toMinute));

    [Theory]
    [InlineData(0, 360, CoreHoursIssue.None, 0)]
    [InlineData(5, 355, CoreHoursIssue.None, 10)]      // within tolerance at both ends
    [InlineData(6, 360, CoreHoursIssue.Late, 6)]
    [InlineData(-60, 354, CoreHoursIssue.Early, 6)]
    [InlineData(30, 300, CoreHoursIssue.Late | CoreHoursIssue.Early, 90)]
    public void Evaluate_OneEntry(int from, int to, CoreHoursIssue issues, int missing)
    {
        CoreHours.Evaluate([Span(from, to)], From, To, 5).Should().Be((issues, missing));
    }

    [Fact]
    public void Evaluate_GapsOverlapsAndNothing()
    {
        CoreHours.Evaluate([Span(0, 120), Span(126, 360)], From, To, 5).Should().Be((CoreHoursIssue.Gap, 6));
        CoreHours.Evaluate([Span(0, 120), Span(125, 360)], From, To, 5).Should().Be((CoreHoursIssue.None, 5));
        CoreHours.Evaluate([Span(100, 360), Span(0, 200)], From, To, 0).Should().Be((CoreHoursIssue.None, 0));   // overlapping, unordered
        CoreHours.Evaluate([Span(400, 500)], From, To, 5).Should().Be((CoreHoursIssue.Missing, 360));
        CoreHours.Evaluate([], From, To, 5).Should().Be((CoreHoursIssue.Missing, 360));
        CoreHours.IssueNames(CoreHoursIssue.Late | CoreHoursIssue.Gap).Should().Equal("late", "gap");
    }

    [Fact]
    public void EvaluateDay_HalfDay_TakesTheBetterCoveredHalf()
    {
        var window = new CoreWindow(new TimeOnly(9, 0), new TimeOnly(15, 0));
        var day = new DateOnly(2026, 3, 3);
        // Morning only (09:00-12:00 local = 08:00-11:00 UTC)
        var morning = CoreHours.EvaluateDay([Span(0, 180)], day, window, halfDay: true, 5, Berlin);
        morning.Issues.Should().Be(CoreHoursIssue.None);
        morning.Window.ToString().Should().Be("09:00-12:00");
        // Nothing: reported against the afternoon half
        var none = CoreHours.EvaluateDay([], day, window, halfDay: true, 5, Berlin);
        none.Should().Be(new CoreHours.Result(CoreHoursIssue.Missing, 180, new CoreWindow(new TimeOnly(12, 0), new TimeOnly(15, 0))));
        // Without the half day, the morning alone leaves early
        CoreHours.EvaluateDay([Span(0, 180)], day, window, halfDay: false, 5, Berlin).Issues.Should().Be(CoreHoursIssue.Early);
    }

    private static Organization Org(bool enabled = true) => new()
    {
        Name = "o", Slug = "o", CoreHoursEnabled = enabled, CoreHoursStart = new TimeOnly(9, 0), CoreHoursEnd = new TimeOnly(15, 0)
    };

    private static Group G(CoreHoursMode? mode, int? start = null, int? end = null) => new()
    {
        Name = "g", CoreHoursMode = mode,
        CoreHoursStart = start is int s ? new TimeOnly(s, 0) : null, CoreHoursEnd = end is int e ? new TimeOnly(e, 0) : null
    };

    [Fact]
    public void CombineCoreHours_GroupOverride_MostPermissiveWins()
    {
        var org = new CoreWindow(new TimeOnly(9, 0), new TimeOnly(15, 0));
        EffectiveRuleResolver.CombineCoreHours(Org(), []).Should().Be(org);
        EffectiveRuleResolver.CombineCoreHours(Org(), [G(null)]).Should().Be(org);                      // inherit
        EffectiveRuleResolver.CombineCoreHours(Org(enabled: false), [G(null)]).Should().BeNull();
        EffectiveRuleResolver.CombineCoreHours(Org(enabled: false), [G(CoreHoursMode.Own, 8, 16)])       // a group may be stricter
            .Should().Be(new CoreWindow(new TimeOnly(8, 0), new TimeOnly(16, 0)));
        EffectiveRuleResolver.CombineCoreHours(Org(), [G(CoreHoursMode.Own, 8, 16), G(CoreHoursMode.Off)]).Should().BeNull();
        EffectiveRuleResolver.CombineCoreHours(Org(), [G(CoreHoursMode.Own, 8, 13), G(CoreHoursMode.Own, 10, 16), G(null)])
            .Should().Be(new CoreWindow(new TimeOnly(10, 0), new TimeOnly(13, 0)));                    // shortest: latest start, earliest end
        EffectiveRuleResolver.CombineCoreHours(Org(), [G(CoreHoursMode.Own, 8, 10), G(CoreHoursMode.Own, 12, 16)]).Should().BeNull();
    }

    // ── Background check ───────────────────────────────────────────────

    private async Task WithDbAsync(Func<TimeTrackingDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>());
    }

    /// <summary>An entry from local <paramref name="from"/> to <paramref name="to"/> (HH:mm) on the day, in Berlin.</summary>
    private Task AddEntryAsync(int userId, int orgId, DateOnly day, string from, string? to, bool failure = false, string zone = "Europe/Berlin") =>
        WithDbAsync(async db =>
        {
            var clock = OrgClock.For(zone);
            var start = clock.ToUtc(day.ToDateTime(TimeOnly.Parse(from)));
            db.TimeEntries.Add(new TimeEntry
            {
                UserId = userId, OrganizationId = orgId, StartTime = start,
                EndTime = to == null ? null : clock.ToUtc(day.ToDateTime(TimeOnly.Parse(to))),
                IsRunning = to == null, IsFailure = failure, CreatedAt = start, UpdatedAt = start
            });
            await db.SaveChangesAsync();
        });

    private async Task<int> CheckAsync(DateTime nowUtc)
    {
        var result = 0;
        await WithDbAsync(async db => result = await CoreHours.CheckDueAsync(db, nowUtc, _factory.Mail, "https://app.test"));
        return result;
    }

    private async Task<CoreHoursCheck?> CheckRowAsync(int userId, DateOnly day)
    {
        CoreHoursCheck? row = null;
        await WithDbAsync(async db => row = await db.CoreHoursChecks.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId && c.Date == day));
        return row;
    }

    private async Task<List<Notification>> NoticesAboutAsync(int memberId, DateOnly day)
    {
        var date = day.ToString("dd.MM.yyyy");
        List<Notification> list = [];
        await WithDbAsync(async db => list = await db.Notifications.AsNoTracking()
            .Where(n => n.Type == CoreHours.NotificationType && n.RelatedEntityId == memberId && n.Message.Contains(date)).ToListAsync());
        return list;
    }

    /// <summary>15:30 local on <paramref name="day"/> in Berlin: after 15:00 + 5 min tolerance.</summary>
    private static DateTime After(DateOnly day) => Berlin.ToUtc(day.ToDateTime(new TimeOnly(15, 30)));

    [Fact]
    public async Task LateStart_IsNotifiedOnce_ToMemberAndOwner_WithMail()
    {
        var day = new DateOnly(2026, 3, 3);   // Tuesday
        var (_, _, _, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "core-late", CoreSettingsAdminsSeeTimes);
        await AddEntryAsync(memberId, orgId, day, "09:20", "15:10");

        // Not yet due at 15:04 (end + tolerance)
        await CheckAsync(Berlin.ToUtc(day.ToDateTime(new TimeOnly(15, 4))));
        (await CheckRowAsync(memberId, day)).Should().BeNull();

        (await CheckAsync(After(day))).Should().BeGreaterThan(0);
        var row = await CheckRowAsync(memberId, day);
        row!.Issues.Should().Be(CoreHoursIssue.Late);
        row.MissingMinutes.Should().Be(20);
        row.NotifiedAt.Should().NotBeNull();

        var notices = await NoticesAboutAsync(memberId, day);
        notices.Select(n => n.UserId).Should().BeEquivalentTo([memberId, await UserIdAsync("core-late-owner@t.test")]);
        notices[0].Message.Should().Contain("09:00-15:00").And.Contain("late start").And.Contain("20 min");
        _factory.Mail.Sent.Should().Contain(m => m.To == "core-late-m@t.test" && m.Body.Contains("/history?date=2026-03-03") && m.Subject.StartsWith("Kernarbeitszeit"));
        _factory.Mail.Sent.Should().Contain(m => m.To == "core-late-owner@t.test" && m.Body.Contains($"/organizations/core-late-org/members/{memberId}"));

        // Once per day
        await CheckAsync(After(day).AddHours(2));
        (await NoticesAboutAsync(memberId, day)).Should().HaveCount(2);
    }

    private async Task<int> UserIdAsync(string email)
    {
        var id = 0;
        await WithDbAsync(async db => id = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        return id;
    }

    [Fact]
    public async Task WithoutAdminTimeVisibility_OnlyTheMemberAndEntryViewingLeadsAreNotified()
    {
        var day = new DateOnly(2026, 3, 2);   // Monday
        var (owner, _, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "core-private", CoreSettings);
        var (_, lead) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "core-private-lead@t.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, lead)).EnsureSuccessStatusCode();
        var (_, viewer) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "core-private-viewer@t.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, viewer)).EnsureSuccessStatusCode();
        // The lead's team may not view entries (explicitly — it would inherit the department's right); the department's admin may
        var dept = await TestHelpers.CreateGroupAsync(owner, slug, "Dept", new { groupAdminCanViewAllTimeEntries = true }, (viewer.User.Id, "Admin"));
        var team = await TestHelpers.CreateGroupAsync(owner, slug, "Team", new { groupAdminCanViewAllTimeEntries = false }, (memberId, "Member"), (lead.User.Id, "Admin"));
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{team}", new { name = "Team", parentGroupId = dept })).EnsureSuccessStatusCode();
        await AddEntryAsync(memberId, orgId, day, "09:30", "15:00");
        await AddEntryAsync(lead.User.Id, orgId, day, "09:00", "15:00");
        await AddEntryAsync(viewer.User.Id, orgId, day, "09:00", "15:00");

        await CheckAsync(After(day));
        (await NoticesAboutAsync(memberId, day)).Select(n => n.UserId).Should().BeEquivalentTo([memberId, viewer.User.Id]);
        _factory.Mail.Sent.Should().Contain(m => m.To == "core-private-m@t.test" && m.Body.Contains("09:00-15:00"));
        // Mails about the member go to exactly the notified colleagues
        _factory.Mail.Sent.Where(m => m.Body.Contains($"/members/{memberId}")).Select(m => m.To).Should().BeEquivalentTo(["core-private-viewer@t.test"]);
    }

    [Fact]
    public async Task CoveredDay_IsStoredWithoutIssues_AndNotNotified()
    {
        var day = new DateOnly(2026, 3, 4);
        var (_, _, _, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "core-ok", CoreSettings);
        await AddEntryAsync(memberId, orgId, day, "09:04", "12:00");
        await AddEntryAsync(memberId, orgId, day, "12:05", "14:56");   // 5 min gap, 4 min early: within tolerance

        await CheckAsync(After(day));
        (await CheckRowAsync(memberId, day))!.Issues.Should().Be(CoreHoursIssue.None);
        (await NoticesAboutAsync(memberId, day)).Should().BeEmpty();
    }

    [Fact]
    public async Task EarlyLeaveAndGap_AreDetected()
    {
        var day = new DateOnly(2026, 3, 5);
        var (_, _, _, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "core-gap", CoreSettings);
        await AddEntryAsync(memberId, orgId, day, "08:00", "11:00");
        await AddEntryAsync(memberId, orgId, day, "11:30", "14:00");

        await CheckAsync(After(day));
        var row = await CheckRowAsync(memberId, day);
        row!.Issues.Should().Be(CoreHoursIssue.Early | CoreHoursIssue.Gap);
        row.MissingMinutes.Should().Be(90);
    }

    [Fact]
    public async Task RunningTimer_Covers_FailureEntry_DoesNot()
    {
        var day = new DateOnly(2026, 3, 6);
        var (_, _, _, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "core-run", CoreSettings);
        var ownerId = await UserIdAsync("core-run-owner@t.test");
        await AddEntryAsync(memberId, orgId, day, "08:55", null);                    // still running at check time
        await AddEntryAsync(ownerId, orgId, day, "08:55", "16:00", failure: true);  // auto-stopped, uncorrected

        await CheckAsync(After(day));
        (await CheckRowAsync(memberId, day))!.Issues.Should().Be(CoreHoursIssue.None);
        (await CheckRowAsync(ownerId, day))!.Issues.Should().Be(CoreHoursIssue.Missing);
    }

    [Fact]
    public async Task Holiday_FullAbsence_AndDaysOff_AreExempt_HalfDayChecksOneHalf()
    {
        var (_, _, _, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "core-exempt", CoreSettings);
        var holiday = new DateOnly(2026, 3, 9);
        var absent = new DateOnly(2026, 3, 10);
        var halfMorningWorked = new DateOnly(2026, 3, 11);
        var halfNothing = new DateOnly(2026, 3, 12);
        var saturday = new DateOnly(2026, 3, 14);
        await WithDbAsync(async db =>
        {
            db.Holidays.Add(new Holiday { OrganizationId = orgId, Date = holiday, Name = "Feiertag" });
            db.AbsenceDays.Add(new AbsenceDay { OrganizationId = orgId, UserId = memberId, Date = absent, Type = AbsenceType.Vacation });
            db.AbsenceDays.Add(new AbsenceDay { OrganizationId = orgId, UserId = memberId, Date = halfMorningWorked, Type = AbsenceType.Vacation, IsHalfDay = true });
            db.AbsenceDays.Add(new AbsenceDay { OrganizationId = orgId, UserId = memberId, Date = halfNothing, Type = AbsenceType.Vacation, IsHalfDay = true });
            await db.SaveChangesAsync();
        });
        await AddEntryAsync(memberId, orgId, halfMorningWorked, "09:00", "12:00");

        foreach (var day in new[] { holiday, absent, halfMorningWorked, halfNothing, saturday })
            await CheckAsync(After(day));

        foreach (var day in new[] { holiday, absent, halfMorningWorked, saturday })
            (await CheckRowAsync(memberId, day))!.Issues.Should().Be(CoreHoursIssue.None, $"{day} is exempt or covered");
        var half = await CheckRowAsync(memberId, halfNothing);
        half!.HalfDay.Should().BeTrue();
        half.Issues.Should().Be(CoreHoursIssue.Missing);
        half.MissingMinutes.Should().Be(180);
    }

    [Fact]
    public async Task WeekendSchedule_MakesSaturdayACoreDay()
    {
        var saturday = new DateOnly(2026, 3, 21);
        var (_, _, _, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "core-weekend", CoreSettings);
        await WithDbAsync(async db =>
        {
            db.WorkSchedules.Add(new WorkSchedule { OrganizationId = orgId, UserId = memberId, ValidFrom = new DateOnly(2026, 1, 1), TargetSat = 8, TargetSun = 8 });
            await db.SaveChangesAsync();
        });

        await CheckAsync(After(saturday));
        (await CheckRowAsync(memberId, saturday))!.Issues.Should().Be(CoreHoursIssue.Missing);
        // Tuesday has no target in that schedule → day off
        await CheckAsync(After(new DateOnly(2026, 3, 17)));
        (await CheckRowAsync(memberId, new DateOnly(2026, 3, 17)))!.Issues.Should().Be(CoreHoursIssue.None);
    }

    [Fact]
    public async Task GroupOverride_OwnHoursAndOff_AndGroupAdminsAreNotified()
    {
        var day = new DateOnly(2026, 3, 24);
        var (owner, _, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "core-group", CoreSettings);
        var (_, lead) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "core-group-lead@t.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, lead)).EnsureSuccessStatusCode();
        var (_, off) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "core-group-off@t.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, off)).EnsureSuccessStatusCode();

        await TestHelpers.CreateGroupAsync(owner, slug, "Short core",
            new { coreHoursMode = 1, coreHoursStart = "10:00", coreHoursEnd = "14:00", groupAdminCanViewAllTimeEntries = true },
            (memberId, "Member"), (lead.User.Id, "Admin"));
        await TestHelpers.CreateGroupAsync(owner, slug, "No core", new { coreHoursMode = 0 }, (off.User.Id, "Member"));
        var perms = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}/groups/permissions");
        perms.EnumerateArray().Select(g => g.GetProperty("coreHoursMode").GetString()).Should().BeEquivalentTo(["Own", "Off"]);

        await AddEntryAsync(memberId, orgId, day, "10:00", "12:00");
        await AddEntryAsync(lead.User.Id, orgId, day, "10:00", "14:00");   // the group admin covers the group window

        await CheckAsync(Berlin.ToUtc(day.ToDateTime(new TimeOnly(14, 10))));   // after the group window, before the org's
        var row = await CheckRowAsync(memberId, day);
        row!.WindowStart.Should().Be(new TimeOnly(10, 0));
        row.Issues.Should().Be(CoreHoursIssue.Early);
        (await CheckRowAsync(lead.User.Id, day))!.Issues.Should().Be(CoreHoursIssue.None);
        (await CheckRowAsync(off.User.Id, day)).Should().BeNull("core hours are off for that group");

        // Admins may not see members' hours here: the member and the lead who may view entries, not the owner
        (await NoticesAboutAsync(memberId, day)).Select(n => n.UserId).Should().BeEquivalentTo([memberId, lead.User.Id]);

        // Back to inherit: the org window applies again
        var groupId = perms.EnumerateArray().First(g => g.GetProperty("coreHoursMode").GetString() == "Off").GetProperty("groupId").GetInt32();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/permissions", new { coreHoursMode = -1 })).EnsureSuccessStatusCode();
        await CheckAsync(After(day.AddDays(1)));
        (await CheckRowAsync(off.User.Id, day.AddDays(1)))!.Issues.Should().Be(CoreHoursIssue.Missing);
    }

    [Fact]
    public async Task OrgLocalTimeZone_DecidesDayAndWindow()
    {
        var day = new DateOnly(2026, 3, 25);
        var (_, _, _, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "core-ny",
            new { coreHoursEnabled = true, coreHoursStart = "09:00", coreHoursEnd = "15:00", timeZone = "America/New_York" });
        await AddEntryAsync(memberId, orgId, day, "09:00", "15:00", zone: "America/New_York");

        var ny = OrgClock.For("America/New_York");
        await CheckAsync(After(day));   // 15:30 Berlin = morning in New York: not due
        (await CheckRowAsync(memberId, day)).Should().BeNull();
        await CheckAsync(ny.ToUtc(day.ToDateTime(new TimeOnly(15, 6))));
        (await CheckRowAsync(memberId, day))!.Issues.Should().Be(CoreHoursIssue.None);
    }

    // ── Settings, compliance, reports ──────────────────────────────────

    [Fact]
    public async Task Settings_RoundTripAndValidate()
    {
        var (owner, _, slug, _, _) = await TestHelpers.OrgWithMemberAsync(_factory, "core-set", new { });
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}");
        detail.GetProperty("coreHoursEnabled").GetBoolean().Should().BeFalse();
        detail.GetProperty("coreHoursStart").GetString().Should().Be("09:00");
        detail.GetProperty("coreHoursToleranceMinutes").GetInt32().Should().Be(5);

        var saved = await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings",
            new { coreHoursEnabled = true, coreHoursStart = "08:30", coreHoursEnd = "14:00", coreHoursToleranceMinutes = 10 });
        saved.EnsureSuccessStatusCode();
        (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("coreHoursStart").GetString().Should().Be("08:30");

        // End before start is a night window; an equal end is not a window
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { coreHoursEnd = "02:00" })).EnsureSuccessStatusCode();
        var bad = await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { coreHoursEnd = "08:30" });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("org.coreHoursOrder");

        var group = await TestHelpers.CreateGroupAsync(owner, slug, "G", new { });
        var badGroup = await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{group}/permissions", new { coreHoursMode = 1, coreHoursStart = "12:00" });
        badGroup.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await badGroup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("org.coreHoursOrder");
        var emptyGroup = await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{group}/permissions", new { coreHoursMode = 1, coreHoursStart = "22:00", coreHoursEnd = "22:00" });
        (await emptyGroup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("org.coreHoursOrder");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{group}/permissions", new { coreHoursMode = 1, coreHoursStart = "22:00", coreHoursEnd = "06:00" }))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Compliance_AndDailyReport_ShowOpenViolations_UntilCorrected()
    {
        var day = new DateOnly(2026, 3, 26);
        var (owner, member, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "core-view",
            new { coreHoursEnabled = true, coreHoursStart = "09:00", coreHoursEnd = "15:00", timeZone = "Europe/Berlin", memberTimeEntryVisibility = true });
        await AddEntryAsync(memberId, orgId, day, "10:00", "15:00");
        await CheckAsync(After(day));

        var url = $"/api/v1/organizations/{slug}/members/{memberId}/compliance?from=2026-03-01&to=2026-03-31";
        var days = await owner.GetFromJsonAsync<List<JsonElement>>(url);
        var flagged = days!.Single();
        flagged.GetProperty("date").GetString().Should().Be("2026-03-26");
        flagged.GetProperty("coreHours").GetBoolean().Should().BeTrue();
        flagged.GetProperty("coreHoursIssues").EnumerateArray().Select(i => i.GetString()).Should().Equal("late");
        flagged.GetProperty("coreHoursMissingMinutes").GetInt32().Should().Be(60);
        flagged.GetProperty("coreHoursWindow").GetString().Should().Be("09:00-15:00");

        var csv = await (await member.PostAsJsonAsync("/api/v1/ExportImport/export",
            new { lang = "en", type = "daily", organizationSlug = slug, from = "2026-03-26", to = "2026-03-26" })).Content.ReadAsStringAsync();
        csv.Split('\n').Single(l => l.StartsWith("2026-03-26")).Should().Contain("Core hours missed");

        // The member fills the morning: the marker goes, the stored check stays
        await AddEntryAsync(memberId, orgId, day, "09:00", "10:00");
        (await owner.GetFromJsonAsync<List<JsonElement>>(url)).Should().BeEmpty();
        (await CheckRowAsync(memberId, day))!.Issues.Should().Be(CoreHoursIssue.Late);
    }

    // ── Night windows (past midnight) ─────────────────────────────────

    private static CoreWindow W(int startHour, int startMinute, int endHour, int endMinute) =>
        new(new TimeOnly(startHour, startMinute), new TimeOnly(endHour, endMinute));

    private static double Hours(CoreWindow window, string day, OrgClock clock)
    {
        var (from, to) = window.Utc(DateOnly.Parse(day), clock);
        return (to - from).TotalHours;
    }

    [Fact]
    public void NightWindow_BelongsToItsStartDay_AndIsMeasuredInRealTime()
    {
        var night = W(22, 0, 6, 0);
        night.Overnight.Should().BeTrue();
        W(9, 0, 15, 0).Overnight.Should().BeFalse();
        night.Utc(new DateOnly(2026, 3, 24), Berlin).Should().Be(
            (new DateTime(2026, 3, 24, 21, 0, 0, DateTimeKind.Utc), new DateTime(2026, 3, 25, 5, 0, 0, DateTimeKind.Utc)));
        // Europe/Berlin: spring forward into Sunday 29 March (7 h), fall back into Sunday 25 October (9 h)
        Hours(night, "2026-03-24", Berlin).Should().Be(8);
        Hours(night, "2026-03-28", Berlin).Should().Be(7);
        Hours(night, "2026-10-24", Berlin).Should().Be(9);
        // An end inside the skipped hour moves forward to 03:00 CEST (01:00 UTC)
        W(22, 0, 2, 30).Utc(new DateOnly(2026, 3, 28), Berlin).ToUtc.Should().Be(new DateTime(2026, 3, 29, 1, 0, 0, DateTimeKind.Utc));
        // Other zones: New York changes on 8 March / 1 November, Tokyo never
        var newYork = OrgClock.For("America/New_York");
        Hours(night, "2026-03-07", newYork).Should().Be(7);
        Hours(night, "2026-10-31", newYork).Should().Be(9);
        Hours(night, "2026-03-28", OrgClock.For("Asia/Tokyo")).Should().Be(8);
    }

    [Fact]
    public void EvaluateDay_NightWindow_EntriesAcrossMidnightCover_HalfDaySplitsRealTime()
    {
        var night = W(22, 0, 6, 0);
        var day = new DateOnly(2026, 3, 28);   // the 7 h spring night
        var (from, to) = night.Utc(day, Berlin);
        CoreHours.EvaluateDay([(from, to)], day, night, halfDay: false, 5, Berlin).Issues.Should().Be(CoreHoursIssue.None);
        var early = CoreHours.EvaluateDay([(from, from.AddHours(3))], day, night, halfDay: false, 5, Berlin);
        early.Issues.Should().Be(CoreHoursIssue.Early);
        early.MissingMinutes.Should().Be(240);
        // Half day: 3.5 real hours each, split at 01:30 (still winter time)
        var half = CoreHours.EvaluateDay([(from, from.AddHours(3.5))], day, night, halfDay: true, 5, Berlin);
        half.Issues.Should().Be(CoreHoursIssue.None);
        half.Window.ToString().Should().Be("22:00-01:30");
    }

    [Fact]
    public void CombineCoreHours_NightWindows_IntersectOnTheStartDay()
    {
        EffectiveRuleResolver.CombineCoreHours(Org(), [G(CoreHoursMode.Own, 22, 6)]).Should().Be(W(22, 0, 6, 0));
        EffectiveRuleResolver.CombineCoreHours(Org(), [G(CoreHoursMode.Own, 22, 2), G(CoreHoursMode.Own, 23, 6)]).Should().Be(W(23, 0, 2, 0));
        EffectiveRuleResolver.CombineCoreHours(Org(), [G(CoreHoursMode.Own, 20, 23), G(CoreHoursMode.Own, 22, 6)]).Should().Be(W(22, 0, 23, 0));
        EffectiveRuleResolver.CombineCoreHours(Org(), [G(CoreHoursMode.Own, 9, 15), G(CoreHoursMode.Own, 22, 2)]).Should().BeNull();
    }

    [Fact]
    public void CombineCoreTolerance_GroupOverride_LargestWins()
    {
        Group T(int? minutes) => new() { Name = "t", CoreHoursToleranceMinutes = minutes };
        var org = Org();
        org.CoreHoursToleranceMinutes = 5;
        EffectiveRuleResolver.CombineCoreTolerance(org, []).Should().Be(5);
        EffectiveRuleResolver.CombineCoreTolerance(org, [T(null)]).Should().Be(5);
        EffectiveRuleResolver.CombineCoreTolerance(org, [T(0)]).Should().Be(0);   // a group may be stricter
        EffectiveRuleResolver.CombineCoreTolerance(org, [T(0), T(20), T(null)]).Should().Be(20);
    }

    /// <summary>An entry between two local times (may cross midnight).</summary>
    private Task AddSpanAsync(int userId, int orgId, DateTime fromLocal, DateTime toLocal, string zone = "Europe/Berlin") =>
        WithDbAsync(async db =>
        {
            var clock = OrgClock.For(zone);
            var start = clock.ToUtc(fromLocal);
            db.TimeEntries.Add(new TimeEntry
            {
                UserId = userId, OrganizationId = orgId, StartTime = start, EndTime = clock.ToUtc(toLocal),
                CreatedAt = start, UpdatedAt = start
            });
            await db.SaveChangesAsync();
        });

    private static DateTime At(DateOnly day, int hour, int minute = 0) => day.ToDateTime(new TimeOnly(hour, minute));

    [Fact]
    public async Task NightShift_IsCheckedTheNextMorning_AndBelongsToTheStartDay()
    {
        var day = new DateOnly(2026, 4, 7);   // Tuesday
        var (owner, _, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "core-night",
            new { coreHoursEnabled = true, coreHoursStart = "22:00", coreHoursEnd = "02:00", coreHoursToleranceMinutes = 5, timeZone = "Europe/Berlin", memberTimeEntryVisibility = true });
        var ownerId = await UserIdAsync("core-night-owner@t.test");
        await AddSpanAsync(memberId, orgId, At(day, 22), At(day.AddDays(1), 2));
        await WithDbAsync(async db =>
        {
            db.Holidays.Add(new Holiday { OrganizationId = orgId, Date = day.AddDays(1), Name = "Feiertag" });   // the next day
            await db.SaveChangesAsync();
        });

        await CheckAsync(Berlin.ToUtc(At(day, 23, 30)));
        (await CheckRowAsync(memberId, day)).Should().BeNull("the night has not ended yet");
        await CheckAsync(Berlin.ToUtc(At(day.AddDays(1), 2, 10)));
        (await CheckRowAsync(memberId, day))!.Issues.Should().Be(CoreHoursIssue.None, "the entry crosses midnight");
        var missed = await CheckRowAsync(ownerId, day);
        missed!.Issues.Should().Be(CoreHoursIssue.Missing, "a holiday on the next day does not exempt the night before");
        missed.MissingMinutes.Should().Be(240);
        (await NoticesAboutAsync(ownerId, day)).Should().ContainSingle().Which.Message.Should().Contain("07.04.2026 (22:00-02:00)");

        var compliance = await owner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/organizations/{slug}/members/{ownerId}/compliance?from=2026-04-01&to=2026-04-30");
        compliance!.Single().GetProperty("date").GetString().Should().Be("2026-04-07");
        compliance!.Single().GetProperty("coreHoursWindow").GetString().Should().Be("22:00-02:00");

        // The holiday night itself (starting on the holiday) is exempt
        await CheckAsync(Berlin.ToUtc(At(day.AddDays(2), 2, 10)));
        (await CheckRowAsync(ownerId, day.AddDays(1)))!.Issues.Should().Be(CoreHoursIssue.None);
    }

    [Theory]
    [InlineData("2026-03-28", 420)]   // spring forward: the night is 7 h long
    [InlineData("2026-10-24", 540)]   // fall back: 9 h
    public async Task NightShift_OnDstChangeNights_IsMeasuredInRealTime(string date, int minutes)
    {
        var day = DateOnly.Parse(date);   // a Saturday
        var prefix = $"core-dst-{minutes}";
        var (owner, _, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, prefix,
            new { coreHoursEnabled = true, coreHoursStart = "22:00", coreHoursEnd = "06:00", coreHoursToleranceMinutes = 5, timeZone = "Europe/Berlin" });
        var (_, worker) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-w@t.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, worker)).EnsureSuccessStatusCode();
        await WithDbAsync(async db =>
        {
            foreach (var id in new[] { memberId, worker.User.Id })
                db.WorkSchedules.Add(new WorkSchedule { OrganizationId = orgId, UserId = id, ValidFrom = new DateOnly(2026, 1, 1), TargetSat = 8 });
            await db.SaveChangesAsync();
        });
        await AddSpanAsync(worker.User.Id, orgId, At(day, 22), At(day.AddDays(1), 6));

        await CheckAsync(Berlin.ToUtc(At(day.AddDays(1), 6, 10)));
        var nothing = await CheckRowAsync(memberId, day);
        nothing!.Issues.Should().Be(CoreHoursIssue.Missing);
        nothing.MissingMinutes.Should().Be(minutes);
        (await CheckRowAsync(worker.User.Id, day))!.Issues.Should().Be(CoreHoursIssue.None);
    }

    // ── Catch-up ───────────────────────────────────────────────────────

    /// <summary>As if the org's core hours and memberships had been in place long before (the API stamps "now").</summary>
    private Task BackdateAsync(int orgId, DateTime? changedAt = null, DateTime? joinedAt = null) =>
        WithDbAsync(async db =>
        {
            var org = await db.Organizations.SingleAsync(o => o.Id == orgId);
            org.CoreHoursChangedAt = changedAt;
            foreach (var m in await db.UserOrganizations.Where(uo => uo.OrganizationId == orgId).ToListAsync())
                m.JoinedAt = joinedAt ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        });

    [Fact]
    public async Task CatchUp_ChecksThreeDaysBack_Once_MarkedAsLate()
    {
        var monday = new DateOnly(2026, 4, 13);
        var (_, _, _, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "core-catch", CoreSettingsAdminsSeeTimes);
        await BackdateAsync(orgId);
        await AddEntryAsync(memberId, orgId, monday.AddDays(1), "09:30", "15:00");

        // Down since Monday morning, back on Friday at 10:00: Tuesday to Thursday are caught up, Monday is too old
        var friday = monday.AddDays(4);
        await CheckAsync(Berlin.ToUtc(At(friday, 10)));
        (await CheckRowAsync(memberId, monday)).Should().BeNull();
        var tuesday = await CheckRowAsync(memberId, monday.AddDays(1));
        tuesday!.Issues.Should().Be(CoreHoursIssue.Late);
        tuesday.CheckedLate.Should().BeTrue();
        (await CheckRowAsync(memberId, monday.AddDays(3)))!.Issues.Should().Be(CoreHoursIssue.Missing);
        (await CheckRowAsync(memberId, friday)).Should().BeNull("Friday's window is not over");

        var notices = await NoticesAboutAsync(memberId, monday.AddDays(1));
        notices.Should().HaveCount(2).And.OnlyContain(n => n.Message.EndsWith("min not covered (checked late)."));
        _factory.Mail.Sent.Should().Contain(m => m.To == "core-catch-m@t.test" && m.Body.Contains("14.04.2026") && m.Body.Contains("nachträglich geprüft"));

        // Once: a second pass adds nothing; Friday on time is not marked
        await CheckAsync(Berlin.ToUtc(At(friday, 10, 1)));
        (await NoticesAboutAsync(memberId, monday.AddDays(1))).Should().HaveCount(2);
        await CheckAsync(After(friday));
        var onTime = await CheckRowAsync(memberId, friday);
        onTime!.CheckedLate.Should().BeFalse();
        (await NoticesAboutAsync(memberId, friday)).Should().OnlyContain(n => !n.Message.Contains("checked late"));
    }

    [Fact]
    public async Task CatchUp_SkipsDaysBeforeTheChange_AndBeforeJoining()
    {
        var wednesday = new DateOnly(2026, 4, 22);
        var (owner, _, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "core-catch-guard", CoreSettings);
        var ownerId = await UserIdAsync("core-catch-guard-owner@t.test");
        // Core hours changed on Wednesday at noon; the member joined on Friday morning
        await BackdateAsync(orgId, changedAt: Berlin.ToUtc(At(wednesday, 12)));
        await WithDbAsync(async db =>
        {
            (await db.UserOrganizations.SingleAsync(uo => uo.OrganizationId == orgId && uo.UserId == memberId)).JoinedAt = Berlin.ToUtc(At(wednesday.AddDays(2), 8));
            await db.SaveChangesAsync();
        });

        await CheckAsync(Berlin.ToUtc(At(wednesday.AddDays(2), 10)));
        (await CheckRowAsync(ownerId, wednesday)).Should().BeNull("its window started before the change");
        (await CheckRowAsync(ownerId, wednesday.AddDays(1)))!.CheckedLate.Should().BeTrue();
        (await CheckRowAsync(memberId, wednesday.AddDays(1))).Should().BeNull("the member joined the day after");
    }

    // ── Tolerance per group ────────────────────────────────────────────

    [Fact]
    public async Task GroupTolerance_OverridesInheritsAndTheLargestWins()
    {
        var day = new DateOnly(2026, 4, 21);   // Tuesday
        var (owner, _, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "core-tol", CoreSettings);
        async Task<int> User(string name)
        {
            var (_, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"core-tol-{name}@t.test");
            (await TestHelpers.AddMemberAsync(_factory, owner, slug, login)).EnsureSuccessStatusCode();
            return login.User.Id;
        }
        var child = await User("child");
        var strict = await User("strict");
        var both = await User("both");
        var relaxedId = await TestHelpers.CreateGroupAsync(owner, slug, "Relaxed", new { coreHoursToleranceMinutes = 30 }, (memberId, "Member"), (both, "Member"));
        var childId = await TestHelpers.CreateGroupAsync(owner, slug, "Child", new { }, (child, "Member"));
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{childId}", new { name = "Child", parentGroupId = relaxedId })).EnsureSuccessStatusCode();
        await TestHelpers.CreateGroupAsync(owner, slug, "Strict", new { coreHoursToleranceMinutes = 0 }, (strict, "Member"), (both, "Member"));
        foreach (var id in new[] { memberId, child, both })
            await AddEntryAsync(id, orgId, day, "09:20", "15:00");
        await AddEntryAsync(strict, orgId, day, "09:02", "15:00");

        await CheckAsync(Berlin.ToUtc(At(day, 15, 31)));
        var relaxed = await CheckRowAsync(memberId, day);
        relaxed!.Issues.Should().Be(CoreHoursIssue.None);
        relaxed.ToleranceMinutes.Should().Be(30);
        (await CheckRowAsync(child, day))!.ToleranceMinutes.Should().Be(30, "a sub-group inherits the tolerance");
        (await CheckRowAsync(both, day))!.ToleranceMinutes.Should().Be(30, "the largest tolerance wins");
        (await CheckRowAsync(strict, day))!.Issues.Should().Be(CoreHoursIssue.Late, "a group may be stricter than the org");

        var perms = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}/groups/{relaxedId}/permissions");
        perms.GetProperty("coreHoursToleranceMinutes").GetInt32().Should().Be(30);
        var reset = await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{relaxedId}/permissions", new { coreHoursToleranceMinutes = -1 });
        (await reset.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("coreHoursToleranceMinutes").ValueKind.Should().Be(JsonValueKind.Null);
        var bad = await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{relaxedId}/permissions", new { coreHoursToleranceMinutes = 121 });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("group.coreHoursTolerance");
    }

    // ── Backfill ───────────────────────────────────────────────────────

    /// <summary>The member works every day and joined long ago, so every past day is a core day for them.</summary>
    private Task EveryDayMemberAsync(int orgId, int memberId) =>
        WithDbAsync(async db =>
        {
            db.WorkSchedules.Add(new WorkSchedule
            {
                OrganizationId = orgId, UserId = memberId, ValidFrom = new DateOnly(2026, 1, 1),
                TargetMon = 8, TargetTue = 8, TargetWed = 8, TargetThu = 8, TargetFri = 8, TargetSat = 8, TargetSun = 8
            });
            (await db.UserOrganizations.SingleAsync(uo => uo.OrganizationId == orgId && uo.UserId == memberId)).JoinedAt = DateTime.UtcNow.AddDays(-60);
            await db.SaveChangesAsync();
        });

    private async Task<List<CoreHoursCheck>> RowsAsync(int userId, DateOnly from, DateOnly to)
    {
        List<CoreHoursCheck> rows = [];
        await WithDbAsync(async db => rows = await db.CoreHoursChecks.AsNoTracking()
            .Where(c => c.UserId == userId && c.Date >= from && c.Date <= to).OrderBy(c => c.Date).ToListAsync());
        return rows;
    }

    [Fact]
    public async Task Backfill_WhenSwitchingOn_FlagsPastDaysWithoutNotifying()
    {
        var (owner, _, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "core-backfill",
            new { timeZone = "Europe/Berlin", memberTimeEntryVisibility = true });
        var ownerId = await UserIdAsync("core-backfill-owner@t.test");
        await EveryDayMemberAsync(orgId, memberId);
        var today = Berlin.Today();
        await AddEntryAsync(memberId, orgId, today.AddDays(-2), "09:00", "15:00");

        var bad = await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { coreHoursEnabled = true, coreHoursBackfillDays = 32 });
        (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("org.coreHoursBackfillDays");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { coreHoursEnabled = true, coreHoursStart = "09:00", coreHoursEnd = "15:00", coreHoursBackfillDays = 5 }))
            .EnsureSuccessStatusCode();
        await CheckAsync(DateTime.UtcNow);

        var rows = await RowsAsync(memberId, today.AddDays(-5), today.AddDays(-1));
        rows.Should().HaveCount(5).And.OnlyContain(r => r.Backfilled && r.NotifiedAt == null && !r.CheckedLate);
        rows.Single(r => r.Date == today.AddDays(-2)).Issues.Should().Be(CoreHoursIssue.None);
        rows.Count(r => r.Issues == CoreHoursIssue.Missing).Should().Be(4);
        (await RowsAsync(memberId, today.AddDays(-30), today.AddDays(-6))).Should().BeEmpty();
        (await RowsAsync(ownerId, today.AddDays(-5), today.AddDays(-1))).Should().BeEmpty("the owner joined just now");
        await WithDbAsync(async db =>
        {
            (await db.Notifications.AnyAsync(n => n.Type == CoreHours.NotificationType && n.RelatedEntityId == memberId)).Should().BeFalse();
            (await db.Organizations.SingleAsync(o => o.Id == orgId)).CoreHoursBackfillFrom.Should().BeNull("the request is done");
        });

        var compliance = await owner.GetFromJsonAsync<List<JsonElement>>(
            $"/api/v1/organizations/{slug}/members/{memberId}/compliance?from={today.AddDays(-5):yyyy-MM-dd}&to={today.AddDays(-1):yyyy-MM-dd}");
        compliance.Should().HaveCount(4).And.OnlyContain(d => d.GetProperty("coreHoursBackfilled").GetBoolean());
    }

    [Fact]
    public async Task Backfill_ForAGroupOverride_FlagsItsMembers()
    {
        var (owner, _, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "core-backfill-g", new { timeZone = "Europe/Berlin" });
        var ownerId = await UserIdAsync("core-backfill-g-owner@t.test");
        await EveryDayMemberAsync(orgId, memberId);
        await EveryDayMemberAsync(orgId, ownerId);
        var today = Berlin.Today();

        var group = await TestHelpers.CreateGroupAsync(owner, slug, "Night", new { }, (memberId, "Member"));
        var bad = await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{group}/permissions",
            new { coreHoursMode = 1, coreHoursStart = "22:00", coreHoursEnd = "02:00", coreHoursBackfillDays = 0 });
        (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("org.coreHoursBackfillDays");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{group}/permissions",
            new { coreHoursMode = 1, coreHoursStart = "22:00", coreHoursEnd = "02:00", coreHoursBackfillDays = 3 })).EnsureSuccessStatusCode();
        await CheckAsync(DateTime.UtcNow);

        var rows = await RowsAsync(memberId, today.AddDays(-3), today.AddDays(-2));
        rows.Should().HaveCount(2).And.OnlyContain(r => r.Backfilled && r.Issues == CoreHoursIssue.Missing && r.WindowStart == new TimeOnly(22, 0));
        (await RowsAsync(ownerId, today.AddDays(-3), today)).Should().BeEmpty("the owner is not in the group and the org has no core hours");
    }
}
