using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// The one-day margin between readers and writers (<c>BalanceCheckpoints.Storable</c>): a reader stores month M only once
/// LastDay(M) &lt; today − 1 (from the 2nd of the next month), a writer bumps only for months with LastDay(M) &lt; today (so a
/// save on a month's last day bumps nothing). The app has no injectable clock, so the end-to-end checks use the org's time
/// zone to put "today" on a month boundary: they run whenever some UTC offset (−12 … +14) has its local today on the 1st, the
/// 2nd or a month's last day — around every month change — and are a no-op on the other days. The rule itself is pinned
/// date-independently via reflection.
/// </summary>
public class BalanceCheckpointMarginTests(NoBalanceJobsApiFactory factory) : IClassFixture<NoBalanceJobsApiFactory>
{
    private readonly BalanceHarness _h = new(factory);

    private static bool Storable(DateOnly monthLastDay, DateOnly readerToday) =>
        (bool)typeof(BalanceCheckpoints).GetMethod("Storable", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [monthLastDay, readerToday])!;

    [Fact]
    public void Storable_FromTheSecondOfTheNextMonth()
    {
        var lastDay = new DateOnly(2026, 9, 30);
        Storable(lastDay, new DateOnly(2026, 9, 30)).Should().BeFalse("the month's last day");
        Storable(lastDay, new DateOnly(2026, 10, 1)).Should().BeFalse("the 1st: inside the one-day margin");
        Storable(lastDay, new DateOnly(2026, 10, 2)).Should().BeTrue("the 2nd");
        Storable(new DateOnly(2024, 2, 29), new DateOnly(2024, 3, 2)).Should().BeTrue();
        Storable(new DateOnly(2025, 12, 31), new DateOnly(2026, 1, 1)).Should().BeFalse("across a year end too");
    }

    /// <summary>A fixed-offset zone whose local today has the given day-of-month predicate, if any exists right now.</summary>
    private static (string Zone, OrgClock Clock)? ZoneWhere(Func<DateOnly, bool> today)
    {
        for (var offset = -12; offset <= 14; offset++)
        {
            // POSIX naming: Etc/GMT-14 is UTC+14
            var zone = offset == 0 ? "Etc/UTC" : $"Etc/GMT{(offset > 0 ? "-" : "+")}{Math.Abs(offset)}";
            var clock = OrgClock.For(zone);
            if (clock.Zone.BaseUtcOffset != TimeSpan.FromHours(offset)) continue;   // unknown id fell back to the default
            if (today(clock.Today())) return (zone, clock);
        }
        return null;
    }

    private async Task<(int OrgId, int UserId)> OrgAsync(string tag, string zone, OrgClock clock)
    {
        var today = clock.Today();
        await using var scope = _h.Scope();
        var db = BalanceHarness.Db(scope);
        var org = new Organization { Name = tag, Slug = tag, TimeZone = zone };
        var user = new User { Email = $"{tag}@margin.test", FirstName = "Mar", LastName = "Gin" };
        db.AddRange(org, user);
        db.UserOrganizations.Add(new UserOrganization { User = user, Organization = org });
        var start = BalanceHarness.MonthOf(today).AddMonths(-3);
        db.WorkSchedules.Add(new WorkSchedule { User = user, Organization = org, ValidFrom = start, TargetMon = 8, TargetTue = 8, TargetWed = 8, TargetThu = 8, TargetFri = 8 });
        for (var d = start; d <= today; d = d.AddDays(1))
        {
            var s = clock.ToUtc(d.ToDateTime(new TimeOnly(0, 10)));
            if (s > DateTime.UtcNow.AddMinutes(-30)) break;
            db.TimeEntries.Add(new TimeEntry { User = user, Organization = org, StartTime = s, EndTime = s.AddMinutes(20), CreatedAt = s, UpdatedAt = s });
        }
        await db.SaveChangesAsync();
        return (org.Id, user.Id);
    }

    private async Task ReadAsync(int orgId, int userId)
    {
        await using var scope = _h.Scope();
        var db = BalanceHarness.Db(scope);
        var org = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == orgId);
        await DailyReportAggregator.BalancesAsync(db, org, new Dictionary<int, double> { [userId] = 0 });
    }

    [Fact]
    public async Task OnThe1st_TheMonthJustEndedIsNotStoredYet_OnThe2ndItIs()
    {
        if (ZoneWhere(d => d.Day == 1) is (string zone1, OrgClock clock1))
        {
            var (org, user) = await OrgAsync("margin-first", zone1, clock1);
            await ReadAsync(org, user);
            var months = (await _h.CheckpointsAsync(user, org)).Select(c => c.Month).ToList();
            months.Should().NotContain(BalanceHarness.MonthOf(clock1.Today()).AddMonths(-1), "on the 1st the month before is inside the margin");
            months.Should().Contain(BalanceHarness.MonthOf(clock1.Today()).AddMonths(-2));
        }
        if (ZoneWhere(d => d.Day == 2) is (string zone2, OrgClock clock2))
        {
            var (org, user) = await OrgAsync("margin-second", zone2, clock2);
            await ReadAsync(org, user);
            (await _h.CheckpointsAsync(user, org)).Select(c => c.Month).Should().Contain(BalanceHarness.MonthOf(clock2.Today()).AddMonths(-1),
                "from the 2nd the month before is stored");
        }
    }

    [Fact]
    public async Task ASaveOnAMonthsLastDay_BumpsNothing()
    {
        if (ZoneWhere(d => d.AddDays(1).Day == 1) is not (string zone, OrgClock clock)) return;
        var (org, user) = await OrgAsync("margin-last", zone, clock);
        await ReadAsync(org, user);
        var before = await _h.VersionsAsync(user, org);
        await using (var scope = _h.Scope())
        {
            var db = BalanceHarness.Db(scope);
            db.AbsenceDays.Add(new AbsenceDay { UserId = user, OrganizationId = org, Date = clock.Today(), Type = AbsenceType.SickDay });
            await db.SaveChangesAsync();
        }
        (await _h.VersionsAsync(user, org)).Should().Be(before, "a change on the month's last day touches no month a reader may store yet");
        await ReadAsync(org, user);
        await _h.AssertValidCheckpointsAsync([org], "after the last-day save");
    }
}
