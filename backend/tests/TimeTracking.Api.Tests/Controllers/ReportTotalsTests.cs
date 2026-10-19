using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Phase 1 of docs/PERFORMANCE_PLAN.md for the report figures: <c>BuildWithRunningTotalAsync</c> only evaluates core hours and
/// entry summaries for its display range but keeps the full running total, <c>GetTargetHours</c> picks the latest schedule,
/// <c>TotalsAsync</c> (team-wide, four queries) equals the per-member <c>BuildAsync</c> computation, and the payroll rows built
/// on it match a hand-computed week.
/// </summary>
public class ReportTotalsTests : IClassFixture<TimeTrackingApiFactory>
{
    private static readonly OrgClock Berlin = OrgClock.For("Europe/Berlin");
    private readonly TimeTrackingApiFactory _factory;

    public ReportTotalsTests(TimeTrackingApiFactory factory) => _factory = factory;

    private async Task<T> WithDbAsync<T>(Func<TimeTrackingDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>());
    }

    private static TimeEntry Entry(User user, Organization org, DateOnly day, int fromHour, int fromMinute, int toHour, int toMinute,
        int pause = 0, bool failure = false, int addDays = 0, string? description = null)
    {
        var start = Berlin.ToUtc(day.ToDateTime(new TimeOnly(fromHour, fromMinute)));
        var end = Berlin.ToUtc(day.AddDays(addDays).ToDateTime(new TimeOnly(toHour, toMinute)));
        return new TimeEntry
        {
            User = user, Organization = org, StartTime = start, EndTime = end, PauseDurationMinutes = pause, PauseIsManual = true,
            IsFailure = failure, Description = description, CreatedAt = start, UpdatedAt = start
        };
    }

    private static WorkSchedule FullTime(User user, Organization org, DateOnly from, double hours = 8) => new()
    {
        User = user, Organization = org, ValidFrom = from, WeeklyWorkHours = hours * 5,
        TargetMon = hours, TargetTue = hours, TargetWed = hours, TargetThu = hours, TargetFri = hours
    };

    // ── BuildWithRunningTotalAsync: details only for the display range ──

    [Fact]
    public async Task RunningTotal_DisplayRowsKeepSummariesAndCoreHours_AndTheCumulativeEqualsTheFullComputation()
    {
        var displayFrom = new DateOnly(2026, 9, 1);
        var displayTo = new DateOnly(2026, 9, 30);
        var (userId, orgId) = await WithDbAsync(async db =>
        {
            var org = new Organization { Name = "Totals rt", Slug = "totals-rt" };
            var user = new User { Email = "totals-rt@t.test", FirstName = "Rita", LastName = "Total" };
            db.AddRange(org, user);
            db.UserOrganizations.Add(new UserOrganization { User = user, Organization = org, InitialOvertimeHours = 3 });
            db.WorkSchedules.Add(FullTime(user, org, new DateOnly(2026, 1, 1)));
            for (var day = new DateOnly(2026, 8, 3); day <= displayTo; day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                db.TimeEntries.Add(Entry(user, org, day, 10, 0, 17, day.Day % 3 * 10, pause: 30, description: "Work"));
            }
            // Stored violations before and inside the display range; the entries (10:00-…) never cover 09:00
            foreach (var day in new[] { new DateOnly(2026, 8, 11), new DateOnly(2026, 9, 15) })
                db.CoreHoursChecks.Add(new CoreHoursCheck
                {
                    Organization = org, User = user, Date = day, WindowStart = new TimeOnly(9, 0), WindowEnd = new TimeOnly(15, 0),
                    ToleranceMinutes = 5, Issues = CoreHoursIssue.Late, MissingMinutes = 60, CheckedAt = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
            return (user.Id, org.Id);
        });

        var (display, cumulative) = await WithDbAsync(db =>
            DailyReportAggregator.BuildWithRunningTotalAsync(db, userId, orgId, displayFrom, displayTo, 3));
        var full = await WithDbAsync(db =>
            DailyReportAggregator.BuildAsync(db, userId, orgId, new DateOnly(2026, 8, 3), displayTo, 3));

        display.Should().HaveCount(30);
        display.First().Date.Should().Be(displayFrom);
        cumulative.Should().Be(full[^1].CumulativeOvertime);
        foreach (var row in display)
        {
            var same = full.Single(r => r.Date == row.Date);
            row.CumulativeOvertime.Should().Be(same.CumulativeOvertime, $"running total on {row.Date}");
            row.EntrySummary.Should().Be(same.EntrySummary, $"summary on {row.Date}");
            row.CoreHoursMissed.Should().Be(same.CoreHoursMissed, $"core hours on {row.Date}");
        }
        display.Single(r => r.Date == new DateOnly(2026, 9, 15)).CoreHoursMissed.Should().BeTrue();
        display.Where(r => r.Date != new DateOnly(2026, 9, 15)).Should().OnlyContain(r => !r.CoreHoursMissed);
        display.Single(r => r.Date == new DateOnly(2026, 9, 14)).EntrySummary.Should().StartWith("10:00-17:").And.Contain("(Work)");
        display.Where(r => r.Date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .Should().OnlyContain(r => r.EntrySummary.Length > 0);

        // detailsFrom: rows before it carry no details, the figures are unchanged
        var partial = await WithDbAsync(db =>
            DailyReportAggregator.BuildAsync(db, userId, orgId, new DateOnly(2026, 8, 3), displayTo, 3, detailsFrom: displayFrom));
        partial.Select(r => r.CumulativeOvertime).Should().Equal(full.Select(r => r.CumulativeOvertime));
        partial.Where(r => r.Date < displayFrom).Should().OnlyContain(r => r.EntrySummary == "" && !r.CoreHoursMissed);
        partial.Single(r => r.Date == new DateOnly(2026, 8, 11)).Should().Match<DailyReportRow>(r => !r.CoreHoursMissed);
        full.Single(r => r.Date == new DateOnly(2026, 8, 11)).CoreHoursMissed.Should().BeTrue("without detailsFrom every row is evaluated");
    }

    // ── GetTargetHours ──

    [Fact]
    public async Task TargetHours_OverlappingSchedules_TheLatestValidFromWins()
    {
        var (userId, orgId) = await WithDbAsync(async db =>
        {
            var org = new Organization { Name = "Totals ws", Slug = "totals-ws" };
            var user = new User { Email = "totals-ws@t.test", FirstName = "Will", LastName = "Schedule" };
            db.AddRange(org, user);
            db.UserOrganizations.Add(new UserOrganization { User = user, Organization = org });
            // Inserted out of order on purpose
            db.WorkSchedules.Add(new WorkSchedule { User = user, Organization = org, ValidFrom = new DateOnly(2026, 6, 15), TargetMon = 6 });
            db.WorkSchedules.Add(new WorkSchedule { User = user, Organization = org, ValidFrom = new DateOnly(2026, 1, 1), TargetMon = 8 });
            db.WorkSchedules.Add(new WorkSchedule
            {
                User = user, Organization = org, ValidFrom = new DateOnly(2026, 6, 1), ValidTo = new DateOnly(2026, 6, 30), TargetMon = 4
            });
            await db.SaveChangesAsync();
            return (user.Id, org.Id);
        });

        var rows = await WithDbAsync(db =>
            DailyReportAggregator.BuildAsync(db, userId, orgId, new DateOnly(2026, 5, 1), new DateOnly(2026, 7, 31), 0));
        double Target(int month, int day) => rows.Single(r => r.Date == new DateOnly(2026, month, day)).TargetHours;

        Target(5, 25).Should().Be(8);    // only the open-ended January schedule
        Target(6, 8).Should().Be(4);     // June schedule (later than January)
        Target(6, 15).Should().Be(6);    // three overlap: 15 June is the latest ValidFrom
        Target(6, 22).Should().Be(6);
        Target(7, 6).Should().Be(6);     // June schedule has ended, the 15 June one still runs
        Target(6, 16).Should().Be(0);    // Tuesday: no target in any of them
    }

    // ── TotalsAsync vs BuildAsync per member ──

    [Fact]
    public async Task TotalsAsync_EqualsThePerMemberBuild_ForFormerMembers_InitialOnly_AndPeriodsBeforeTheFirstEntry()
    {
        var periodFrom = new DateOnly(2026, 9, 7);
        var periodTo = new DateOnly(2026, 9, 27);
        var setup = await WithDbAsync(async db =>
        {
            var org = new Organization { Name = "Totals eq", Slug = "totals-eq" };
            var other = new Organization { Name = "Totals other", Slug = "totals-eq-other" };
            db.AddRange(org, other);
            db.Holidays.Add(new Holiday { Organization = org, Date = new DateOnly(2026, 9, 10), Name = "Fixed" });
            db.Holidays.Add(new Holiday { Organization = org, Date = new DateOnly(2000, 9, 22), Name = "Recurring half", IsRecurring = true, IsHalfDay = true });
            var custom = new AbsenceTypeDefinition { Organization = org, Name = "Fortbildung", CreditMode = AbsenceCreditMode.FillToTarget };
            db.Add(custom);

            User Person(string key, double initial, bool active = true)
            {
                var u = new User { Email = $"totals-eq-{key}@t.test", FirstName = key, LastName = "Eq" };
                db.UserOrganizations.Add(new UserOrganization { User = u, Organization = org, InitialOvertimeHours = initial, IsActive = active });
                return u;
            }

            // Long history with schedule change, absences, a failure, a running timer and an entry in another org
            var a = Person("a", 2.5);
            db.WorkSchedules.Add(FullTime(a, org, new DateOnly(2026, 1, 1)));
            db.WorkSchedules.Add(FullTime(a, org, new DateOnly(2026, 9, 14), 6));
            for (var day = new DateOnly(2026, 7, 1); day <= new DateOnly(2026, 9, 30); day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || day.Day % 7 == 0) continue;
                db.TimeEntries.Add(Entry(a, org, day, 8, 0, 16, day.Day % 4 * 15, pause: 30));
            }
            db.TimeEntries.Add(Entry(a, org, new DateOnly(2026, 9, 12), 22, 0, 3, 0, addDays: 1));   // Saturday night across midnight
            db.TimeEntries.Add(Entry(a, org, new DateOnly(2026, 9, 19), 18, 0, 4, 0, failure: true, addDays: 1));
            db.TimeEntries.Add(Entry(a, other, new DateOnly(2026, 9, 20), 8, 0, 18, 0));
            db.TimeEntries.Add(new TimeEntry { User = a, Organization = org, StartTime = Berlin.ToUtc(new DateTime(2026, 9, 25, 8, 0, 0)), IsRunning = true });
            db.AbsenceDays.Add(new AbsenceDay { User = a, Organization = org, Date = new DateOnly(2026, 9, 15), Type = AbsenceType.Vacation });
            db.AbsenceDays.Add(new AbsenceDay { User = a, Organization = org, Date = new DateOnly(2026, 9, 16), Type = AbsenceType.SickDay, IsHalfDay = true });
            db.AbsenceDays.Add(new AbsenceDay { User = a, Organization = org, Date = new DateOnly(2026, 9, 17), Type = AbsenceType.Other, CustomType = custom });
            db.AbsenceDays.Add(new AbsenceDay { User = a, Organization = org, Date = new DateOnly(2026, 6, 15), Type = AbsenceType.Vacation });   // before the first entry

            // A former member with entries
            var b = Person("b", -4, active: false);
            db.WorkSchedules.Add(FullTime(b, org, new DateOnly(2026, 8, 1)));
            for (var day = new DateOnly(2026, 8, 24); day <= new DateOnly(2026, 9, 11); day = day.AddDays(1))
                if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                    db.TimeEntries.Add(Entry(b, org, day, 9, 0, 13, 0));

            // Only an initial overtime, no entries at all
            var c = Person("c", 7.25);
            db.WorkSchedules.Add(FullTime(c, org, new DateOnly(2026, 1, 1)));

            // First entry after the period: the period lies before it
            var d = Person("d", 1);
            db.WorkSchedules.Add(FullTime(d, org, new DateOnly(2026, 1, 1)));
            db.TimeEntries.Add(Entry(d, org, new DateOnly(2026, 10, 1), 8, 0, 12, 0));

            // First entry inside the period (nothing before it counts), only a running timer otherwise
            var e = Person("e", 0);
            db.WorkSchedules.Add(FullTime(e, org, new DateOnly(2026, 1, 1)));
            db.TimeEntries.Add(Entry(e, org, new DateOnly(2026, 9, 21), 7, 0, 15, 0));
            db.TimeEntries.Add(new TimeEntry { User = e, Organization = org, StartTime = Berlin.ToUtc(new DateTime(2026, 9, 1, 8, 0, 0)), IsRunning = true });

            await db.SaveChangesAsync();
            return (OrgId: org.Id, Users: new[] { a, b, c, d, e }.ToDictionary(u => u.FirstName, u => u.Id));
        });

        var initials = new Dictionary<string, double> { ["a"] = 2.5, ["b"] = -4, ["c"] = 7.25, ["d"] = 1, ["e"] = 0 };
        var byUser = setup.Users.ToDictionary(kv => kv.Value, kv => initials[kv.Key]);

        foreach (var (pFrom, pTo) in new[] { (periodFrom, periodTo), (new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)), (new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)) })
        {
            var totals = await WithDbAsync(async db =>
            {
                var org = await db.Organizations.SingleAsync(o => o.Id == setup.OrgId);
                return await DailyReportAggregator.TotalsAsync(db, org, byUser, pFrom, pTo);
            });
            totals.Keys.Should().BeEquivalentTo(byUser.Keys);
            foreach (var (name, id) in setup.Users)
            {
                var expected = await WithDbAsync(db => PerMemberAsync(db, id, setup.OrgId, initials[name], pFrom, pTo));
                var actual = totals[id];
                actual.Target.Should().BeApproximately(expected.Target, 1e-9, $"target of {name} {pFrom}..{pTo}");
                actual.Worked.Should().BeApproximately(expected.Worked, 1e-9, $"worked of {name} {pFrom}..{pTo}");
                actual.Overtime.Should().BeApproximately(expected.Overtime, 1e-9, $"overtime of {name} {pFrom}..{pTo}");
                actual.Balance.Should().BeApproximately(expected.Balance, 1e-9, $"balance of {name} {pFrom}..{pTo}");
            }
        }

        // Spot checks of the edge cases
        var sept = await WithDbAsync(async db =>
            await DailyReportAggregator.TotalsAsync(db, await db.Organizations.SingleAsync(o => o.Id == setup.OrgId), byUser, periodFrom, periodTo));
        sept[setup.Users["c"]].Should().Be(new DailyReportAggregator.PeriodTotals(0, 0, 0, 7.25));
        sept[setup.Users["d"]].Should().Be(new DailyReportAggregator.PeriodTotals(0, 0, 0, 1));
        sept[setup.Users["a"]].FailureCount.Should().Be(1);
        sept[setup.Users["e"]].Worked.Should().Be(8, "07:00-15:00 without pause; days before the first entry do not count");
    }

    /// <summary>What the payroll computed per member before Phase 1: BuildAsync from the first finished entry (all time) to the period end.</summary>
    private static async Task<DailyReportAggregator.PeriodTotals> PerMemberAsync(TimeTrackingDbContext db, int userId, int orgId, double initial,
        DateOnly from, DateOnly to)
    {
        var first = await db.TimeEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.OrganizationId == orgId && !e.IsRunning)
            .MinAsync(e => (DateTime?)e.StartTime);
        if (first == null) return new(0, 0, 0, initial);
        var rows = await DailyReportAggregator.BuildAsync(db, userId, orgId, Berlin.LocalDate(first.Value), to, initial);
        var period = rows.Where(r => r.Date >= from).ToList();
        return new(period.Sum(r => r.TargetHours), period.Sum(r => r.NetWorkedHours), period.Sum(r => r.Overtime),
            rows.Count > 0 ? rows[^1].CumulativeOvertime : initial);
    }

    // ── Payroll rows on a hand-computed week ──

    [Fact]
    public async Task PayrollRows_MatchAHandComputedWeek()
    {
        var setup = await WithDbAsync(async db =>
        {
            var org = new Organization { Name = "Totals pay", Slug = "totals-pay", MemberTimeEntryVisibility = true };
            var m = new User { Email = "totals-pay-m@t.test", FirstName = "Paul", LastName = "Pay" };
            var former = new User { Email = "totals-pay-f@t.test", FirstName = "Fritz", LastName = "Former" };
            var idle = new User { Email = "totals-pay-i@t.test", FirstName = "Ida", LastName = "Idle" };
            db.AddRange(org, m, former, idle);
            db.UserOrganizations.Add(new UserOrganization { User = m, Organization = org, InitialOvertimeHours = 10, PersonnelNumber = "P-1" });
            db.UserOrganizations.Add(new UserOrganization { User = former, Organization = org, IsActive = false });
            db.UserOrganizations.Add(new UserOrganization { User = idle, Organization = org, InitialOvertimeHours = 2 });
            db.WorkSchedules.Add(FullTime(m, org, new DateOnly(2026, 1, 1)));
            db.WorkSchedules.Add(FullTime(idle, org, new DateOnly(2026, 1, 1)));

            // History week: only Monday 7 Sep worked (8 h net), Tue-Fri missing: -32 h
            db.TimeEntries.Add(Entry(m, org, new DateOnly(2026, 9, 7), 8, 0, 17, 0, pause: 60));
            // Period Mon 14 - Fri 18 Sep
            db.TimeEntries.Add(Entry(m, org, new DateOnly(2026, 9, 14), 8, 0, 18, 0, pause: 30));          // 9.5 h: +1.5
            db.AbsenceDays.Add(new AbsenceDay { User = m, Organization = org, Date = new DateOnly(2026, 9, 15), Type = AbsenceType.Vacation });
            db.AbsenceDays.Add(new AbsenceDay { User = m, Organization = org, Date = new DateOnly(2026, 9, 16), Type = AbsenceType.SickDay });
            db.AbsenceDays.Add(new AbsenceDay { User = m, Organization = org, Date = new DateOnly(2026, 9, 17), Type = AbsenceType.Other, IsHalfDay = true });
            db.TimeEntries.Add(Entry(m, org, new DateOnly(2026, 9, 17), 8, 0, 12, 0));                      // half target 4 h, worked 4 h
            db.TimeEntries.Add(Entry(m, org, new DateOnly(2026, 9, 18), 8, 0, 18, 0, failure: true));      // counts 0: -8
            db.TimeEntries.Add(Entry(m, org, new DateOnly(2026, 9, 11), 8, 0, 18, 0, failure: true));      // before the period: not in its count

            // Former member with 3 h in the period, no schedule
            db.TimeEntries.Add(Entry(former, org, new DateOnly(2026, 9, 16), 9, 0, 12, 0));
            await db.SaveChangesAsync();
            return (OrgId: org.Id, M: m.Id, Former: former.Id, Idle: idle.Id);
        });

        var rows = await WithDbAsync(async db =>
        {
            var org = await db.Organizations.SingleAsync(o => o.Id == setup.OrgId);
            return await new PayrollExportService(db, NullLogger<PayrollExportService>.Instance)
                .RowsAsync(org, new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 18));
        });

        rows.Should().HaveCount(3);
        var m = rows.Single(r => r.UserId == setup.M);
        m.Should().Be(new PayrollRow(setup.M, "P-1", "Paul Pay", "totals-pay-m@t.test",
            TargetHours: 20, WorkedHours: 13.5, OvertimeHours: -6.5, OvertimeBalance: 10 - 32 - 6.5,
            VacationDays: 1, SickDays: 1, OtherAbsenceDays: 0.5, FailureEntryCount: 1, IsFormerMember: false));
        var f = rows.Single(r => r.UserId == setup.Former);
        f.IsFormerMember.Should().BeTrue();
        f.WorkedHours.Should().Be(3);
        f.TargetHours.Should().Be(0);
        f.OvertimeBalance.Should().Be(3);
        rows.Single(r => r.UserId == setup.Idle).Should().Match<PayrollRow>(r =>
            r.TargetHours == 0 && r.WorkedHours == 0 && r.OvertimeBalance == 2 && !r.IsFormerMember && r.FailureEntryCount == 0);
    }
}
