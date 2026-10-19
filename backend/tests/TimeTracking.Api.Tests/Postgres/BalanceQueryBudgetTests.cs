using System.Net.Http.Json;
using System.Text.Json;
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
/// Query budgets (docs/PERFORMANCE_PLAN.md "Correctness net" and Phase 3 "PerfSmoke"): with the checkpoints stored, every
/// history-sensitive endpoint of the scaling report — balance, team overview, payroll, the inbox calls, the accounting
/// summary and vacation overview, the daily CSV, the monthly PDF, the org detail and the everyday writes (timer start/stop,
/// manual entry) — sends a fixed number of SQL commands: the same for one and for three years of history, and at most a
/// ceiling per endpoint. The balance reads entries of at most about a month; the invalidation hook adds no query to an
/// everyday save in the current month. Runs on the perf container (<see cref="PerfApiFactory"/>: no background jobs, SQL
/// counted per request) with two <see cref="PerfSeeder"/> organizations of 10 people; part of the normal suite (Category
/// PerfSmoke, not Perf), so it stays small: two warm-up and two counted calls per endpoint.
/// </summary>
[Collection(PerfCollection.Name)]
[Trait("Category", "PerfSmoke")]
public class BalanceQueryBudgetTests(PerfApiFactory factory, ITestOutputHelper output)
{
    private const string Password = "Budget-Password1";

    /// <summary>
    /// Ceilings per endpoint: whole requests (auth filter, role/visibility checks included), measured 2026-10-19 on
    /// 10 people × 1 and 3 years. Lower them when a request gets cheaper; raising one needs a reason (a new query that does
    /// not grow with the history).
    /// </summary>
    private static readonly Dictionary<string, int> Ceilings = new()
    {
        ["balance"] = 18,
        ["team overview"] = 10,
        ["payroll"] = 11,
        ["inbox: failures"] = 1,
        ["inbox: approvals"] = 6,
        ["accounting summary"] = 11,
        ["accounting vacation"] = 9, // 2 more since the vacation carry-over (Phase 2b)
        ["daily CSV"] = 18,
        ["monthly PDF"] = 11,
        ["org detail"] = 10, // 2 more since the vacation carry-over (Phase 2b)
        ["timer start"] = 7,
        ["timer stop"] = 7,
        ["manual entry"] = 13
    };

    private static readonly string[] Writes = ["timer start", "timer stop", "manual entry"];

    /// <summary>One counted call; <c>Before</c>/<c>After</c> run outside the count (start the timer a stop needs, delete what a call created).</summary>
    private sealed record Call(string Name, Func<Task<HttpResponseMessage>> Run,
        Func<Task>? Before = null, Func<HttpResponseMessage, Task>? After = null);

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

            var oneYear = await MeasureAsync(app, seeds[0]);
            var threeYears = await MeasureAsync(app, seeds[1]);
            foreach (var (name, count) in oneYear)
                output.WriteLine($"{name}: 1 year {count} SQL, 3 years {threeYears[name]} SQL (ceiling {Ceilings[name]})");
            threeYears.Should().Equal(oneYear, "with checkpoints the cost of no endpoint grows with the years of history");

            // Near a month change a write may legitimately look up the org's zone (it could touch an ended month): the writes
            // keep the 1-vs-3-years equality but skip their ceilings then
            var nearMonthChange = NearMonthChange();
            foreach (var (name, count) in oneYear)
                if (!nearMonthChange || !Writes.Contains(name))
                    count.Should().BeLessThanOrEqualTo(Ceilings[name], $"{name}: the query budget");

            await HookAddsNoQueryAsync(superuser, seeds[0]);
        }
        finally
        {
            await app.DisposeAsync();
            await factory.DropDatabaseAsync(database);
        }
    }

    private static bool NearMonthChange()
    {
        var utcToday = DateOnly.FromDateTime(DateTime.UtcNow);
        var lastDay = BalanceHarness.MonthOf(utcToday).AddMonths(1).AddDays(-1);
        return utcToday.Day <= 2 || utcToday >= lastDay.AddDays(-2);
    }

    private async Task<Dictionary<string, int>> MeasureAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app, PerfSeedResult seed)
    {
        var member = app.CreateClient();
        TestHelpers.SetBearerToken(member, (await TestHelpers.LoginAsync(member, PerfSeedResult.MemberEmail(seed.Slug, 1), Password)).AccessToken);
        var owner = app.CreateClient();
        TestHelpers.SetBearerToken(owner, (await TestHelpers.LoginAsync(owner, PerfSeedResult.OwnerEmail(seed.Slug), Password)).AccessToken);
        var memberId = seed.MemberIds[0];
        var slug = seed.Slug;
        var today = OrgClock.For("Europe/Berlin").Today();
        var lastMonth = BalanceHarness.MonthOf(today).AddMonths(-1);
        var (from, to) = (lastMonth.ToString("yyyy-MM-dd"), lastMonth.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd"));

        // perf-user1 has no running timer and nothing today (the seeded history ends yesterday): the manual entry two hours
        // ago never overlaps, and every write is stopped/deleted again outside the count
        Task<HttpResponseMessage> Start() => member.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationSlug = slug });
        Task<HttpResponseMessage> Stop() => member.PostAsJsonAsync("/api/v1/TimeTracking/stop", new { });
        async Task DeleteEntry(HttpResponseMessage created)
        {
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("entry").GetProperty("id").GetInt32();
            (await member.DeleteAsync($"/api/v1/TimeTracking/{id}")).EnsureSuccessStatusCode();
        }

        var calls = new Call[]
        {
            new("balance", () => member.GetAsync($"/api/v1/organizations/{slug}/members/{memberId}/balance")),
            new("team overview", () => owner.GetAsync($"/api/v1/organizations/{slug}/team-overview")),
            new("payroll", () => owner.PostAsJsonAsync("/api/v1/ExportImport/payroll", new { orgSlug = slug, from, to })),
            new("inbox: failures", () => member.GetAsync("/api/v1/TimeTracking/failures")),
            new("inbox: approvals", () => owner.GetAsync("/api/v1/organizations/notifications")),
            new("accounting summary", () => owner.GetAsync($"/api/v1/organizations/{slug}/accounting/summary?year={lastMonth.Year}&month={lastMonth.Month}")),
            new("accounting vacation", () => owner.GetAsync($"/api/v1/organizations/{slug}/accounting/vacation?year={today.Year}")),
            new("daily CSV", () => owner.PostAsJsonAsync("/api/v1/ExportImport/export",
                new { type = "daily", organizationSlug = slug, targetUserId = memberId, from, to })),
            new("monthly PDF", () => owner.GetAsync($"/api/v1/organizations/{slug}/members/{memberId}/reports/monthly?year={lastMonth.Year}&month={lastMonth.Month}")),
            new("org detail", () => owner.GetAsync($"/api/v1/Organizations/{slug}")),
            new("timer start", Start, After: async _ => (await Stop()).EnsureSuccessStatusCode()),
            new("timer stop", Stop, Before: async () => (await Start()).EnsureSuccessStatusCode()),
            new("manual entry", () =>
            {
                var start = DateTime.UtcNow.AddHours(-2);
                return member.PostAsJsonAsync("/api/v1/TimeTracking/manual",
                    new { startTime = start, endTime = start.AddHours(1), organizationId = seed.OrganizationId, description = "budget" });
            }, After: DeleteEntry)
        };

        // Warm-up: the first calls store every member's checkpoints (and compile the queries)
        foreach (var call in calls)
            for (var i = 0; i < 2; i++)
            {
                if (call.Before != null) await call.Before();
                var response = await call.Run();
                response.IsSuccessStatusCode.Should().BeTrue($"{call.Name}: {await response.Content.ReadAsStringAsync()}");
                if (call.After != null) await call.After(response);
            }

        var result = new Dictionary<string, int>();
        foreach (var call in calls)
        {
            var runs = new List<int>();
            for (var i = 0; i < 2; i++)
            {
                if (call.Before != null) await call.Before();
                HttpResponseMessage response;
                using (var scope = QueryCounter.Begin(capture: call.Name == "balance"))
                {
                    response = await call.Run();
                    response.EnsureSuccessStatusCode();
                    runs.Add(scope.Count);
                    if (call.Name == "balance")
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
                if (call.After != null) await call.After(response);
            }
            runs.Distinct().Should().ContainSingle($"{call.Name}: the same count on every warm call ({string.Join(", ", runs)})");
            output.WriteLine($"{slug} {call.Name}: {runs[0]} SQL");
            result[call.Name] = runs[0];
        }
        return result;
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
