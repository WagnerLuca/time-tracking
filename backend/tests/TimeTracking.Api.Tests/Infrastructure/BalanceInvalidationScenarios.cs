using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Tests.Infrastructure;

/// <summary>
/// One test per invalidation trigger of <c>BalanceInvalidation</c> (docs/PERFORMANCE_PLAN.md Phase 2), shared by the InMemory
/// (<c>ApplyTrackedAsync</c>) and the Postgres (<c>ApplySqlAsync</c>) variants. Each scenario builds a member with ~8 months of
/// history, lets a consumer store the checkpoints of the ended months, makes one change and asserts the version bump and the
/// carry: rows of months before the affected one move to the new version (still valid), rows from it on keep the old one
/// (invalid) — then the next read stores correct rows again and equals the full recompute.
/// </summary>
public abstract class BalanceInvalidationScenarios
{
    protected abstract WebApplicationFactory<Program> App { get; }
    private BalanceHarness H => new(App);

    private static int _counter;

    protected sealed record World(int OrgId, int UserId, int OtherOrgId, OrgClock Clock, DateOnly Today)
    {
        /// <summary>First day of the month <paramref name="back"/> months before the current one.</summary>
        public DateOnly Month(int back) => BalanceHarness.MonthOf(Today).AddMonths(-back);
    }

    /// <summary>An org (+ a second one the member is also in) with eight months of weekday entries and an open schedule,
    /// and the checkpoints of the ended months stored by a read.</summary>
    protected async Task<World> WorldAsync(string zone = "Europe/Berlin")
    {
        var n = Interlocked.Increment(ref _counter);
        var tag = $"{GetType().Name.ToLowerInvariant()}-{n}-{Guid.NewGuid():N}"[..40];
        int orgId, otherId, userId;
        var clock = OrgClock.For(zone);
        var today = clock.Today();
        await using (var scope = H.Scope())
        {
            var db = BalanceHarness.Db(scope);
            var month = BalanceHarness.MonthOf(today);
            var org = new Organization { Name = tag, Slug = tag, TimeZone = zone, MemberTimeEntryVisibility = true, BalanceWarmedMonth = month.AddMonths(1) };
            var other = new Organization { Name = tag + "-o", Slug = tag + "-o", TimeZone = zone, BalanceWarmedMonth = month.AddMonths(1) };
            var user = new User { Email = $"{tag}@inv.test", FirstName = "Inva", LastName = "Lidation" };
            db.AddRange(org, other, user);
            db.UserOrganizations.Add(new UserOrganization { User = user, Organization = org, Role = OrganizationRole.Member, InitialOvertimeHours = 3 });
            db.UserOrganizations.Add(new UserOrganization { User = user, Organization = other, Role = OrganizationRole.Member });
            var start = month.AddMonths(-8);
            foreach (var o in new[] { org, other })
                db.WorkSchedules.Add(new WorkSchedule
                {
                    User = user, Organization = o, ValidFrom = start, WeeklyWorkHours = 40,
                    TargetMon = 8, TargetTue = 8, TargetWed = 8, TargetThu = 8, TargetFri = 8
                });
            var i = 0;
            for (var d = start; d < today; d = d.AddDays(1))
            {
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                var s = clock.ToUtc(d.ToDateTime(new TimeOnly(8, 0)));
                db.TimeEntries.Add(new TimeEntry
                {
                    User = user, Organization = i++ % 5 == 0 ? other : org, StartTime = s, EndTime = s.AddMinutes(480 + i % 7 * 10),
                    PauseDurationMinutes = 30, PauseIsManual = true, CreatedAt = s, UpdatedAt = s
                });
            }
            await db.SaveChangesAsync();
            (orgId, otherId, userId) = (org.Id, other.Id, user.Id);
        }
        var world = new World(orgId, userId, otherId, clock, today);
        await ReadAsync(world);
        (await H.CheckpointsAsync(userId, orgId)).Should().HaveCountGreaterThanOrEqualTo(6, "a read stores the checkpoints of the ended months");
        return world;
    }

    /// <summary>A consumer read of both orgs (system work: sees and writes everything), then the invariant.</summary>
    protected async Task ReadAsync(World w)
    {
        foreach (var orgId in new[] { w.OrgId, w.OtherOrgId })
        {
            await using var scope = H.Scope();
            var db = BalanceHarness.Db(scope);
            var org = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == orgId);
            var initial = await db.UserOrganizations.AsNoTracking().Where(uo => uo.OrganizationId == orgId && uo.UserId == w.UserId)
                .Select(uo => uo.InitialOvertimeHours).SingleAsync();
            var balances = await DailyReportAggregator.BalancesAsync(db, org, new Dictionary<int, double> { [w.UserId] = initial });
            var reference = await BalanceHarness.ReferenceAsync(db, w.UserId, orgId);
            Math.Abs(balances[w.UserId].Balance - reference.Balance(initial)).Should().BeLessThanOrEqualTo(BalanceHarness.Exact);
        }
        await H.AssertValidCheckpointsAsync([w.OrgId, w.OtherOrgId], "after a read");
    }

    /// <summary>Runs a change in a fresh app scope (system work) and saves it.</summary>
    protected async Task ChangeAsync(Func<TimeTrackingDbContext, Task> change)
    {
        await using var scope = H.Scope();
        var db = BalanceHarness.Db(scope);
        await change(db);
        await db.SaveChangesAsync();
    }

    protected async Task<(int Member, int Org, List<BalanceCheckpoint> Rows)> SnapshotAsync(World w, int? orgId = null)
    {
        var (member, org) = await H.VersionsAsync(w.UserId, orgId ?? w.OrgId);
        return (member, org, await H.CheckpointsAsync(w.UserId, orgId ?? w.OrgId));
    }

    /// <summary>
    /// The member's (<paramref name="memberBump"/>) or the organization's version went up by one; rows of months before
    /// <paramref name="from"/> carry the new version, the others keep the old one. Then the invariant, a read, and the
    /// invariant again (the read must not be fooled by anything left behind).
    /// </summary>
    protected async Task AssertBumpedAsync(World w, (int Member, int Org, List<BalanceCheckpoint> Rows) before, bool memberBump, DateOnly from,
        int? orgId = null)
    {
        var org = orgId ?? w.OrgId;
        var after = await SnapshotAsync(w, org);
        before.Rows.Should().NotBeEmpty();
        if (memberBump)
        {
            after.Member.Should().Be(before.Member + 1, "the member's version is bumped");
            after.Org.Should().Be(before.Org, "the organization's version stays");
        }
        else
        {
            after.Org.Should().Be(before.Org + 1, "the organization's version is bumped");
            after.Member.Should().Be(before.Member, "the member's version stays");
        }
        foreach (var row in after.Rows)
        {
            var version = memberBump ? row.Version : row.OrgVersion;
            var old = memberBump ? before.Member : before.Org;
            if (row.Month < from)
                version.Should().Be(old + 1, $"{row.Month:yyyy-MM} is before {from:yyyy-MM}: carried over, still valid");
            else
                version.Should().Be(old, $"{row.Month:yyyy-MM} is not before {from:yyyy-MM}: invalid");
        }
        after.Rows.Select(r => r.Month).Should().Equal(before.Rows.Select(r => r.Month), "the change itself writes no checkpoint");
        await H.AssertValidCheckpointsAsync([w.OrgId, w.OtherOrgId], "right after the change");
        await ReadAsync(w);
    }

    protected async Task AssertUnchangedAsync(World w, (int Member, int Org, List<BalanceCheckpoint> Rows) before)
    {
        var after = await SnapshotAsync(w);
        (after.Member, after.Org).Should().Be((before.Member, before.Org), "nothing in an ended month changed");
        after.Rows.Select(r => (r.Month, r.Version, r.OrgVersion)).Should().Equal(before.Rows.Select(r => (r.Month, r.Version, r.OrgVersion)));
        await ReadAsync(w);
    }

    private async Task<TimeEntry> EntryInAsync(TimeTrackingDbContext db, World w, DateOnly month, int? orgId = null)
    {
        var from = w.Clock.StartOfDayUtc(month.AddDays(9));
        return await db.TimeEntries.Where(e => e.UserId == w.UserId && e.OrganizationId == (orgId ?? w.OrgId) && e.StartTime >= from)
            .OrderBy(e => e.StartTime).FirstAsync();
    }

    [Fact]
    public async Task EntryEdit_InAPastMonth_BumpsTheMember_CarriesEarlierMonths()
    {
        var w = await WorldAsync();
        var before = await SnapshotAsync(w);
        await ChangeAsync(async db =>
        {
            var e = await EntryInAsync(db, w, w.Month(4));
            e.EndTime = e.EndTime!.Value.AddMinutes(77);
        });
        await AssertBumpedAsync(w, before, memberBump: true, w.Month(4));
    }

    [Fact]
    public async Task EntryMovedToAnotherMonth_InvalidatesFromTheEarlierOne()
    {
        var w = await WorldAsync();
        var before = await SnapshotAsync(w);
        var target = DateOnly.MinValue;
        await ChangeAsync(async db =>
        {
            var e = await EntryInAsync(db, w, w.Month(2));
            (e.StartTime, e.EndTime) = (e.StartTime.AddDays(-95), e.EndTime!.Value.AddDays(-95));
            target = w.Clock.LocalDate(e.StartTime);
        });
        await AssertBumpedAsync(w, before, memberBump: true, BalanceHarness.MonthOf(target));
    }

    [Fact]
    public async Task EntryMovedBetweenOrganizations_BumpsBothMemberships()
    {
        var w = await WorldAsync();
        var before = await SnapshotAsync(w);
        var beforeOther = await SnapshotAsync(w, w.OtherOrgId);
        await ChangeAsync(async db => (await EntryInAsync(db, w, w.Month(3))).OrganizationId = w.OtherOrgId);
        await AssertBumpedAsync(w, before, memberBump: true, w.Month(3));
        // The other org's rows were already recomputed by the read above; its version went up by exactly one
        (await H.VersionsAsync(w.UserId, w.OtherOrgId)).Member.Should().Be(beforeOther.Member + 1);
    }

    [Fact]
    public async Task EntryDeleted_InAPastMonth_Bumps()
    {
        var w = await WorldAsync();
        var before = await SnapshotAsync(w);
        await ChangeAsync(async db => db.TimeEntries.Remove(await EntryInAsync(db, w, w.Month(5))));
        await AssertBumpedAsync(w, before, memberBump: true, w.Month(5));
    }

    [Fact]
    public async Task Absence_InAPastMonth_BumpsTheMember()
    {
        var w = await WorldAsync();
        var before = await SnapshotAsync(w);
        await ChangeAsync(db =>
        {
            db.AbsenceDays.Add(new AbsenceDay { UserId = w.UserId, OrganizationId = w.OrgId, Date = w.Month(3).AddDays(14), Type = AbsenceType.Vacation, IsHalfDay = true });
            return Task.CompletedTask;
        });
        await AssertBumpedAsync(w, before, memberBump: true, w.Month(3));
    }

    [Fact]
    public async Task ScheduleValidToOnly_InvalidatesFromTheDayAfterTheEarlierEnd()
    {
        var w = await WorldAsync();
        var before = await SnapshotAsync(w);
        // Ends on the last day of month 5 back: the days from month 4 back on lose their target
        await ChangeAsync(async db =>
            (await db.WorkSchedules.SingleAsync(ws => ws.UserId == w.UserId && ws.OrganizationId == w.OrgId)).ValidTo = w.Month(4).AddDays(-1));
        await AssertBumpedAsync(w, before, memberBump: true, w.Month(4));

        // Moving that end later by a month: invalid from the day after the earlier end again (month 4 back)
        var again = await SnapshotAsync(w);
        await ChangeAsync(async db =>
            (await db.WorkSchedules.SingleAsync(ws => ws.UserId == w.UserId && ws.OrganizationId == w.OrgId)).ValidTo = w.Month(3).AddDays(-1));
        await AssertBumpedAsync(w, again, memberBump: true, w.Month(4));
    }

    [Fact]
    public async Task ScheduleTargets_InvalidateFromValidFrom()
    {
        var w = await WorldAsync();
        await ChangeAsync(db =>
        {
            db.WorkSchedules.Add(new WorkSchedule
            {
                UserId = w.UserId, OrganizationId = w.OrgId, ValidFrom = w.Month(3).AddDays(5), WeeklyWorkHours = 30,
                TargetMon = 6, TargetTue = 6, TargetWed = 6, TargetThu = 6, TargetFri = 6
            });
            return Task.CompletedTask;
        });
        await ReadAsync(w);
        var before = await SnapshotAsync(w);
        await ChangeAsync(async db =>
            (await db.WorkSchedules.Where(ws => ws.UserId == w.UserId && ws.OrganizationId == w.OrgId).OrderByDescending(ws => ws.ValidFrom).FirstAsync()).TargetWed = 2);
        await AssertBumpedAsync(w, before, memberBump: true, w.Month(3));
    }

    [Fact]
    public async Task OneOffHoliday_BumpsTheOrganization_FromItsMonth()
    {
        var w = await WorldAsync();
        var before = await SnapshotAsync(w);
        await ChangeAsync(db =>
        {
            db.Holidays.Add(new Holiday { OrganizationId = w.OrgId, Name = "Once", Date = w.Month(3).AddDays(10) });
            return Task.CompletedTask;
        });
        await AssertBumpedAsync(w, before, memberBump: false, w.Month(3));
    }

    [Fact]
    public async Task RecurringHoliday_InvalidatesEveryMonth()
    {
        var w = await WorldAsync();
        var before = await SnapshotAsync(w);
        await ChangeAsync(db =>
        {
            db.Holidays.Add(new Holiday { OrganizationId = w.OrgId, Name = "Every year", Date = new DateOnly(2001, w.Month(2).Month, 12), IsRecurring = true });
            return Task.CompletedTask;
        });
        await AssertBumpedAsync(w, before, memberBump: false, DateOnly.MinValue);
    }

    /// <summary>
    /// A recurring holiday applies in every year, whatever year its stored <c>Date</c> has: one stored with a later date than the
    /// checkpoints (next year's date of a day four months back, or a day of the last ended month) still invalidates every
    /// month — "from its own month" would leave the rows over this year's occurrence valid and stale.
    /// </summary>
    [Theory]
    [InlineData(4, 1)]   // stored next year, applies four months back this year
    [InlineData(1, 0)]   // stored in the last ended month of this year
    public async Task RecurringHolidayStoredAfterTheCheckpoints_InvalidatesEveryMonth(int monthsBack, int yearsAhead)
    {
        var w = await WorldAsync();
        var day = w.Month(monthsBack).AddDays(11);
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) day = day.AddDays(1);
        var before = await SnapshotAsync(w);
        before.Rows.Should().Contain(r => r.Month < BalanceHarness.MonthOf(day), "there are checkpoints before the holiday's month");
        await ChangeAsync(db =>
        {
            db.Holidays.Add(new Holiday { OrganizationId = w.OrgId, Name = "Yearly later", Date = day.AddYears(yearsAhead), IsRecurring = true });
            return Task.CompletedTask;
        });
        await AssertBumpedAsync(w, before, memberBump: false, DateOnly.MinValue);
        var reference = await App.Services.GetDbAsync(db => BalanceHarness.ReferenceAsync(db, w.UserId, w.OrgId));
        reference.Row(day)!.Status.Should().Be("Holiday", "the recurring holiday applies this year");
    }

    /// <summary>
    /// The store never downgrades: a computation stamped with an older member version (a slow reader that started before an
    /// edit) leaves a newer row alone — version, sum and ComputedAt — and a store with identical versions rewrites nothing.
    /// <c>SaveAsync</c> and its records are internal, so they are reached by reflection.
    /// </summary>
    [Fact]
    public async Task SaveAsync_NeverReplacesANewerRow_IdenticalVersionsRewriteNothing()
    {
        var w = await WorldAsync();
        var month = w.Month(3);
        var (memberVersion, orgVersion) = await H.VersionsAsync(w.UserId, w.OrgId);
        // The current row of that month, made one member version newer than the member (as if written after a bump)
        await ChangeAsync(async db =>
        {
            (await db.UserOrganizations.SingleAsync(uo => uo.UserId == w.UserId && uo.OrganizationId == w.OrgId)).BalanceVersion = memberVersion + 1;
            var row = await db.BalanceCheckpoints.SingleAsync(c => c.UserId == w.UserId && c.OrganizationId == w.OrgId && c.Month == month);
            (row.Version, row.OvertimeDelta, row.ComputedAt) = (memberVersion + 1, 123.25, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        });
        async Task<BalanceCheckpoint> Row() => (await H.CheckpointsAsync(w.UserId, w.OrgId)).Single(c => c.Month == month);
        var stored = await Row();

        await SaveViaReflectionAsync(w, month, memberVersion, orgVersion, delta: -7.5);
        var afterOlder = await Row();
        (afterOlder.Version, afterOlder.OvertimeDelta, afterOlder.ComputedAt).Should().Be((stored.Version, 123.25, stored.ComputedAt),
            "an older stamp never replaces a newer row");

        await SaveViaReflectionAsync(w, month, memberVersion + 1, orgVersion, delta: 123.25);
        var afterSame = await Row();
        (afterSame.Version, afterSame.OvertimeDelta, afterSame.ComputedAt).Should().Be((stored.Version, 123.25, stored.ComputedAt),
            "identical versions rewrite nothing (ComputedAt unchanged)");
    }

    private async Task SaveViaReflectionAsync(World w, DateOnly month, int memberVersion, int orgVersion, double delta)
    {
        var t = typeof(BalanceCheckpoints);
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic;
        var startType = t.GetNestedType("Start", any)!;
        var stampType = t.GetNestedType("Stamp", any)!;
        var memberType = t.GetNestedType("Member", any)!;
        string? zone = await App.Services.GetDbAsync(db => db.Organizations.Where(o => o.Id == w.OrgId).Select(o => o.TimeZone).SingleAsync());
        var stamp = Activator.CreateInstance(stampType, w.OrgId, orgVersion,
            (IReadOnlyDictionary<int, int>)new Dictionary<int, int> { [w.UserId] = memberVersion }, w.Clock.Today(), zone)!;
        var start = Activator.CreateInstance(startType, month.AddMonths(-4), 0.0, month.AddMonths(-4))!;
        var member = Activator.CreateInstance(memberType, start, new List<WorkSchedule>(), 0, null, null)!;
        var monthEnds = new List<(DateOnly Day, double Delta)> { (month.AddMonths(1).AddDays(-1), delta) };
        var tupleType = typeof(ValueTuple<,,>).MakeGenericType(typeof(int), memberType, typeof(List<(DateOnly, double)>));
        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tupleType))!;
        list.Add(Activator.CreateInstance(tupleType, w.UserId, member, monthEnds));
        var save = t.GetMethod("SaveAsync", BindingFlags.Static | any)!;
        await using var scope = H.Scope();
        var args = save.GetParameters().Select(p => p.Name switch
        {
            "db" => (object?)BalanceHarness.Db(scope),
            "stamp" => stamp,
            "zoneUsed" => zone,
            "computed" => list,
            _ => p.HasDefaultValue ? p.DefaultValue : null
        }).ToArray();
        await (Task)save.Invoke(null, args)!;
    }

    [Fact]
    public async Task CreditModeChange_InvalidatesEveryMonth_ANameChangeNothing()
    {
        var w = await WorldAsync();
        int typeId = 0;
        await ChangeAsync(db =>
        {
            var type = new AbsenceTypeDefinition { OrganizationId = w.OrgId, Name = "Trip", CreditMode = AbsenceCreditMode.FillToTarget };
            db.AbsenceTypeDefinitions.Add(type);
            db.AbsenceDays.Add(new AbsenceDay { UserId = w.UserId, OrganizationId = w.OrgId, Date = w.Month(4).AddDays(3), Type = AbsenceType.Other, CustomType = type });
            return Task.CompletedTask;
        });
        await ReadAsync(w);
        typeId = await App.Services.GetDbAsync(db => db.AbsenceTypeDefinitions.Where(t => t.OrganizationId == w.OrgId).Select(t => t.Id).SingleAsync());

        var unchanged = await SnapshotAsync(w);
        await ChangeAsync(async db => (await db.AbsenceTypeDefinitions.SingleAsync(t => t.Id == typeId)).Name = "Business trip");
        await AssertUnchangedAsync(w, unchanged);

        var before = await SnapshotAsync(w);
        await ChangeAsync(async db => (await db.AbsenceTypeDefinitions.SingleAsync(t => t.Id == typeId)).CreditMode = AbsenceCreditMode.FullCredit);
        await AssertBumpedAsync(w, before, memberBump: false, DateOnly.MinValue);
    }

    [Fact]
    public async Task TimeZoneChange_InvalidatesEveryMonth()
    {
        var w = await WorldAsync();
        var before = await SnapshotAsync(w);
        await ChangeAsync(async db => (await db.Organizations.SingleAsync(o => o.Id == w.OrgId)).TimeZone = "America/Los_Angeles");
        await AssertBumpedAsync(w, before, memberBump: false, DateOnly.MinValue);
    }

    [Fact]
    public async Task InitialOvertimeChange_BumpsNothing_BalanceFollows()
    {
        var w = await WorldAsync();
        var before = await SnapshotAsync(w);
        await ChangeAsync(async db =>
            (await db.UserOrganizations.SingleAsync(uo => uo.UserId == w.UserId && uo.OrganizationId == w.OrgId)).InitialOvertimeHours = -12.5);
        await AssertUnchangedAsync(w, before);
    }

    [Fact]
    public async Task EditInTheCurrentMonth_BumpsNothing()
    {
        var w = await WorldAsync();
        var start = DateTime.UtcNow.AddHours(-2);
        // Only meaningful while two hours ago is still in the current month (with the writers' margin: not its 1st)
        if (BalanceHarness.MonthOf(w.Clock.LocalDate(start)) != BalanceHarness.MonthOf(w.Today)) return;
        var before = await SnapshotAsync(w);
        await ChangeAsync(db =>
        {
            db.TimeEntries.Add(new TimeEntry { UserId = w.UserId, OrganizationId = w.OrgId, StartTime = start, EndTime = start.AddHours(1), CreatedAt = start, UpdatedAt = start });
            db.AbsenceDays.Add(new AbsenceDay { UserId = w.UserId, OrganizationId = w.OrgId, Date = w.Today, Type = AbsenceType.SickDay });
            return Task.CompletedTask;
        });
        await AssertUnchangedAsync(w, before);
    }

    [Fact]
    public async Task PureTimerStop_OfATimerStartedInAnEndedMonth_Bumps()
    {
        var w = await WorldAsync();
        var start = w.Clock.ToUtc(w.Month(2).AddDays(-1).ToDateTime(new TimeOnly(22, 0)));
        int id = 0;
        await ChangeAsync(db =>
        {
            var e = new TimeEntry { UserId = w.UserId, OrganizationId = w.OrgId, StartTime = start, IsRunning = true, CreatedAt = start, UpdatedAt = start };
            db.TimeEntries.Add(e);
            return Task.CompletedTask;
        });
        // Starting a timer is not a balance change; reading stores the month without it
        await ReadAsync(w);
        var before = await SnapshotAsync(w);
        await ChangeAsync(async db =>
        {
            var e = await db.TimeEntries.SingleAsync(x => x.UserId == w.UserId && x.IsRunning);
            (e.EndTime, e.IsRunning) = (start.AddHours(3), false);
            id = e.Id;
        });
        id.Should().BeGreaterThan(0);
        await AssertBumpedAsync(w, before, memberBump: true, w.Month(3));
    }

    /// <summary>
    /// A recurring and a one-off holiday on the same day: the one-off one decides (non-recurring first, then by id), whatever
    /// order they were created in — so the day's credit, and every checkpoint over it, is the same on every read.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task SameDayRecurringAndOneOffHoliday_TheOneOffDecides(bool oneOffIsHalf, bool recurringFirst)
    {
        var w = await WorldAsync();
        var day = w.Month(3).AddDays(10);
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) day = day.AddDays(1);
        var oneOff = new Holiday { OrganizationId = w.OrgId, Name = "Once", Date = day, IsHalfDay = oneOffIsHalf };
        var recurring = new Holiday { OrganizationId = w.OrgId, Name = "Yearly", Date = new DateOnly(2001, day.Month, day.Day), IsRecurring = true, IsHalfDay = !oneOffIsHalf };
        foreach (var h in recurringFirst ? new[] { recurring, oneOff } : [oneOff, recurring])
            await ChangeAsync(db => { db.Holidays.Add(h); return Task.CompletedTask; });

        await ReadAsync(w);
        var reference = await App.Services.GetDbAsync(db => BalanceHarness.ReferenceAsync(db, w.UserId, w.OrgId));
        var row = reference.Row(day)!;
        row.HolidayName.Should().Be("Once");
        row.TargetHours.Should().Be(oneOffIsHalf ? 4 : 0, "the one-off holiday's half/full day decides");
        row.Status.Should().Be(oneOffIsHalf ? "Half Holiday" : "Holiday");
        // Read again from the stored checkpoints: the same balance
        await ReadAsync(w);
    }

    /// <summary>
    /// System work too: the business-day-end auto-stop (no HttpContext) of a timer started on the last evening of the previous
    /// month, run at 00:30 on the 1st, bumps the member like any other save.
    /// </summary>
    [Fact]
    public async Task SystemAutoStop_OfATimerFromThePreviousMonthsLastDay_Bumps()
    {
        var w = await WorldAsync();
        var lastDay = w.Month(0).AddDays(-1);
        var start = w.Clock.ToUtc(lastDay.ToDateTime(new TimeOnly(20, 0)));
        await ChangeAsync(db =>
        {
            db.TimeEntries.Add(new TimeEntry { UserId = w.UserId, OrganizationId = w.OrgId, StartTime = start, IsRunning = true, CreatedAt = start, UpdatedAt = start });
            return Task.CompletedTask;
        });
        var before = await SnapshotAsync(w);
        var now = w.Clock.ToUtc(w.Month(0).ToDateTime(new TimeOnly(0, 30)));
        await using (var scope = H.Scope())
            (await FailureEntryService.CloseOverdueEntriesAsync(BalanceHarness.Db(scope), now)).Should().BeGreaterThanOrEqualTo(1);
        (await App.Services.GetDbAsync(db => db.TimeEntries.SingleAsync(e => e.UserId == w.UserId && e.StartTime == start))).IsFailure.Should().BeTrue();
        await AssertBumpedAsync(w, before, memberBump: true, BalanceHarness.MonthOf(lastDay));
    }
}

public static class ServiceProviderDbExtensions
{
    /// <summary>A query in a fresh app scope (system work).</summary>
    public static async Task<T> GetDbAsync<T>(this IServiceProvider services, Func<TimeTrackingDbContext, Task<T>> query)
    {
        await using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateAsyncScope(services);
        return await query(BalanceHarness.Db(scope));
    }
}
