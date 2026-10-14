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
        var (_, _, _, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "core-late", CoreSettings);
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

        await TestHelpers.CreateGroupAsync(owner, slug, "Short core", new { coreHoursMode = 1, coreHoursStart = "10:00", coreHoursEnd = "14:00" },
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

        (await NoticesAboutAsync(memberId, day)).Select(n => n.UserId)
            .Should().BeEquivalentTo([memberId, await UserIdAsync("core-group-owner@t.test"), lead.User.Id]);

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

        var bad = await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { coreHoursEnd = "08:00" });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("org.coreHoursOrder");

        var group = await TestHelpers.CreateGroupAsync(owner, slug, "G", new { });
        var badGroup = await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{group}/permissions", new { coreHoursMode = 1, coreHoursStart = "12:00" });
        badGroup.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await badGroup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("org.coreHoursOrder");
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
}
