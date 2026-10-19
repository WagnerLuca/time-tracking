using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;
using Xunit.Abstractions;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// Phase 2 query budget (docs/PERFORMANCE_PLAN.md "Correctness net"): with the checkpoints stored, the balance endpoint, the
/// team overview and the payroll export send a fixed number of SQL commands — the same for one and for three years of
/// history — and the balance reads entries of at most about a month; the invalidation hook adds no query to an everyday save
/// in the current month. Runs on the perf container (<see cref="PerfApiFactory"/>: no background jobs, SQL counted per
/// request) with two <see cref="PerfSeeder"/> organizations of 10 people, but is not a Perf test: it runs in the normal suite.
/// </summary>
[Collection(PerfCollection.Name)]
public class BalanceQueryBudgetTests(PerfApiFactory factory, ITestOutputHelper output)
{
    private const string Password = "Budget-Password1";

    private sealed record Calls(int Balance, int TeamOverview, int Payroll);

    [Fact]
    public async Task AfterWarmUp_FixedQueryCounts_IndependentOfHistoryLength_HookFreeInTheCurrentMonth()
    {
        const string database = "balance_budget";
        var (app, superuser, _) = await factory.ForDatabaseAsync(database);
        try
        {
            var seeds = new List<PerfSeedResult>();
            await using (var conn = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(superuser) { CommandTimeout = 600 }.ConnectionString))
                foreach (var years in new[] { 1, 3 })
                    seeds.Add(await PerfSeeder.SeedAsync(conn, new PerfSeedOptions
                    {
                        Employees = 10, Years = years, Seed = 5, OrgSlug = $"budget-{years}y", Password = Password, CoreHours = false
                    }));

            var counts = new List<Calls>();
            foreach (var seed in seeds)
                counts.Add(await MeasureAsync(app, seed));
            output.WriteLine($"1 year: {counts[0]}, 3 years: {counts[1]}");
            counts[1].Should().Be(counts[0], "with checkpoints the cost does not grow with the years of history");
            // Whole requests (auth filter, role/visibility checks, the dashboard's planned rows included), measured 2026-10-19:
            // balance 18, team overview 10, payroll 11 — ceilings, lower them when the requests get cheaper
            counts[0].Balance.Should().BeLessThanOrEqualTo(18);
            counts[0].TeamOverview.Should().BeLessThanOrEqualTo(10);
            counts[0].Payroll.Should().BeLessThanOrEqualTo(11);

            await HookAddsNoQueryAsync(superuser, seeds[0]);
        }
        finally
        {
            await app.DisposeAsync();
            await factory.DropDatabaseAsync(database);
        }
    }

    private async Task<Calls> MeasureAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app, PerfSeedResult seed)
    {
        var member = app.CreateClient();
        TestHelpers.SetBearerToken(member, (await TestHelpers.LoginAsync(member, PerfSeedResult.MemberEmail(seed.Slug, 1), Password)).AccessToken);
        var owner = app.CreateClient();
        TestHelpers.SetBearerToken(owner, (await TestHelpers.LoginAsync(owner, PerfSeedResult.OwnerEmail(seed.Slug), Password)).AccessToken);
        var memberId = seed.MemberIds[0];
        var today = OrgClock.For("Europe/Berlin").Today();
        var lastMonth = BalanceHarness.MonthOf(today).AddMonths(-1);
        var calls = new (string Name, Func<Task<HttpResponseMessage>> Call)[]
        {
            ("balance", () => member.GetAsync($"/api/v1/organizations/{seed.Slug}/members/{memberId}/balance")),
            ("team overview", () => owner.GetAsync($"/api/v1/organizations/{seed.Slug}/team-overview")),
            ("payroll", () => owner.PostAsJsonAsync("/api/v1/ExportImport/payroll",
                new { orgSlug = seed.Slug, from = lastMonth.ToString("yyyy-MM-dd"), to = lastMonth.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd") }))
        };

        // Warm-up: the first calls store every member's checkpoints (and compile the queries)
        foreach (var (name, call) in calls)
            for (var i = 0; i < 2; i++)
                (await call()).IsSuccessStatusCode.Should().BeTrue(name);

        var result = new List<int>();
        foreach (var (name, call) in calls)
        {
            var runs = new List<int>();
            for (var i = 0; i < 2; i++)
            {
                using var scope = QueryCounter.Begin(capture: name == "balance");
                (await call()).EnsureSuccessStatusCode();
                runs.Add(scope.Count);
                if (name == "balance")
                {
                    // Entries of at most the current week/month and the month before (from the last checkpoint on)
                    var bound = DateTime.SpecifyKind(today.AddDays(-62).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
                    var instants = scope.Commands.Where(c => c.Sql.Contains("\"TimeEntries\""))
                        .SelectMany(c => c.Parameters).Select(p => p.Value).OfType<DateTime>()
                        .Where(v => v.Year >= 2000).ToList();
                    instants.Should().NotBeEmpty();
                    instants.Should().OnlyContain(v => v >= bound, "after warm-up the balance loads no history before the last checkpoint");
                }
            }
            runs.Distinct().Should().ContainSingle($"{name}: the same count on every warm call ({string.Join(", ", runs)})");
            output.WriteLine($"{seed.Slug} {name}: {runs[0]} SQL");
            result.Add(runs[0]);
        }
        return new Calls(result[0], result[1], result[2]);
    }

    /// <summary>A manual entry and a timer stop in the current month: one SQL command each, the hook decides without a query.</summary>
    private static async Task HookAddsNoQueryAsync(string superuser, PerfSeedResult seed)
    {
        var utcToday = DateOnly.FromDateTime(DateTime.UtcNow);
        var lastDay = BalanceHarness.MonthOf(utcToday).AddMonths(1).AddDays(-1);
        // Near a month change the hook may legitimately look up the org's zone (the change could touch an ended month)
        if (utcToday.Day <= 2 || utcToday >= lastDay.AddDays(-2)) return;

        var counter = new QueryCounter();
        await using var db = new TimeTrackingDbContext(new DbContextOptionsBuilder<TimeTrackingDbContext>().UseNpgsql(superuser).AddInterceptors(counter).Options);
        var userId = seed.MemberIds[0];
        var start = DateTime.UtcNow.AddHours(-2);
        var entry = new TimeEntry { UserId = userId, OrganizationId = seed.OrganizationId, StartTime = start, EndTime = start.AddHours(1), CreatedAt = start, UpdatedAt = start };
        db.TimeEntries.Add(entry);
        counter.Count = 0;
        await db.SaveChangesAsync();
        counter.Count.Should().Be(1, "a manual entry today: the INSERT only");

        var timer = new TimeEntry { UserId = userId, OrganizationId = seed.OrganizationId, StartTime = DateTime.UtcNow.AddMinutes(-30), IsRunning = true, CreatedAt = start, UpdatedAt = start };
        db.TimeEntries.Add(timer);
        await db.SaveChangesAsync();
        (timer.EndTime, timer.IsRunning) = (DateTime.UtcNow, false);
        counter.Count = 0;
        await db.SaveChangesAsync();
        counter.Count.Should().Be(1, "a timer stop today: the UPDATE only");
    }
}
