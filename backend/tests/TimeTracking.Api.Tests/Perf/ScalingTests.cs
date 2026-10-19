using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using TimeTracking.Api.Data;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;
using Xunit.Abstractions;

namespace TimeTracking.Api.Tests.Perf;

/// <summary>
/// Phase 0 of docs/PERFORMANCE_PLAN.md: how the interactive and export calls scale with years of history and
/// team size. Seeds one <see cref="PerfSeeder"/> organization per size, each in a fresh database of its own, then calls every endpoint over HTTP as
/// the app role (row-level security on) — warm-ups first, then timed calls with their SQL command count — and
/// writes <c>TestResults/perf/scaling-&lt;timestamp&gt;.md</c> (+ <c>.csv</c>) with p50/p95 per size, the growth
/// ratio t(10y)/t(1y) and <c>EXPLAIN (ANALYZE, BUFFERS)</c> of the slowest SQL of the 5 slowest calls at the
/// longest history. Records only (budgets come in Phase 3); fails only when a call errors.
/// Not part of the normal run: <c>dotnet test --filter Category=Perf</c>. Environment:
/// <c>PERF_SIZES</c> ("100x1,100x2,…", employees x years), <c>PERF_WARMUP</c> (3), <c>PERF_RUNS</c> (15); a call whose first
/// warm-up takes longer than <c>PERF_SLOW_MS</c> (5000) gets 1 warm-up and <c>PERF_SLOW_RUNS</c> (5) measured calls, so the
/// N+1 exports at 10 years do not take an hour.
/// </summary>
[Collection(PerfCollection.Name)]
[Trait("Category", "Perf")]
public class ScalingTests
{
    private const string DefaultSizes = "100x1,100x2,100x5,100x10,25x5,50x5,200x5";
    private const string Password = "Password123";

    private readonly PerfApiFactory _factory;
    private readonly ITestOutputHelper _output;

    public ScalingTests(PerfApiFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    private sealed record Size(int Employees, int Years)
    {
        public string Label => $"{Employees}x{Years}";
    }

    private enum Caller { Member, Admin, Owner }

    /// <summary>One measured call. <c>Before</c>/<c>After</c> run outside the timing and the SQL count (e.g. stop the timer a start began).</summary>
    private sealed record Endpoint(string Name, string Kind, Caller Caller, Func<Context, Task<HttpResponseMessage>> Call,
        Func<Context, Task>? Before = null, Func<Context, HttpResponseMessage, Task>? After = null);

    private sealed record Result(string Endpoint, string Kind, Size Size, double P50, double P95, int Sql, string? Error,
        IReadOnlyList<QueryCounter.Command> Commands, int CallerId, int Runs = 0);

    /// <summary>The seeded organization of one size, signed-in clients and the dates the calls ask for.</summary>
    private sealed class Context
    {
        public required PerfSeedResult Seed { get; init; }
        public required Dictionary<Caller, HttpClient> Clients { get; init; }
        public Dictionary<Caller, DateTime> SignedInAt { get; } = [];
        public string Slug => Seed.Slug;
        public int MemberId => Seed.MemberIds[0];
        public int CallerId(Caller c) => c switch { Caller.Member => MemberId, Caller.Admin => Seed.AdminIds[0], _ => Seed.OwnerId };
        public required DateOnly Today { get; init; }
        public required DateTime WeekFromUtc { get; init; }
        public required DateTime WeekToUtc { get; init; }
        public required DateOnly MonthFirst { get; init; }
        public DateOnly MonthLast => MonthFirst.AddMonths(1).AddDays(-1);
        public required DateTime ManualStartUtc { get; init; }
        public int? EntryId { get; set; }
    }

    private static int EnvInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v >= 0 ? v : fallback;

    private static List<Size> Sizes() =>
        (Environment.GetEnvironmentVariable("PERF_SIZES") is { Length: > 0 } s ? s : DefaultSizes)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => x.Split('x'))
        .Select(p => new Size(int.Parse(p[0], CultureInfo.InvariantCulture), int.Parse(p[1], CultureInfo.InvariantCulture)))
        .ToList();

    private static string I(FormattableString s) => FormattableString.Invariant(s);
    private static string Iso(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    private static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static List<Endpoint> Endpoints() =>
    [
        new("balance", "interactive", Caller.Member, c => Get(c, Caller.Member, $"organizations/{c.Slug}/members/{c.MemberId}/balance")),
        new("team-overview", "interactive", Caller.Admin, c => Get(c, Caller.Admin, $"organizations/{c.Slug}/team-overview")),
        new("inbox: failures", "interactive", Caller.Member, c => Get(c, Caller.Member, "TimeTracking/failures")),
        new("inbox: approvals", "interactive", Caller.Admin, c => Get(c, Caller.Admin, "organizations/notifications")),
        new("inbox: user-notifications", "interactive", Caller.Member, c => Get(c, Caller.Member, "organizations/user-notifications")),
        new("inbox: my-requests", "interactive", Caller.Member, c => Get(c, Caller.Member, "organizations/my-requests?limit=50")),
        new("inbox: notifications", "interactive", Caller.Member, c => Get(c, Caller.Member, "Notifications?unreadOnly=true&limit=50")),
        new("TimeTracking week", "interactive", Caller.Member,
            c => Get(c, Caller.Member, $"TimeTracking?from={Iso(c.WeekFromUtc)}&to={Iso(c.WeekToUtc)}&limit=200")),
        new("member entries week", "interactive", Caller.Admin,
            c => Get(c, Caller.Admin, $"organizations/{c.Slug}/member-entries/{c.MemberId}?from={Iso(c.WeekFromUtc)}&to={Iso(c.WeekToUtc)}&limit=200")),
        new("compliance month", "interactive", Caller.Member,
            c => Get(c, Caller.Member, $"organizations/{c.Slug}/members/{c.MemberId}/compliance?from={Day(c.MonthFirst)}&to={Day(c.MonthLast)}")),
        new("absences year", "interactive", Caller.Admin,
            c => Get(c, Caller.Admin, $"organizations/{c.Slug}/absences?from={c.Today.Year}-01-01&to={c.Today.Year}-12-31&limit=200")),
        new("org detail", "interactive", Caller.Admin, c => Get(c, Caller.Admin, $"Organizations/{c.Slug}")),
        new("accounting summary month", "export", Caller.Owner,
            c => Get(c, Caller.Owner, $"organizations/{c.Slug}/accounting/summary?year={c.MonthFirst.Year}&month={c.MonthFirst.Month}")),
        new("payroll CSV month", "export", Caller.Owner,
            c => Post(c, Caller.Owner, "ExportImport/payroll", new { orgSlug = c.Slug, from = Day(c.MonthFirst), to = Day(c.MonthLast) })),
        new("accounting vacation year", "export", Caller.Owner,
            c => Get(c, Caller.Owner, $"organizations/{c.Slug}/accounting/vacation?year={c.Today.Year}")),
        new("daily CSV month", "export", Caller.Owner,
            c => Post(c, Caller.Owner, "ExportImport/export",
                new { type = "daily", organizationSlug = c.Slug, targetUserId = c.MemberId, from = Day(c.MonthFirst), to = Day(c.MonthLast) })),
        new("monthly PDF", "export", Caller.Owner,
            c => Get(c, Caller.Owner, $"organizations/{c.Slug}/members/{c.MemberId}/reports/monthly?year={c.MonthFirst.Year}&month={c.MonthFirst.Month}")),
        new("timer start", "interactive", Caller.Member,
            c => Post(c, Caller.Member, "TimeTracking/start", new { organizationSlug = c.Slug }),
            After: async (c, _) => (await Post(c, Caller.Member, "TimeTracking/stop", new { })).EnsureSuccessStatusCode()),
        new("timer stop", "interactive", Caller.Member,
            c => Post(c, Caller.Member, "TimeTracking/stop", new { }),
            Before: async c => (await Post(c, Caller.Member, "TimeTracking/start", new { organizationSlug = c.Slug })).EnsureSuccessStatusCode()),
        new("manual entry create", "interactive", Caller.Member,
            c => Post(c, Caller.Member, "TimeTracking/manual", ManualEntry(c)),
            After: async (c, response) =>
            {
                var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("entry").GetProperty("id").GetInt32();
                (await c.Clients[Caller.Member].DeleteAsync($"/api/v1/TimeTracking/{id}")).EnsureSuccessStatusCode();
            }),
        new("manual entry delete", "interactive", Caller.Member,
            c => c.Clients[Caller.Member].DeleteAsync($"/api/v1/TimeTracking/{c.EntryId}"),
            Before: async c =>
            {
                var created = await Post(c, Caller.Member, "TimeTracking/manual", ManualEntry(c));
                created.EnsureSuccessStatusCode();
                c.EntryId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("entry").GetProperty("id").GetInt32();
            })
    ];

    private static object ManualEntry(Context c) => new
    {
        startTime = c.ManualStartUtc, endTime = c.ManualStartUtc.AddHours(1), organizationId = c.Seed.OrganizationId, description = "perf"
    };

    private static Task<HttpResponseMessage> Get(Context c, Caller caller, string path) => c.Clients[caller].GetAsync("/api/v1/" + path);

    private static Task<HttpResponseMessage> Post(Context c, Caller caller, string path, object body) =>
        c.Clients[caller].PostAsJsonAsync("/api/v1/" + path, body);

    [Fact]
    public async Task RecordScalingBaseline()
    {
        var sizes = Sizes();
        var top = TopSize(sizes);
        var (warmup, runs) = (EnvInt("PERF_WARMUP", 3), Math.Max(1, EnvInt("PERF_RUNS", 15)));
        var outDir = Path.Combine(TestProjectDir(), "TestResults", "perf");
        Directory.CreateDirectory(outDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var progress = Path.Combine(outDir, $"scaling-{stamp}.log");
        void Log(string line)
        {
            _output.WriteLine(line);
            File.AppendAllText(progress, $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}");
        }

        var results = new List<Result>();
        var seeds = new List<(Size Size, PerfSeedResult Seed)>();
        var explains = new List<(Result, List<(QueryCounter.Command, string)>)>();
        foreach (var size in sizes)
        {
            // Every size in a fresh database of its own (migrated by its own app instance), so the sizes compare like with like
            var database = $"perf_{size.Label}";
            var (app, superuser, appRole) = await _factory.ForDatabaseAsync(database);
            try
            {
                await using (var conn = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(superuser) { CommandTimeout = 600 }.ConnectionString))
                {
                    var seed = await PerfSeeder.SeedAsync(conn,
                        new PerfSeedOptions { Employees = size.Employees, Years = size.Years, Password = Password });
                    seeds.Add((size, seed));
                    Log(I($"seeded {size.Label} in {seed.Elapsed.TotalSeconds:0.0} s: {string.Join(", ", seed.Rows.Select(r => $"{r.Key} {r.Value}"))}"));
                }
                var ctx = await ContextAsync(app, seeds[^1].Seed);
                var measured = new List<Result>();
                foreach (var endpoint in Endpoints())
                {
                    var result = await MeasureAsync(ctx, endpoint, size, warmup, runs);
                    measured.Add(result);
                    Log(I($"{size.Label} {endpoint.Name}: p50 {result.P50:0.0} ms, p95 {result.P95:0.0} ms, {result.Sql} SQL, {result.Runs} runs{(result.Error != null ? " ERROR " + result.Error : "")}"));
                }
                results.AddRange(measured);
                if (size == top) explains = await ExplainSlowestAsync(measured, appRole);
            }
            finally
            {
                await app.DisposeAsync();
                await _factory.DropDatabaseAsync(database);
            }
        }

        var (md, csv) = (Path.Combine(outDir, $"scaling-{stamp}.md"), Path.Combine(outDir, $"scaling-{stamp}.csv"));
        await File.WriteAllTextAsync(md, Report(results, seeds, sizes, warmup, runs, explains));
        await File.WriteAllTextAsync(csv, Csv(results));
        Log(I($"report: {md}"));

        results.Where(r => r.Error != null).Select(r => $"{r.Size.Label} {r.Endpoint}: {r.Error}").Should().BeEmpty();
    }

    private static async Task<Context> ContextAsync(WebApplicationFactory<Program> app, PerfSeedResult seed)
    {
        var clock = OrgClock.For(OrgClock.DefaultTimeZone);
        var today = clock.Today();
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var lastSunday = today.AddDays(today.DayOfWeek == DayOfWeek.Sunday ? -7 : -(int)today.DayOfWeek);
        var ctx = new Context
        {
            Seed = seed,
            Clients = Enum.GetValues<Caller>().ToDictionary(c => c, _ =>
            {
                var client = app.CreateClient();
                client.Timeout = TimeSpan.FromMinutes(10);
                return client;
            }),
            Today = today,
            WeekFromUtc = clock.StartOfDayUtc(monday),
            WeekToUtc = clock.StartOfDayUtc(monday.AddDays(7)),
            MonthFirst = new DateOnly(today.Year, today.Month, 1).AddMonths(-1),
            ManualStartUtc = clock.ToUtc(lastSunday.ToDateTime(new TimeOnly(10, 0)))
        };
        foreach (var caller in Enum.GetValues<Caller>()) await SignInAsync(ctx, caller);
        return ctx;
    }

    /// <summary>Access tokens live 15 minutes in Testing: sign in again before one could expire mid-measurement.</summary>
    private static async Task SignInAsync(Context ctx, Caller caller)
    {
        if (ctx.SignedInAt.TryGetValue(caller, out var at) && DateTime.UtcNow - at < TimeSpan.FromMinutes(8)) return;
        var email = caller switch
        {
            Caller.Member => PerfSeedResult.MemberEmail(ctx.Slug, 1),
            Caller.Admin => PerfSeedResult.AdminEmail(ctx.Slug, 1),
            _ => PerfSeedResult.OwnerEmail(ctx.Slug)
        };
        await TestHelpers.AuthenticateAsync(ctx.Clients[caller], email, ctx.Seed.Password);
        ctx.SignedInAt[caller] = DateTime.UtcNow;
    }

    private static async Task<Result> MeasureAsync(Context ctx, Endpoint endpoint, Size size, int warmup, int runs)
    {
        await SignInAsync(ctx, endpoint.Caller);
        var slowMs = EnvInt("PERF_SLOW_MS", 5000);
        var slowRuns = Math.Max(1, EnvInt("PERF_SLOW_RUNS", 5));
        var times = new List<double>();
        var sql = new List<int>();
        IReadOnlyList<QueryCounter.Command> commands = [];
        for (var i = 0; i < warmup + runs; i++)
        {
            if (endpoint.Before != null) await endpoint.Before(ctx);
            var last = i == warmup + runs - 1;
            HttpResponseMessage response;
            var watch = Stopwatch.StartNew();
            using (var scope = QueryCounter.Begin(capture: last))
            {
                response = await endpoint.Call(ctx);
                await response.Content.LoadIntoBufferAsync();
                watch.Stop();
                if (i == 0 && watch.Elapsed.TotalMilliseconds > slowMs && warmup + runs > 1 + slowRuns)
                    (warmup, runs, last) = (1, Math.Min(runs, slowRuns), false);
                if (i >= warmup) sql.Add(scope.Count);
                if (last) commands = scope.Commands;
            }
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                return new Result(endpoint.Name, endpoint.Kind, size, 0, 0, 0,
                    $"{(int)response.StatusCode} {body[..Math.Min(body.Length, 300)]}", [], ctx.CallerId(endpoint.Caller));
            }
            if (endpoint.After != null) await endpoint.After(ctx, response);
            if (i >= warmup) times.Add(watch.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        sql.Sort();
        return new Result(endpoint.Name, endpoint.Kind, size, Percentile(times, 0.5), Percentile(times, 0.95), sql[sql.Count / 2], null,
            commands, ctx.CallerId(endpoint.Caller), times.Count);
    }

    /// <summary>Nearest-rank percentile of sorted values.</summary>
    private static double Percentile(List<double> sorted, double p) =>
        sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];

    // ── Report ──────────────────────────────────────────────────────────────

    /// <summary>The size whose slowest calls get an EXPLAIN: the longest history of the growth comparison.</summary>
    private static Size TopSize(List<Size> sizes) => GrowthSizes(sizes).Top ?? sizes.MaxBy(s => s.Years * 1000 + s.Employees)!;

    /// <summary>The sizes the growth ratio compares: the most-measured team size, shortest vs longest history.</summary>
    private static (Size? Base, Size? Top) GrowthSizes(List<Size> sizes)
    {
        var team = sizes.GroupBy(s => s.Employees).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First();
        var (low, high) = (team.MinBy(s => s.Years)!, team.MaxBy(s => s.Years)!);
        return low == high ? (null, null) : (low, high);
    }

    private static string Report(List<Result> results, List<(Size Size, PerfSeedResult Seed)> seeds, List<Size> sizes, int warmup, int runs,
        List<(Result Result, List<(QueryCounter.Command Command, string Plan)> Plans)> explains)
    {
        var (low, high) = GrowthSizes(sizes);
        var ratioHeader = low != null ? $"t({high!.Label})/t({low.Label})" : "growth";
        var sb = new StringBuilder();
        sb.AppendLine($"# Scaling report — {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"Sizes (employees x years): {string.Join(", ", sizes.Select(s => s.Label))}. Each call: {warmup} warm-ups, {runs} measured (calls slower than {EnvInt("PERF_SLOW_MS", 5000)} ms: 1 + {EnvInt("PERF_SLOW_RUNS", 5)}, marked *), " +
                      "over HTTP (TestServer, no network) as the app role with row-level security on, against PostgreSQL 16 in Docker (Testcontainers). " +
                      "Method: every size gets a fresh database of its own (created empty, migrated by its own app instance, seeded with PerfSeeder by binary COPY, " +
                      "then ANALYZEd), measured, and dropped — sizes never share tables, indexes or caches beyond the Postgres server itself; the background jobs " +
                      "(business-day-end/core hours loop, push dispatch, retention) are not running. " +
                      "Times in ms: p50 and p95 by nearest rank over n measured calls (with n < 20 the p95 is the maximum), SQL = commands EF sent for one call (median). Member = perf-user1 (full history), admin = perf-admin1, owner. " +
                      $"Month = the last ended month, week = the current one, year = {DateTime.Now.Year}. Growth = p50 ratio between the shortest and longest history at the same team size.");
        sb.AppendLine();
        sb.AppendLine("## Seeded data");
        sb.AppendLine();
        var tables = seeds.SelectMany(s => s.Seed.Rows.Keys).Distinct().ToList();
        sb.AppendLine($"| size | seconds | {string.Join(" | ", tables)} |");
        sb.AppendLine($"|---|---:|{string.Concat(tables.Select(_ => "---:|"))}");
        foreach (var (size, seed) in seeds)
            sb.AppendLine(I($"| {size.Label} | {seed.Elapsed.TotalSeconds:0.0} | {string.Join(" | ", tables.Select(t => seed.Rows.GetValueOrDefault(t).ToString("N0", CultureInfo.InvariantCulture)))} |"));
        sb.AppendLine();

        sb.AppendLine("## Summary (p50 ms · SQL commands)");
        sb.AppendLine();
        sb.AppendLine($"| endpoint | kind | {string.Join(" | ", sizes.Select(s => s.Label))} | {ratioHeader} |");
        sb.AppendLine($"|---|---|{string.Concat(sizes.Select(_ => "---:|"))}---:|");
        foreach (var group in results.GroupBy(r => r.Endpoint))
        {
            var cells = sizes.Select(s => group.FirstOrDefault(r => r.Size == s) is { } r
                ? r.Error != null ? "error" : I($"{r.P50:0.0} · {r.Sql}{(r.Runs < runs ? "*" : "")}") : "–");
            sb.AppendLine($"| {group.Key} | {group.First().Kind} | {string.Join(" | ", cells)} | {Ratio(group, low, high)} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Per endpoint");
        foreach (var group in results.GroupBy(r => r.Endpoint))
        {
            sb.AppendLine();
            sb.AppendLine($"### {group.Key} ({group.First().Kind})");
            sb.AppendLine();
            sb.AppendLine($"| | {string.Join(" | ", sizes.Select(s => s.Label))} |");
            sb.AppendLine($"|---|{string.Concat(sizes.Select(_ => "---:|"))}");
            string Row(string label, Func<Result, string> cell) =>
                $"| {label} | {string.Join(" | ", sizes.Select(s => group.FirstOrDefault(r => r.Size == s) is { } r ? r.Error != null ? "error" : cell(r) : "–"))} |";
            sb.AppendLine(Row("p50 ms", r => r.P50.ToString("0.0", CultureInfo.InvariantCulture)));
            sb.AppendLine(Row("p95 ms (nearest rank)", r => r.P95.ToString("0.0", CultureInfo.InvariantCulture)));
            sb.AppendLine(Row("n", r => r.Runs.ToString(CultureInfo.InvariantCulture)));
            sb.AppendLine(Row("SQL", r => r.Sql.ToString(CultureInfo.InvariantCulture)));
            sb.AppendLine();
            sb.AppendLine($"Growth {ratioHeader}: {Ratio(group, low, high)}");
            foreach (var error in group.Where(r => r.Error != null)) sb.AppendLine($"\n{error.Size.Label}: `{error.Error}`");
        }

        if (explains.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"## EXPLAIN (ANALYZE, BUFFERS) — the 5 slowest calls at {explains[0].Result.Size.Label}");
            sb.AppendLine();
            sb.AppendLine("The slowest SELECTs (up to 3) of the last measured call, re-run as the app role for the same user (RLS on).");
            foreach (var (result, plans) in explains)
            {
                sb.AppendLine();
                sb.AppendLine(I($"### {result.Endpoint} — p50 {result.P50:0.0} ms, {result.Sql} SQL"));
                foreach (var (command, plan) in plans)
                {
                    sb.AppendLine();
                    sb.AppendLine(I($"{command.Duration.TotalMilliseconds:0.0} ms in the call:"));
                    sb.AppendLine();
                    sb.AppendLine("```sql");
                    sb.AppendLine(command.Sql.Trim());
                    sb.AppendLine("```");
                    sb.AppendLine();
                    sb.AppendLine("```text");
                    sb.AppendLine(plan.TrimEnd());
                    sb.AppendLine("```");
                }
            }
        }
        return sb.ToString();
    }

    private static string Ratio(IEnumerable<Result> group, Size? low, Size? high)
    {
        if (low == null) return "–";
        var (a, b) = (group.FirstOrDefault(r => r.Size == low), group.FirstOrDefault(r => r.Size == high));
        return a is { Error: null, P50: > 0 } && b is { Error: null }
            ? I($"{b.P50 / a.P50:0.0}x (SQL {a.Sql} → {b.Sql})")
            : "–";
    }

    private static string Csv(List<Result> results)
    {
        var sb = new StringBuilder("endpoint,kind,size,employees,years,n,p50_ms,p95_ms,sql,error\n");
        foreach (var r in results)
            sb.AppendLine(string.Join(',', $"\"{r.Endpoint}\"", r.Kind, r.Size.Label, r.Size.Employees, r.Size.Years, r.Runs,
                r.P50.ToString("0.0", CultureInfo.InvariantCulture), r.P95.ToString("0.0", CultureInfo.InvariantCulture), r.Sql,
                r.Error == null ? "" : $"\"{r.Error.Replace("\"", "'").ReplaceLineEndings(" ")}\""));
        return sb.ToString();
    }

    /// <summary>
    /// For the 5 slowest calls at the longest history: EXPLAIN (ANALYZE, BUFFERS) of their slowest SELECTs, as the
    /// app role for the calling user so the RLS policies are part of the plan. Writes are never re-run.
    /// </summary>
    private static async Task<List<(Result, List<(QueryCounter.Command, string)>)>> ExplainSlowestAsync(List<Result> results, string appConnectionString)
    {
        var explains = new List<(Result, List<(QueryCounter.Command, string)>)>();
        foreach (var result in results.Where(r => r.Error == null).OrderByDescending(r => r.P50).Take(5))
        {
            var plans = new List<(QueryCounter.Command, string)>();
            var selects = result.Commands
                .Where(c => c.Sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                            || c.Sql.TrimStart().StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.Duration).Take(3);
            foreach (var command in selects)
            {
                await using var conn = new NpgsqlConnection(appConnectionString);
                await conn.OpenAsync();
                await using (var stamp = new NpgsqlCommand("SELECT set_config('app.user_id', @u, false), set_config('app.bypass_rls', 'off', false)", conn))
                {
                    stamp.Parameters.AddWithValue("u", result.CallerId.ToString(CultureInfo.InvariantCulture));
                    await stamp.ExecuteNonQueryAsync();
                }
                await using var explain = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS) " + command.Sql, conn);
                foreach (var p in command.Parameters) explain.Parameters.Add(p.Clone());
                var plan = new StringBuilder();
                try
                {
                    await using var reader = await explain.ExecuteReaderAsync();
                    while (await reader.ReadAsync()) plan.AppendLine(reader.GetString(0));
                }
                catch (PostgresException ex)
                {
                    plan.AppendLine($"EXPLAIN failed: {ex.MessageText}");
                }
                plans.Add((command, plan.ToString()));
            }
            explains.Add((result, plans));
        }
        return explains;
    }

    private static string TestProjectDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TimeTracking.Api.Tests.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
