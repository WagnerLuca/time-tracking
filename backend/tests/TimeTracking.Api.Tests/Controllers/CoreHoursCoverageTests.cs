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
/// Phase 1 of docs/PERFORMANCE_PLAN.md: the covering intervals of core-hours checks are looked up per member by start
/// (<c>CoveredIntervals.Reaching</c>) and loaded with a 7-day bound. Stored violations are re-evaluated against the
/// current entries, so these tests store a violation and then check which entries still clear it — across midnight,
/// for night windows, with several entries, a long entry that started days earlier and a running timer.
/// </summary>
public class CoreHoursCoverageTests : IClassFixture<TimeTrackingApiFactory>
{
    private static readonly OrgClock Berlin = OrgClock.For("Europe/Berlin");
    private static readonly DateOnly Day = new(2026, 5, 12);   // Tuesday
    private static readonly DateTime Now = Berlin.ToUtc(new DateTime(2026, 5, 14, 12, 0, 0));

    private readonly TimeTrackingApiFactory _factory;

    public CoreHoursCoverageTests(TimeTrackingApiFactory factory) => _factory = factory;

    private async Task<T> WithDbAsync<T>(Func<TimeTrackingDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>());
    }

    private Task WithDbAsync(Func<TimeTrackingDbContext, Task> action) => WithDbAsync(async db => { await action(db); return 0; });

    private static DateTime Local(DateOnly day, int hour, int minute = 0) => Berlin.ToUtc(day.ToDateTime(new TimeOnly(hour, minute)));

    /// <summary>A fresh org with one user per key, each with a stored violation of the window on <see cref="Day"/>.</summary>
    private async Task<(int OrgId, Dictionary<string, int> Users)> SetupAsync(string prefix, TimeOnly start, TimeOnly end, params string[] keys)
    {
        var users = new Dictionary<string, int>();
        var orgId = 0;
        await WithDbAsync(async db =>
        {
            var org = new Organization { Name = prefix, Slug = prefix, CoreHoursEnabled = true, CoreHoursStart = start, CoreHoursEnd = end };
            db.Organizations.Add(org);
            var created = keys.ToDictionary(k => k, k => new User { Email = $"{prefix}-{k}@t.test", FirstName = k, LastName = "Core" });
            foreach (var u in created.Values)
            {
                db.UserOrganizations.Add(new UserOrganization { User = u, Organization = org });
                db.CoreHoursChecks.Add(new CoreHoursCheck
                {
                    Organization = org, User = u, Date = Day, WindowStart = start, WindowEnd = end, ToleranceMinutes = 5,
                    Issues = CoreHoursIssue.Missing, MissingMinutes = 360, CheckedAt = Now
                });
            }
            await db.SaveChangesAsync();
            orgId = org.Id;
            foreach (var (k, u) in created) users[k] = u.Id;
        });
        return (orgId, users);
    }

    private Task AddAsync(int userId, int orgId, DateTime startUtc, DateTime? endUtc, bool failure = false) =>
        WithDbAsync(async db =>
        {
            db.TimeEntries.Add(new TimeEntry
            {
                UserId = userId, OrganizationId = orgId, StartTime = startUtc, EndTime = endUtc, IsRunning = endUtc == null,
                IsFailure = failure, CreatedAt = startUtc, UpdatedAt = startUtc
            });
            await db.SaveChangesAsync();
        });

    private Task<Dictionary<int, Dictionary<DateOnly, CoreHours.Result>>> OpenAsync(int orgId, IEnumerable<int> userIds) =>
        WithDbAsync(db => CoreHours.OpenViolationsAsync(db, userIds.ToList(), orgId, Day.AddDays(-1), Day.AddDays(1), Now));

    [Fact]
    public async Task DayWindow_EntriesAcrossMidnight_SeveralEntries_LongEntries_AndRunningTimers()
    {
        var (orgId, u) = await SetupAsync("cov-day", new TimeOnly(9, 0), new TimeOnly(15, 0),
            "none", "midnight", "pieces", "gap", "long", "longAndShort", "running", "failure", "otherOrg");

        // midnight: a night shift from the evening before runs into the window and covers it
        await AddAsync(u["midnight"], orgId, Local(Day.AddDays(-1), 20), Local(Day, 16));
        // pieces: three entries, touching within the tolerance
        await AddAsync(u["pieces"], orgId, Local(Day, 8, 55), Local(Day, 11));
        await AddAsync(u["pieces"], orgId, Local(Day, 11), Local(Day, 13, 2));
        await AddAsync(u["pieces"], orgId, Local(Day, 13, 4), Local(Day, 15, 10));
        // gap: 18 minutes missing in the middle
        await AddAsync(u["gap"], orgId, Local(Day, 8, 55), Local(Day, 13, 2));
        await AddAsync(u["gap"], orgId, Local(Day, 13, 20), Local(Day, 15, 10));
        // long: one 82 h entry that started three days earlier (legacy / auto-stop missed) still reaches the window
        await AddAsync(u["long"], orgId, Local(Day.AddDays(-3), 6), Local(Day, 16));
        // longAndShort: the long one sorts first, short ones after it — the lookup must not stop at the short ones
        await AddAsync(u["longAndShort"], orgId, Local(Day.AddDays(-2), 6), Local(Day, 16));
        await AddAsync(u["longAndShort"], orgId, Local(Day.AddDays(-1), 9), Local(Day.AddDays(-1), 10));
        await AddAsync(u["longAndShort"], orgId, Local(Day, 7), Local(Day, 8));
        await AddAsync(u["longAndShort"], orgId, Local(Day, 18), Local(Day, 19));
        // running: a timer started ten days earlier still runs (up to "now") and covers the window
        await AddAsync(u["running"], orgId, Local(Day.AddDays(-10), 8), null);
        // failure: a failure entry covers nothing
        await AddAsync(u["failure"], orgId, Local(Day, 8), Local(Day, 16), failure: true);
        // otherOrg: the entry is in another organization
        var otherOrg = await WithDbAsync(async db =>
        {
            var o = new Organization { Name = "cov-day other", Slug = "cov-day-other" };
            db.Organizations.Add(o);
            await db.SaveChangesAsync();
            return o.Id;
        });
        await AddAsync(u["otherOrg"], otherOrg, Local(Day, 8), Local(Day, 16));

        var open = await OpenAsync(orgId, u.Values);
        open.Keys.Should().BeEquivalentTo(new[] { u["none"], u["gap"], u["failure"], u["otherOrg"] });
        open[u["none"]][Day].Issues.Should().Be(CoreHoursIssue.Missing);
        open[u["none"]][Day].MissingMinutes.Should().Be(360);
        open[u["gap"]][Day].Issues.Should().Be(CoreHoursIssue.Gap);
        open[u["gap"]][Day].MissingMinutes.Should().Be(18);
        open[u["failure"]][Day].Issues.Should().Be(CoreHoursIssue.Missing);

        // The single-member overload agrees
        (await WithDbAsync(db => CoreHours.OpenViolationsAsync(db, u["midnight"], orgId, Day, Day, Now))).Should().BeEmpty();
        (await WithDbAsync(db => CoreHours.OpenViolationsAsync(db, u["none"], orgId, Day, Day, Now))).Should().ContainKey(Day);
    }

    [Fact]
    public async Task NightWindow_EntriesSplitAtMidnight_Cover_AndACorrectionClearsTheViolation()
    {
        var (orgId, u) = await SetupAsync("cov-night", new TimeOnly(22, 0), new TimeOnly(2, 0), "split", "early", "nextDayOnly");

        await AddAsync(u["split"], orgId, Local(Day, 21), Local(Day, 23, 30));
        await AddAsync(u["split"], orgId, Local(Day, 23, 30), Local(Day.AddDays(1), 3));
        await AddAsync(u["early"], orgId, Local(Day, 22), Local(Day.AddDays(1), 0, 30));
        // Covers the next day's night only, not the window starting on Day
        await AddAsync(u["nextDayOnly"], orgId, Local(Day.AddDays(1), 22), Local(Day.AddDays(2), 2));

        var open = await OpenAsync(orgId, u.Values);
        open.Keys.Should().BeEquivalentTo(new[] { u["early"], u["nextDayOnly"] });
        open[u["early"]][Day].Issues.Should().Be(CoreHoursIssue.Early);
        open[u["early"]][Day].MissingMinutes.Should().Be(90);
        open[u["nextDayOnly"]][Day].Issues.Should().Be(CoreHoursIssue.Missing);

        // The member fills the rest of the night: the violation is no longer open
        await AddAsync(u["early"], orgId, Local(Day.AddDays(1), 0, 30), Local(Day.AddDays(1), 2));
        (await OpenAsync(orgId, [u["early"]])).Should().BeEmpty();
    }

    // ── Through the background check and the compliance endpoint ──

    private static readonly object CoreSettings = new
    {
        coreHoursEnabled = true, coreHoursStart = "09:00", coreHoursEnd = "15:00", coreHoursToleranceMinutes = 5, timeZone = "Europe/Berlin",
        memberTimeEntryVisibility = true
    };

    private Task BackdateAsync(int orgId) =>
        WithDbAsync(async db =>
        {
            var org = await db.Organizations.SingleAsync(o => o.Id == orgId);
            org.CoreHoursChangedAt = null;
            foreach (var m in await db.UserOrganizations.Where(uo => uo.OrganizationId == orgId).ToListAsync())
                m.JoinedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        });

    [Fact]
    public async Task BackgroundCheck_ALongEntryFromDaysBefore_Covers_AndCompliance_FollowsCorrections()
    {
        var (owner, _, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "cov-check", CoreSettings);
        await BackdateAsync(orgId);
        var (_, worker) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "cov-check-w@t.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, worker)).EnsureSuccessStatusCode();
        await BackdateAsync(orgId);

        // The worker's 58 h entry (auto-stop missed) started two days earlier and covers the window
        await AddAsync(worker.User.Id, orgId, Local(Day.AddDays(-2), 6), Local(Day, 16));
        // The member only works the afternoon
        await AddAsync(memberId, orgId, Local(Day, 11), Local(Day, 15, 30));

        await WithDbAsync(db => CoreHours.CheckDueAsync(db, Local(Day, 15, 30), _factory.Mail, "https://app.test"));
        var rows = await WithDbAsync(db => db.CoreHoursChecks.AsNoTracking().Where(c => c.OrganizationId == orgId && c.Date == Day).ToListAsync());
        rows.Single(r => r.UserId == worker.User.Id).Issues.Should().Be(CoreHoursIssue.None);
        rows.Single(r => r.UserId == memberId).Issues.Should().Be(CoreHoursIssue.Late);

        var url = $"/api/v1/organizations/{slug}/members/{memberId}/compliance?from=2026-05-01&to=2026-05-31";
        (await owner.GetFromJsonAsync<List<JsonElement>>(url))!
            .Where(d => d.TryGetProperty("coreHours", out var c) && c.ValueKind == JsonValueKind.True)
            .Select(d => d.GetProperty("date").GetString()).Should().Contain("2026-05-12");   // (11 May is Missing too: no entry that day)

        // A correction entry from the night before reaching into the morning clears it
        await AddAsync(memberId, orgId, Local(Day.AddDays(-1), 23), Local(Day, 11));
        (await owner.GetFromJsonAsync<List<JsonElement>>(url))!
            .Where(d => d.TryGetProperty("coreHours", out var c) && c.ValueKind == JsonValueKind.True)
            .Select(d => d.GetProperty("date").GetString()).Should().NotContain("2026-05-12");
    }
}
