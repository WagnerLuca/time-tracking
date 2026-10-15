using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;
using Xunit.Abstractions;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// The Organigramm on real Postgres with 100 members over 3 levels of groups: a fixed, small number of queries
/// for the owner and for a normal member, whatever the size.
/// </summary>
[Collection(PostgresCollection.Name)]
public class OrgChartPostgresTests
{
    private readonly PostgresApiFactory _factory;
    private readonly ITestOutputHelper _output;

    public OrgChartPostgresTests(PostgresApiFactory factory, ITestOutputHelper output)
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
    public async Task HundredMembers_ThreeLevels_FixedQueryCount_ForOwnerAndMember()
    {
        _ = _factory.Server;   // starts the app: migrations + seed
        int ownerId, viewerId;
        int[] depts, areas, teams;
        var expectedForViewer = new HashSet<int>();
        await using (var db = Db())
        {
            // Only Member is visible outside own groups, so the viewer sees their department only; presence in groups
            var org = new Organization
            {
                Name = "Chart perf", Slug = "pg-chart-perf",
                VisibleInGroups = Organization.DefaultVisibility | MemberInfo.Presence, VisibleOutsideGroups = MemberInfo.None
            };
            var owner = new User { Email = "pg-chart-owner@pg.test", FirstName = "Olga", LastName = "Owner" };
            db.AddRange(org, owner);
            db.UserOrganizations.Add(new UserOrganization { User = owner, Organization = org, Role = OrganizationRole.Owner });

            // 2 departments → 4 areas → 8 teams
            var deptGroups = Enumerable.Range(0, 2).Select(d => new Group { Organization = org, Name = $"Dept {d}" }).ToArray();
            var areaGroups = Enumerable.Range(0, 4).Select(a => new Group
                { Organization = org, Name = $"Area {a}", ParentGroup = deptGroups[a / 2], GroupAdminCanManageMembers = true }).ToArray();
            var teamGroups = Enumerable.Range(0, 8).Select(t => new Group { Organization = org, Name = $"Team {t}", ParentGroup = areaGroups[t / 2] }).ToArray();
            db.Groups.AddRange(deptGroups.Concat(areaGroups).Concat(teamGroups));

            var users = new User[100];
            for (var i = 0; i < 100; i++)
            {
                var user = users[i] = new User { Email = $"pg-chart-{i}@pg.test", FirstName = $"M{i:00}", LastName = "Test" };
                db.UserOrganizations.Add(new UserOrganization { User = user, Organization = org });
                if (i % 10 != 0)   // every tenth in no group
                    db.GroupMembers.Add(new GroupMember { Group = teamGroups[i % 8], User = user });
                if (i % 15 == 5)   // a few area leads on top
                    db.GroupMembers.Add(new GroupMember { Group = areaGroups[i / 15 % 4], User = user, Role = GroupRole.Admin });
                if (i % 4 == 0)
                    db.TimeEntries.Add(new TimeEntry { User = user, Organization = org, StartTime = DateTime.UtcNow.AddMinutes(-5 - i), IsRunning = true });
                if (i % 7 == 0)
                {
                    var failure = DateTime.UtcNow.AddDays(-3);
                    db.TimeEntries.Add(new TimeEntry { User = user, Organization = org, StartTime = failure, EndTime = failure.AddHours(5), IsFailure = true });
                }
            }
            await db.SaveChangesAsync();
            ownerId = owner.Id;
            viewerId = users[1].Id;   // Team 1 → Area 0 → Dept 0, no lead
            depts = deptGroups.Select(g => g.Id).ToArray();
            areas = areaGroups.Select(g => g.Id).ToArray();
            teams = teamGroups.Select(g => g.Id).ToArray();
            for (var i = 0; i < 100; i++)
                if ((i % 10 != 0 && i % 8 / 4 == 0) || (i % 15 == 5 && i / 15 % 4 / 2 == 0))
                    expectedForViewer.Add(users[i].Id);
        }

        var counter = new CountingInterceptor();
        await using (var db = Db(counter))
        {
            var service = new OrganizationService(db, NullLogger<OrganizationService>.Instance);

            (await service.GetOrgChartAsync("pg-chart-perf", ownerId)).Data.Should().NotBeNull();   // warm-up (query compilation)
            counter.Count = 0;
            var chart = (await service.GetOrgChartAsync("pg-chart-perf", ownerId)).Data!;
            _output.WriteLine($"org chart, owner, 100 members: {counter.Count} queries");
            counter.Count.Should().BeLessThanOrEqualTo(6);

            chart.People.Should().HaveCount(101);
            chart.People.Should().OnlyContain(p => p.Email != null && p.Groups != null);
            chart.People.Count(p => p.Present == true).Should().Be(25, "failure entries are not presence");
            chart.People.Count(p => p.Groups!.Count == 0).Should().Be(8);   // every tenth unless an area lead (20, 50, 80) + the owner
            chart.People.SelectMany(p => p.Groups!).Count(m => m.IsLead).Should().Be(7);
            chart.Groups.Should().HaveCount(14).And.OnlyContain(g => g.CanManageMembers);
            chart.Groups.Where(g => depts.Contains(g.Id)).Should().OnlyContain(g => g.ParentGroupId == null);
            for (var a = 0; a < 4; a++) chart.Groups.Single(g => g.Id == areas[a]).ParentGroupId.Should().Be(depts[a / 2]);
            for (var t = 0; t < 8; t++) chart.Groups.Single(g => g.Id == teams[t]).ParentGroupId.Should().Be(areas[t / 2]);

            (await service.GetOrgChartAsync("pg-chart-perf", viewerId)).Data.Should().NotBeNull();
            counter.Count = 0;
            var seen = (await service.GetOrgChartAsync("pg-chart-perf", viewerId)).Data!;
            _output.WriteLine($"org chart, member, 100 members: {counter.Count} queries, {seen.People.Count} people visible");
            counter.Count.Should().BeLessThanOrEqualTo(6);

            seen.People.Select(p => p.UserId).Should().BeEquivalentTo(expectedForViewer.Append(viewerId).Distinct());
            seen.Groups.Should().HaveCount(14).And.OnlyContain(g => !g.CanManageMembers);
            seen.People.Where(p => p.UserId != viewerId).Should().OnlyContain(p => p.Present != null && p.Groups != null && p.Email != null);
        }
    }
}
