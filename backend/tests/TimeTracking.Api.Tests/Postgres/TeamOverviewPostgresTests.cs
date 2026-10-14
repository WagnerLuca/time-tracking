using System.Data.Common;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;
using Xunit.Abstractions;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// The team overview on real Postgres with 50 members and half a year of entries each: a fixed, single-digit
/// number of queries however large the team is, and every balance equal to the per-member computation.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TeamOverviewPostgresTests
{
    private readonly PostgresApiFactory _factory;
    private readonly ITestOutputHelper _output;

    public TeamOverviewPostgresTests(PostgresApiFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    private sealed class CountingInterceptor : DbCommandInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private TimeTrackingDbContext Db(CountingInterceptor? counter = null)
    {
        var options = new DbContextOptionsBuilder<TimeTrackingDbContext>().UseNpgsql(new Npgsql.NpgsqlConnectionStringBuilder(_factory.SuperuserConnectionString) { Database = "timetracking" }.ConnectionString);
        if (counter != null) options.AddInterceptors(counter);
        return new TimeTrackingDbContext(options.Options);
    }

    [Fact]
    public async Task FiftyMembers_SingleDigitQueries_BalancesEqualThePerMemberComputation()
    {
        _ = _factory.Server;   // starts the app: migrations + seed
        var clock = OrgClock.For("Europe/Berlin");
        var today = clock.Today();
        int orgId, ownerId;
        await using (var db = Db())
        {
            var org = new Organization { Name = "Team perf", Slug = "pg-team-perf", MemberTimeEntryVisibility = true };
            var owner = new User { Email = "pg-team-owner@pg.test", FirstName = "Olga", LastName = "Owner" };
            db.AddRange(org, owner);
            db.UserOrganizations.Add(new UserOrganization { User = owner, Organization = org, Role = OrganizationRole.Owner });
            db.Holidays.Add(new Holiday { Organization = org, Name = "Fixed", Date = today.AddDays(-40) });
            var group = new Group { Organization = org, Name = "Half" };
            db.Groups.Add(group);
            for (var i = 0; i < 50; i++)
            {
                var user = new User { Email = $"pg-team-{i}@pg.test", FirstName = $"M{i:00}", LastName = "Test" };
                db.UserOrganizations.Add(new UserOrganization { User = user, Organization = org, InitialOvertimeHours = i % 5 });
                if (i % 2 == 0) db.GroupMembers.Add(new GroupMember { Group = group, User = user });
                db.WorkSchedules.Add(new WorkSchedule
                {
                    User = user, Organization = org, ValidFrom = today.AddDays(-200), WeeklyWorkHours = 40,
                    TargetMon = 8, TargetTue = 8, TargetWed = 8, TargetThu = 8, TargetFri = 8
                });
                for (var d = 180 - i; d >= 1; d--)
                {
                    var day = today.AddDays(-d);
                    if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                    var start = clock.ToUtc(day.ToDateTime(new TimeOnly(8, 0)));
                    db.TimeEntries.Add(new TimeEntry
                    {
                        User = user, Organization = org, StartTime = start, EndTime = start.AddMinutes(480 + (d + i) % 60),
                        PauseDurationMinutes = 30, PauseIsManual = true, CreatedAt = start, UpdatedAt = start
                    });
                }
                db.AbsenceDays.Add(new AbsenceDay { User = user, Organization = org, Date = today.AddDays(-10), Type = AbsenceType.Vacation, IsHalfDay = i % 3 == 0 });
                if (i % 10 == 0)
                {
                    var failure = clock.ToUtc(today.AddDays(-7).ToDateTime(new TimeOnly(18, 0)));
                    db.TimeEntries.Add(new TimeEntry { User = user, Organization = org, StartTime = failure, EndTime = failure.AddHours(5), IsFailure = true });
                }
                if (i % 4 == 0)
                    db.TimeEntries.Add(new TimeEntry { User = user, Organization = org, StartTime = DateTime.UtcNow.AddMinutes(-5), IsRunning = true });
            }
            await db.SaveChangesAsync();
            orgId = org.Id;
            ownerId = owner.Id;
        }

        var counter = new CountingInterceptor();
        TeamOverviewResponse overview;
        await using (var db = Db(counter))
        {
            var service = new OrganizationService(db, NullLogger<OrganizationService>.Instance);
            (await service.GetTeamOverviewAsync("pg-team-perf", ownerId)).Data.Should().NotBeNull();   // warm-up (query compilation)
            counter.Count = 0;
            var watch = Stopwatch.StartNew();
            overview = (await service.GetTeamOverviewAsync("pg-team-perf", ownerId)).Data!;
            watch.Stop();
            _output.WriteLine($"team overview, 50 members: {counter.Count} queries, {watch.ElapsedMilliseconds} ms");
            counter.Count.Should().BeLessThan(10);
            watch.ElapsedMilliseconds.Should().BeLessThan(2000);
        }

        overview.Members.Should().HaveCount(50);
        overview.Members.Count(m => m.Present == true).Should().Be(13);
        overview.Members.Sum(m => m.FailureCount).Should().Be(5);
        await using (var db = Db())
        {
            foreach (var m in overview.Members)
            {
                var initial = await db.UserOrganizations.Where(uo => uo.UserId == m.UserId && uo.OrganizationId == orgId)
                    .Select(uo => uo.InitialOvertimeHours).SingleAsync();
                var (_, cumulative) = await DailyReportAggregator.BuildWithRunningTotalAsync(db, m.UserId, orgId, today, today, initial);
                m.BalanceHours.Should().Be(Math.Round(cumulative, 2), $"member {m.FirstName}");
            }
        }
    }
}
