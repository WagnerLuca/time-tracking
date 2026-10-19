using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// The monthly pre-warm (<see cref="BalanceCheckpoints.WarmAsync"/>, hosted by <see cref="BalanceCheckpointWarmupService"/>)
/// and the retention cleanup of checkpoints that can never be valid again (<see cref="BalanceCheckpoints.Invalid"/>).
/// The app runs without the background jobs, so the test drives both.
/// </summary>
public class BalanceCheckpointWarmupTests(NoBalanceJobsApiFactory factory) : IClassFixture<NoBalanceJobsApiFactory>
{
    private readonly BalanceHarness _h = new(factory);

    private async Task<(int OrgId, int UserId, OrgClock Clock)> OrgAsync(string tag, string zone = "Europe/Berlin")
    {
        var clock = OrgClock.For(zone);
        var today = clock.Today();
        await using var scope = _h.Scope();
        var db = BalanceHarness.Db(scope);
        var org = new Organization { Name = tag, Slug = tag, TimeZone = zone };
        var user = new User { Email = $"{tag}@warm.test", FirstName = "Warm", LastName = "Up" };
        db.AddRange(org, user);
        db.UserOrganizations.Add(new UserOrganization { User = user, Organization = org, InitialOvertimeHours = 2 });
        var start = BalanceHarness.MonthOf(today).AddMonths(-5);
        db.WorkSchedules.Add(new WorkSchedule { User = user, Organization = org, ValidFrom = start, TargetMon = 8, TargetTue = 8, TargetWed = 8, TargetThu = 8, TargetFri = 8 });
        for (var d = start; d < today; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            var s = clock.ToUtc(d.ToDateTime(new TimeOnly(8, 30)));
            db.TimeEntries.Add(new TimeEntry { User = user, Organization = org, StartTime = s, EndTime = s.AddMinutes(470), PauseDurationMinutes = 30, CreatedAt = s, UpdatedAt = s });
        }
        await db.SaveChangesAsync();
        return (org.Id, user.Id, clock);
    }

    private async Task<DateOnly?> WarmedMonthAsync(int orgId) =>
        await factory.Services.GetDbAsync(db => db.Organizations.AsNoTracking().Where(o => o.Id == orgId).Select(o => o.BalanceWarmedMonth).SingleAsync());

    private async Task<int> WarmAsync(DateTime nowUtc)
    {
        await using var scope = _h.Scope();
        return await BalanceCheckpoints.WarmAsync(BalanceHarness.Db(scope), nowUtc);
    }

    [Fact]
    public async Task Warm_OncePerOrgMonth_NotBeforeThe2nd_WritesValidRows()
    {
        var (orgId, userId, clock) = await OrgAsync("warm-once");
        var month = BalanceHarness.MonthOf(clock.Today());

        // The 1st (the month before has not got its one-day margin yet): nothing
        await WarmAsync(clock.ToUtc(month.ToDateTime(new TimeOnly(23, 0))));
        (await WarmedMonthAsync(orgId)).Should().BeNull("not before the 2nd");
        (await _h.CheckpointsAsync(userId, orgId)).Should().BeEmpty();

        // The 2nd: warmed, rows of the ended months stored and valid
        await WarmAsync(clock.ToUtc(month.AddDays(1).ToDateTime(new TimeOnly(0, 30))));
        (await WarmedMonthAsync(orgId)).Should().Be(month);
        var rows = await _h.CheckpointsAsync(userId, orgId);
        rows.Select(r => r.Month).Should().Equal(Enumerable.Range(1, 5).Select(i => month.AddMonths(-6 + i)), "the five ended months");
        (await _h.AssertValidCheckpointsAsync([orgId], "after the pre-warm")).Should().Be(5);

        // Again in the same month: skipped (rows untouched), however often the loop runs
        await WarmAsync(clock.ToUtc(month.AddDays(1).ToDateTime(new TimeOnly(1, 0))));
        await WarmAsync(DateTime.UtcNow);
        (await _h.CheckpointsAsync(userId, orgId)).Select(r => (r.Month, r.ComputedAt)).Should().Equal(rows.Select(r => (r.Month, r.ComputedAt)));
        (await WarmedMonthAsync(orgId)).Should().Be(month);
    }

    [Fact]
    public async Task Warm_UsesTheOrgsLocalMonth()
    {
        // 23:30 UTC on the 1st is already the 2nd in Tokyo but still the 1st in New York
        var (tokyo, _, tokyoClock) = await OrgAsync("warm-tokyo", "Asia/Tokyo");
        var (ny, _, _) = await OrgAsync("warm-ny", "America/New_York");
        var month = BalanceHarness.MonthOf(tokyoClock.Today());
        var now = DateTime.SpecifyKind(month.ToDateTime(new TimeOnly(23, 30)), DateTimeKind.Utc);
        await WarmAsync(now);
        (await WarmedMonthAsync(tokyo)).Should().Be(month);
        (await WarmedMonthAsync(ny)).Should().BeNull();
    }

    [Fact]
    public async Task Invalid_SelectsOnlyRowsThatCanNeverBeValidAgain_TheCleanupDeletesThem()
    {
        var (orgId, userId, _) = await OrgAsync("warm-invalid");
        var (deletedOrg, deletedUser, _) = await OrgAsync("warm-invalid-gone");
        var (formerOrg, formerUser, _) = await OrgAsync("warm-invalid-former");
        await WarmAsync(DateTime.UtcNow);
        var valid = await _h.CheckpointsAsync(userId, orgId);
        valid.Should().HaveCount(5);

        await using (var scope = _h.Scope())
        {
            var db = BalanceHarness.Db(scope);
            var now = DateTime.UtcNow;
            BalanceCheckpoint Row(int org, int user, int month, int version = 0, int orgVersion = 0, int algorithm = BalanceCheckpoints.AlgorithmVersion) => new()
            {
                OrganizationId = org, UserId = user, Month = new DateOnly(2001, month, 1), FirstDay = new DateOnly(2001, 1, 1),
                Version = version, OrgVersion = orgVersion, Algorithm = algorithm, ComputedAt = now
            };
            db.BalanceCheckpoints.AddRange(
                Row(orgId, userId, 1, version: -1),                                  // an older member version
                Row(orgId, userId, 2, orgVersion: -1),                               // an older org version
                Row(orgId, userId, 3, algorithm: BalanceCheckpoints.AlgorithmVersion - 1),
                Row(orgId, userId, 4, version: 7));                                  // a version that does not exist (yet): not valid either
            // A deleted organization and a membership row that is gone
            (await db.Organizations.SingleAsync(o => o.Id == deletedOrg)).IsActive = false;
            db.UserOrganizations.Remove(await db.UserOrganizations.SingleAsync(uo => uo.OrganizationId == formerOrg && uo.UserId == formerUser));
            await db.SaveChangesAsync();
        }

        await using (var scope = _h.Scope())
        {
            var db = BalanceHarness.Db(scope);
            var invalid = await BalanceCheckpoints.Invalid(db).ToListAsync();
            invalid.Where(c => c.OrganizationId == orgId).Select(c => c.Month.Month).Should().BeEquivalentTo([1, 2, 3, 4]);
            invalid.Count(c => c.OrganizationId == deletedOrg).Should().Be(5);
            invalid.Count(c => c.OrganizationId == formerOrg).Should().Be(5);
        }

        await RetentionScenario.RunAsync(factory.Services);

        (await _h.CheckpointsAsync(userId, orgId)).Select(r => r.Id).Should().Equal(valid.Select(r => r.Id), "valid rows stay");
        (await _h.CheckpointsAsync(deletedUser, deletedOrg)).Should().BeEmpty();
        (await _h.CheckpointsAsync(formerUser, formerOrg)).Should().BeEmpty();
        (await _h.AssertValidCheckpointsAsync([orgId], "after the cleanup")).Should().Be(5);
    }
}
