using System.Diagnostics;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TimeTracking.Api.Data;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;
using Xunit.Abstractions;

namespace TimeTracking.Api.Tests.Perf;

/// <summary>
/// Issue #81: closing 24 months for 200 members (frozen figures written in one transaction under RLS), and what reading a
/// closed month costs compared with computing it. Not part of the normal run: <c>dotnet test --filter Category=Perf</c>.
/// <c>PERF_FREEZE_SIZE</c> ("200x3", employees x years) overrides the size. Prints the timings; fails only above a minute.
/// </summary>
[Collection(PerfCollection.Name)]
[Trait("Category", "Perf")]
public class FreezePerfTests(PerfApiFactory factory, ITestOutputHelper output)
{
    private const string Password = "Freeze-Password1";

    [Fact]
    public async Task Close24MonthsOf200Members_TakesSeconds_ReadingAClosedMonthIsCheaper()
    {
        var size = (Environment.GetEnvironmentVariable("PERF_FREEZE_SIZE") ?? "200x3").Split('x').Select(int.Parse).ToArray();
        const string database = "freeze_perf";
        var (app, superuser, _) = await factory.ForDatabaseAsync(database);
        try
        {
            PerfSeedResult seed;
            await using (var conn = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(superuser) { CommandTimeout = 1200 }.ConnectionString))
                seed = await PerfSeeder.SeedAsync(conn, new PerfSeedOptions
                {
                    Employees = size[0], Years = size[1], Seed = 7, OrgSlug = "freeze", Password = Password
                });
            var owner = app.CreateClient();
            owner.Timeout = TimeSpan.FromMinutes(10);
            TestHelpers.SetBearerToken(owner, (await TestHelpers.LoginAsync(owner, PerfSeedResult.OwnerEmail(seed.Slug), Password)).AccessToken);

            var lastMonth = BalanceHarness.MonthOf(OrgClock.For("Europe/Berlin").Today()).AddMonths(-1);
            var first = lastMonth.AddMonths(-23);
            async Task<(double Ms, int Sql)> TimeAsync(Func<Task<HttpResponseMessage>> call)
            {
                using var scope = QueryCounter.Begin();
                var watch = Stopwatch.StartNew();
                var response = await call();
                watch.Stop();
                response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
                return (watch.Elapsed.TotalMilliseconds, scope.Count);
            }
            Task<HttpResponseMessage> Summary() =>
                owner.GetAsync($"/api/v1/organizations/{seed.Slug}/accounting/summary?year={lastMonth.Year}&month={lastMonth.Month}");
            Task<HttpResponseMessage> Pdf() =>
                owner.GetAsync($"/api/v1/organizations/{seed.Slug}/members/{seed.MemberIds[0]}/reports/monthly?year={lastMonth.Year}&month={lastMonth.Month}");

            // Warm (checkpoints stored, queries compiled), as in production after the pre-warm
            await TimeAsync(() => owner.GetAsync($"/api/v1/organizations/{seed.Slug}/team-overview"));
            for (var i = 0; i < 3; i++) { await TimeAsync(Summary); await TimeAsync(Pdf); }
            var openSummary = await TimeAsync(Summary);
            var openPdf = await TimeAsync(Pdf);

            var close = await TimeAsync(() => owner.PostAsJsonAsync($"/api/v1/organizations/{seed.Slug}/closed-periods",
                new { from = first, to = lastMonth, reason = "perf" }));
            for (var i = 0; i < 3; i++) { await TimeAsync(Summary); await TimeAsync(Pdf); }
            var closedSummary = await TimeAsync(Summary);
            var closedPdf = await TimeAsync(Pdf);
            var reopen = await TimeAsync(() => owner.PostAsJsonAsync($"/api/v1/organizations/{seed.Slug}/closed-periods/reopen",
                new { month = lastMonth, reason = "perf" }));

            await using var check = new TimeTrackingDbContext(new DbContextOptionsBuilder<TimeTrackingDbContext>().UseNpgsql(superuser).Options);
            var days = await check.FrozenDays.CountAsync();
            var months = await check.FrozenMonths.CountAsync();

            output.WriteLine($"{size[0]} members x {size[1]} years");
            output.WriteLine($"close 24 months: {close.Ms:F0} ms, {close.Sql} SQL, {months} frozen months, {days} frozen days (after reopening one)");
            output.WriteLine($"reopen 1 month: {reopen.Ms:F0} ms, {reopen.Sql} SQL");
            output.WriteLine($"accounting summary of last month: open {openSummary.Ms:F1} ms / {openSummary.Sql} SQL, closed {closedSummary.Ms:F1} ms / {closedSummary.Sql} SQL");
            output.WriteLine($"monthly PDF of last month: open {openPdf.Ms:F1} ms / {openPdf.Sql} SQL, closed {closedPdf.Ms:F1} ms / {closedPdf.Sql} SQL");
            close.Ms.Should().BeLessThan(60_000);
            closedSummary.Sql.Should().BeLessThanOrEqualTo(openSummary.Sql);
        }
        finally
        {
            await app.DisposeAsync();
            await factory.DropDatabaseAsync(database);
        }
    }
}
