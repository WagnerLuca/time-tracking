using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Controllers;
using TimeTracking.Api.Tests.Infrastructure;
using Xunit.Abstractions;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// Phase 1 of docs/PERFORMANCE_PLAN.md on real Postgres: the bounded overlap query, the batched payroll (SQL commands
/// independent of the member count), the team overview's presence and failure counts from one entries query, the
/// <c>ExecuteDelete</c> retention path and the <c>PerformanceIndexes</c> migration.
/// </summary>
[Collection(PostgresCollection.Name)]
public class PerformancePhase1PostgresTests
{
    private static readonly OrgClock Berlin = OrgClock.For("Europe/Berlin");
    private readonly PostgresApiFactory _factory;
    private readonly ITestOutputHelper _output;

    public PerformancePhase1PostgresTests(PostgresApiFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    /// <summary>A context on the superuser connection (bypasses RLS) for setup and direct service calls.</summary>
    private TimeTrackingDbContext Db(QueryCounter? counter = null)
    {
        _ = _factory.Server;   // migrations + seed
        var options = new DbContextOptionsBuilder<TimeTrackingDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(_factory.SuperuserConnectionString) { Database = "timetracking" }.ConnectionString);
        if (counter != null) options.AddInterceptors(counter);
        return new TimeTrackingDbContext(options.Options);
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    // ── EntryTimes.OverlapsAsync ──

    [Fact]
    public async Task Overlaps_BoundedByLongestEntry_RunningTimersAnyAge_AllOrgs_ExceptEntry_AndAdjacency()
    {
        int u1, u2, u3, crossing, dst;
        await using (var db = Db())
        {
            var org = new Organization { Name = "pg-ovl", Slug = "pg-ovl" };
            var other = new Organization { Name = "pg-ovl other", Slug = "pg-ovl-other" };
            var a = new User { Email = "pg-ovl-a@pg.test", FirstName = "A", LastName = "Ovl" };
            var b = new User { Email = "pg-ovl-b@pg.test", FirstName = "B", LastName = "Ovl" };
            var c = new User { Email = "pg-ovl-c@pg.test", FirstName = "C", LastName = "Ovl" };
            db.AddRange(org, other, a, b, c);
            TimeEntry Finished(User u, Organization o, DateTime s, DateTime e) =>
                new() { User = u, Organization = o, StartTime = s, EndTime = e, CreatedAt = s, UpdatedAt = s };
            // Started the previous day, runs past midnight
            var cross = Finished(a, org, Utc(2026, 5, 4, 20), Utc(2026, 5, 5, 4));
            // 25 h on the fall-back night: 24 Oct 22:00 to 25 Oct 22:00 Berlin
            var night = Finished(a, org, Berlin.ToUtc(new DateTime(2026, 10, 24, 22, 0, 0)), Berlin.ToUtc(new DateTime(2026, 10, 25, 22, 0, 0)));
            // 6.5 days (legacy, still inside the 7-day bound)
            var week = Finished(a, org, Utc(2026, 6, 1, 0), Utc(2026, 6, 7, 12));
            // In another organization of the same user
            var elsewhere = Finished(a, other, Utc(2026, 7, 1, 8), Utc(2026, 7, 1, 12));
            db.TimeEntries.AddRange(cross, night, week, elsewhere);
            // Running timers: one started long ago, one started later in the day
            db.TimeEntries.Add(new TimeEntry { User = b, Organization = org, StartTime = Utc(2026, 4, 1, 8), IsRunning = true });
            db.TimeEntries.Add(new TimeEntry { User = c, Organization = org, StartTime = Utc(2026, 5, 10, 12), IsRunning = true });
            await db.SaveChangesAsync();
            (u1, u2, u3, crossing, dst) = (a.Id, b.Id, c.Id, cross.Id, night.Id);
        }

        async Task<bool> Overlaps(int user, DateTime from, DateTime to, int? except = null)
        {
            await using var db = Db();
            return await EntryTimes.OverlapsAsync(db, user, from, to, except);
        }

        (await Overlaps(u1, Utc(2026, 5, 5, 3), Utc(2026, 5, 5, 5))).Should().BeTrue("the entry from the previous day runs until 04:00");
        (await Overlaps(u1, Utc(2026, 5, 5, 0), Utc(2026, 5, 5, 1))).Should().BeTrue();
        (await Overlaps(u1, Utc(2026, 5, 5, 4), Utc(2026, 5, 5, 6))).Should().BeFalse("starting exactly at its end is adjacent");
        (await Overlaps(u1, Utc(2026, 5, 4, 18), Utc(2026, 5, 4, 20))).Should().BeFalse("ending exactly at its start is adjacent");
        (await Overlaps(u1, Utc(2026, 5, 5, 3), Utc(2026, 5, 5, 5), crossing)).Should().BeFalse("the entry itself is excepted");

        var lateOn25th = Berlin.ToUtc(new DateTime(2026, 10, 25, 21, 30, 0));
        (await Overlaps(u1, lateOn25th, lateOn25th.AddMinutes(15))).Should().BeTrue("the 25 h DST entry still runs");
        (await Overlaps(u1, lateOn25th, lateOn25th.AddMinutes(15), dst)).Should().BeFalse();

        (await Overlaps(u1, Utc(2026, 6, 7, 10), Utc(2026, 6, 7, 11))).Should().BeTrue("a 6.5-day entry is within the 7-day bound");
        (await Overlaps(u1, Utc(2026, 7, 1, 9), Utc(2026, 7, 1, 10))).Should().BeTrue("overlaps count across organizations");

        (await Overlaps(u2, Utc(2026, 5, 10, 8), Utc(2026, 5, 10, 9))).Should().BeTrue("a running timer started 39 days earlier still runs");
        (await Overlaps(u2, Utc(2026, 3, 31, 8), Utc(2026, 3, 31, 9))).Should().BeFalse("before the running timer started");
        (await Overlaps(u2, Utc(2026, 3, 31, 8), Utc(2026, 4, 1, 8))).Should().BeFalse("ending exactly when the timer started");

        (await Overlaps(u3, Utc(2026, 5, 10, 8), Utc(2026, 5, 10, 12))).Should().BeFalse("the timer starts after the window");
        (await Overlaps(u3, Utc(2026, 5, 10, 8), Utc(2026, 5, 10, 12, 1))).Should().BeTrue();
    }

    // ── Payroll: SQL commands independent of the member count ──

    private async Task<int> PayrollOrgAsync(string slug, int members)
    {
        await using var db = Db();
        var org = new Organization { Name = slug, Slug = slug, MemberTimeEntryVisibility = true };
        db.Organizations.Add(org);
        db.Holidays.Add(new Holiday { Organization = org, Date = new DateOnly(2026, 9, 10), Name = "Fixed" });
        for (var i = 0; i <= members; i++)
        {
            var former = i == members;   // one former member with time in the period
            var user = new User { Email = $"{slug}-{i}@pg.test", FirstName = $"M{i:00}", LastName = "Pay" };
            db.UserOrganizations.Add(new UserOrganization { User = user, Organization = org, IsActive = !former, InitialOvertimeHours = i % 3 });
            db.WorkSchedules.Add(new WorkSchedule
            {
                User = user, Organization = org, ValidFrom = new DateOnly(2026, 1, 1), WeeklyWorkHours = 40,
                TargetMon = 8, TargetTue = 8, TargetWed = 8, TargetThu = 8, TargetFri = 8
            });
            for (var day = new DateOnly(2026, 7, 1); day <= new DateOnly(2026, 9, 30); day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                var start = Berlin.ToUtc(day.ToDateTime(new TimeOnly(8, 0)));
                db.TimeEntries.Add(new TimeEntry
                {
                    User = user, Organization = org, StartTime = start, EndTime = start.AddMinutes(480 + i), PauseDurationMinutes = 30,
                    PauseIsManual = true, IsFailure = day.Day == 15 && i % 4 == 0, CreatedAt = start, UpdatedAt = start
                });
            }
            db.AbsenceDays.Add(new AbsenceDay { User = user, Organization = org, Date = new DateOnly(2026, 9, 21), Type = AbsenceType.Vacation, IsHalfDay = i % 2 == 0 });
            db.AbsenceDays.Add(new AbsenceDay { User = user, Organization = org, Date = new DateOnly(2026, 9, 22), Type = AbsenceType.SickDay });
        }
        await db.SaveChangesAsync();
        return org.Id;
    }

    private async Task<(int Commands, List<PayrollRow> Rows)> PayrollCommandsAsync(int orgId)
    {
        var counter = new QueryCounter();
        await using var db = Db(counter);
        var org = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == orgId);
        var service = new PayrollExportService(db, NullLogger<PayrollExportService>.Instance);
        await service.RowsAsync(org, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));   // warm-up (query compilation)
        counter.Count = 0;
        var rows = await service.RowsAsync(org, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        return (counter.Count, rows);
    }

    [Fact]
    public async Task Payroll_SqlCommands_DoNotGrowWithTheMemberCount_AndRowsEqualThePerMemberComputation()
    {
        var small = await PayrollOrgAsync("pg-pay-5", 5);
        var large = await PayrollOrgAsync("pg-pay-30", 30);

        var (smallCount, smallRows) = await PayrollCommandsAsync(small);
        var (largeCount, largeRows) = await PayrollCommandsAsync(large);
        _output.WriteLine($"payroll: 5 members {smallCount} commands, 30 members {largeCount} commands");
        smallRows.Should().HaveCount(6);
        largeRows.Should().HaveCount(31);
        largeCount.Should().Be(smallCount, "the payroll loads every member's data in batched queries");
        largeCount.Should().BeLessThanOrEqualTo(10);

        largeRows.Count(r => r.IsFormerMember).Should().Be(1);
        largeRows.Sum(r => r.FailureEntryCount).Should().Be(8, "members 0, 4, …, 28 have a failure on 15 Sep");
        largeRows.Should().OnlyContain(r => r.SickDays == 1 && (r.VacationDays == 1 || r.VacationDays == 0.5));

        // Every row's hours equal BuildAsync from the member's first entry
        await using var db = Db();
        foreach (var row in largeRows)
        {
            var initial = await db.UserOrganizations.IgnoreQueryFilters()
                .Where(uo => uo.UserId == row.UserId && uo.OrganizationId == large).Select(uo => uo.InitialOvertimeHours).SingleAsync();
            var rows = await DailyReportAggregator.BuildAsync(db, row.UserId, large, new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30), initial);
            var period = rows.Where(r => r.Date >= new DateOnly(2026, 9, 1)).ToList();
            row.TargetHours.Should().Be(Math.Round(period.Sum(r => r.TargetHours), 2), row.Name);
            row.WorkedHours.Should().Be(Math.Round(period.Sum(r => r.NetWorkedHours), 2), row.Name);
            row.OvertimeHours.Should().Be(Math.Round(period.Sum(r => r.Overtime), 2), row.Name);
            row.OvertimeBalance.Should().Be(Math.Round(rows[^1].CumulativeOvertime, 2), row.Name);
        }
    }

    // ── Team overview: presence and failures from one query ──

    [Fact]
    public async Task TeamOverview_PresenceFromRunningTimers_FailureCounts_AndWithoutTimeVisibility()
    {
        int ownerId;
        Dictionary<string, int> ids;
        var today = Berlin.Today();
        await using (var db = Db())
        {
            var org = new Organization { Name = "pg-team-p1", Slug = "pg-team-p1", MemberTimeEntryVisibility = true };
            var other = new Organization { Name = "pg-team-p1 other", Slug = "pg-team-p1-other" };
            var owner = new User { Email = "pg-team-p1-owner@pg.test", FirstName = "Owner", LastName = "P1" };
            db.AddRange(org, other, owner);
            db.UserOrganizations.Add(new UserOrganization { User = owner, Organization = org, Role = OrganizationRole.Owner });
            var users = new[] { "running", "failure", "both", "idle", "elsewhere" }
                .ToDictionary(k => k, k => new User { Email = $"pg-team-p1-{k}@pg.test", FirstName = k, LastName = "P1" });
            foreach (var u in users.Values)
            {
                db.UserOrganizations.Add(new UserOrganization { User = u, Organization = org, InitialOvertimeHours = 1.5 });
                db.UserOrganizations.Add(new UserOrganization { User = u, Organization = other });
            }
            var failureStart = Berlin.ToUtc(today.AddDays(-3).ToDateTime(new TimeOnly(18, 0)));
            TimeEntry Failure(User u, Organization o, int daysBack) => new()
            {
                User = u, Organization = o, StartTime = failureStart.AddDays(-daysBack), EndTime = failureStart.AddDays(-daysBack).AddHours(6), IsFailure = true
            };
            TimeEntry Running(User u, Organization o, int minutes) => new() { User = u, Organization = o, StartTime = DateTime.UtcNow.AddMinutes(-minutes), IsRunning = true };
            db.TimeEntries.Add(Running(users["running"], org, 30));
            db.TimeEntries.AddRange(Failure(users["failure"], org, 0), Failure(users["failure"], org, 10));
            db.TimeEntries.AddRange(Failure(users["both"], org, 1), Running(users["both"], org, 5));
            // Running and failing in another organization: neither present here nor counted
            db.TimeEntries.AddRange(Running(users["elsewhere"], other, 20), Failure(users["elsewhere"], other, 0));
            await db.SaveChangesAsync();
            ownerId = owner.Id;
            ids = users.ToDictionary(kv => kv.Key, kv => kv.Value.Id);
        }

        var counter = new QueryCounter();
        await using (var db = Db(counter))
        {
            var service = new OrganizationService(db, NullLogger<OrganizationService>.Instance);
            (await service.GetTeamOverviewAsync("pg-team-p1", ownerId)).Data.Should().NotBeNull();   // warm-up
            counter.Count = 0;
            var overview = (await service.GetTeamOverviewAsync("pg-team-p1", ownerId)).Data!;
            _output.WriteLine($"team overview (5 members): {counter.Count} commands");
            counter.Count.Should().BeLessThanOrEqualTo(9);

            var byName = overview.Members.ToDictionary(m => m.FirstName);
            byName["running"].Present.Should().BeTrue();
            byName["running"].PresentSince.Should().NotBeNull();
            byName["running"].FailureCount.Should().Be(0);
            byName["running"].BalanceHours.Should().Be(1.5, "a running timer counts nothing yet");
            byName["failure"].Present.Should().BeFalse("failure entries are never running");
            byName["failure"].FailureCount.Should().Be(2);
            byName["both"].Present.Should().BeTrue();
            byName["both"].FailureCount.Should().Be(1);
            byName["idle"].Present.Should().BeFalse();
            byName["idle"].FailureCount.Should().Be(0);
            byName["idle"].BalanceHours.Should().Be(1.5);
            byName["elsewhere"].Present.Should().BeFalse();
            byName["elsewhere"].FailureCount.Should().Be(0);
        }

        // Without time visibility for admins: no balances or failure counts, presence still from the running timers
        await using (var db = Db())
        {
            var org = await db.Organizations.SingleAsync(o => o.Slug == "pg-team-p1");
            org.MemberTimeEntryVisibility = false;
            await db.SaveChangesAsync();
        }
        await using (var db = Db())
        {
            var overview = (await new OrganizationService(db, NullLogger<OrganizationService>.Instance).GetTeamOverviewAsync("pg-team-p1", ownerId)).Data!;
            overview.Members.Should().OnlyContain(m => m.BalanceHours == null && m.FailureCount == null);
            var present = overview.Members.Where(m => m.Present == true).Select(m => m.FirstName);
            if (overview.Members.Any(m => m.Present != null))
                present.Should().BeEquivalentTo("running", "both");
        }
    }

    // ── Retention cleanup: the ExecuteDelete path ──

    [Fact]
    public async Task Retention_ExecuteDelete_RemovesOldRows_KeepsFreshAndUnread()
    {
        _ = _factory.Server;
        var ids = await RetentionScenario.SeedAsync(_factory.Services, "pg-ret");
        await RetentionScenario.RunAsync(_factory.Services);
        await RetentionScenario.AssertAsync(_factory.Services, ids);
    }

    // ── Migration PerformanceIndexes ──

    [Fact]
    public async Task Migration_CreatesThePerformanceIndexes()
    {
        _ = _factory.Server;
        await using var conn = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_factory.SuperuserConnectionString) { Database = "timetracking" }.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT indexname, indexdef FROM pg_indexes WHERE schemaname = 'public'", conn);
        var indexes = new Dictionary<string, string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                indexes[reader.GetString(0)] = reader.GetString(1);

        indexes.Should().ContainKeys(
            "IX_TimeEntries_UserId_Failure", "IX_OrgRequests_OrganizationId_Status", "IX_OrgRequests_OrganizationId_CreatedAt",
            "IX_Notifications_IsRead_CreatedAt", "IX_RefreshTokens_ExpiresAt", "IX_TerminalPunches_ReceivedAt", "IX_TimeEntryChanges_UserId");
        indexes["IX_TimeEntries_UserId_Failure"].Should().Contain("WHERE \"IsFailure\"");
        indexes["IX_OrgRequests_OrganizationId_Status"].Should().Contain("(\"OrganizationId\", \"Status\")");
        indexes.Should().NotContainKey("IX_OrgRequests_OrganizationId", "the composite indexes lead with OrganizationId and replace it");
    }
}
