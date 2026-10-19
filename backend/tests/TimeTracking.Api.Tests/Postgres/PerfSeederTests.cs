using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using TimeTracking.Api.Data;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// The history generator of the scaling tests (docs/PERFORMANCE_PLAN.md, Phase 0) on a small size: what it
/// writes keeps the app's invariants, and the app serves it. The <c>perf-seed</c> command runs as the
/// non-superuser app role, so the RLS bypass of the COPY is exercised too. Every org seeded here is dropped
/// again, so no core-hours organization stays behind for the other tests' background checks.
/// </summary>
[Collection(PostgresCollection.Name)]
public class PerfSeederTests
{
    private const string Password = "Perf-Test-Password1";
    private readonly PostgresApiFactory _factory;

    public PerfSeederTests(PostgresApiFactory factory) => _factory = factory;

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "TimeTracking.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = _factory.AppConnectionString })
        .Build();

    private string Superuser => new NpgsqlConnectionStringBuilder(_factory.SuperuserConnectionString) { Database = "timetracking" }.ConnectionString;

    private async Task<long> ScalarAsync(string sql, string slug)
    {
        await using var conn = new NpgsqlConnection(Superuser);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("slug", slug);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private const string OrgOf = """(SELECT "Id" FROM "Organizations" WHERE "Slug" = @slug)""";

    private Task<int> RunAsync(params string[] args) => PerfSeedCommand.RunAsync(["perf-seed", .. args], Config(), new Env("Testing"));

    [Fact]
    public async Task SeedCommand_WritesAConsistentHistory_TheAppServes_DropRemovesIt()
    {
        _ = _factory.Server;   // migrations + demo seed
        const string slug = "perf-pg-seed";
        string[] seed = ["--employees", "8", "--years", "1", "--seed", "7", "--org-slug", slug, "--password", Password];
        // A database holding a real (non-perf, non-demo) organization: refused without --force
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "perf-guard-owner@pg.test");
        await TestHelpers.CreateOrganizationAsync(owner, "Real Org", "pg-perf-guard-org");
        (await RunAsync(seed)).Should().Be(2);
        (await ScalarAsync("""SELECT count(*) FROM "Organizations" WHERE "Slug" = @slug""", slug)).Should().Be(0);
        try
        {
            (await RunAsync([.. seed, "--force"])).Should().Be(0);

            (await ScalarAsync($"""SELECT count(*) FROM "TimeEntries" WHERE "OrganizationId" = {OrgOf}""", slug)).Should().BeGreaterThan(8 * 150);
            (await ScalarAsync($"""SELECT count(*) FROM "AbsenceDays" WHERE "OrganizationId" = {OrgOf}""", slug)).Should().BeGreaterThan(8 * 20);
            (await ScalarAsync($"""SELECT count(*) FROM "TerminalPunches" WHERE "OrganizationId" = {OrgOf}""", slug)).Should().BeGreaterThan(0);
            (await ScalarAsync($"""SELECT count(*) FROM "OrgRequests" WHERE "OrganizationId" = {OrgOf}""", slug)).Should().BeGreaterThanOrEqualTo(8 * 12);
            (await ScalarAsync($"""SELECT count(*) FROM "TimeEntries" WHERE "OrganizationId" = {OrgOf} AND "IsRunning" """, slug)).Should().Be(2);

            // Core hours: one check per member and calendar day from joining to yesterday (the running job may add today), like the background job writes them;
            // every violation notified to the member and the admins
            (await ScalarAsync($"""
                SELECT count(*) FROM (
                  SELECT c."UserId", count(*) AS n, min(c."Date") AS first, max(c."Date") AS last, min(uo."JoinedAt") AS joined
                  FROM "CoreHoursChecks" c JOIN "UserOrganizations" uo ON uo."UserId" = c."UserId" AND uo."OrganizationId" = c."OrganizationId"
                  WHERE c."OrganizationId" = {OrgOf} GROUP BY c."UserId") x
                WHERE n <> last - first + 1 OR last < (now() AT TIME ZONE 'Europe/Berlin')::date - 1
                   OR first <> (joined AT TIME ZONE 'Europe/Berlin')::date
                """, slug)).Should().Be(0);
            (await ScalarAsync($"""SELECT count(DISTINCT "UserId") FROM "CoreHoursChecks" WHERE "OrganizationId" = {OrgOf}""", slug)).Should().Be(8);
            (await ScalarAsync($"""
                SELECT count(*) FROM "CoreHoursChecks" c WHERE c."OrganizationId" = {OrgOf} AND c."Issues" <> 0
                  AND (c."NotifiedAt" IS NULL OR NOT EXISTS (SELECT 1 FROM "Notifications" n WHERE n."UserId" = c."UserId"
                       AND n."Type" = 'CoreHoursViolation' AND n."RelatedEntityId" = c."UserId" AND n."CreatedAt" = c."CheckedAt"))
                """, slug)).Should().Be(0);
            (await ScalarAsync($"""
                SELECT count(*) FROM "Notifications" n WHERE n."OrganizationId" = {OrgOf} AND n."Type" = 'RequestAccepted'
                  AND NOT EXISTS (SELECT 1 FROM "OrgRequests" r WHERE r."Id" = n."RelatedEntityId" AND r."Status" = 1)
                """, slug)).Should().Be(0);

            // Every entry ≤ 24 h and finished ones in the past; no two entries of a user overlap
            (await ScalarAsync($"""
                SELECT count(*) FROM "TimeEntries" WHERE "OrganizationId" = {OrgOf}
                  AND ("EndTime" - "StartTime" > interval '24 hours' OR "EndTime" > now() OR "EndTime" <= "StartTime"
                       OR ("EndTime" IS NULL) <> "IsRunning")
                """, slug)).Should().Be(0);
            (await ScalarAsync($"""
                SELECT count(*) FROM "TimeEntries" a JOIN "TimeEntries" b
                  ON a."UserId" = b."UserId" AND a."Id" < b."Id"
                 AND a."StartTime" < COALESCE(b."EndTime", 'infinity') AND b."StartTime" < COALESCE(a."EndTime", 'infinity')
                WHERE a."OrganizationId" = {OrgOf}
                """, slug)).Should().Be(0);
            // Ids handed to COPY came from the sequences: the history points at real entries
            (await ScalarAsync("""SELECT count(*) FROM "TimeEntryChanges" c LEFT JOIN "TimeEntries" e ON e."Id" = c."TimeEntryId" WHERE e."Id" IS NULL""", slug))
                .Should().Be(0);

            // The app serves it under RLS
            var member = _factory.CreateClient();
            await TestHelpers.AuthenticateAsync(member, PerfSeedResult.MemberEmail(slug, 1), Password);
            var me = await member.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/Auth/me");
            var memberId = me.GetProperty("id").GetInt32();
            (await member.GetAsync($"/api/v1/organizations/{slug}/members/{memberId}/balance")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await member.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationSlug = slug })).StatusCode.Should().Be(HttpStatusCode.Created);
            (await member.PostAsJsonAsync("/api/v1/TimeTracking/stop", new { })).StatusCode.Should().Be(HttpStatusCode.OK);

            // Never twice into the same slug
            (await RunAsync("--employees", "3", "--years", "1", "--org-slug", slug, "--force")).Should().Be(1);
        }
        finally
        {
            (await RunAsync("--drop", "--org-slug", slug)).Should().Be(0);
        }

        // Gone with everything that pointed at it or at its users
        (await ScalarAsync("""SELECT count(*) FROM "Organizations" WHERE "Slug" = @slug""", slug)).Should().Be(0);
        (await ScalarAsync("""SELECT count(*) FROM "Users" WHERE "Email" LIKE '%@' || @slug || '.test'""", slug)).Should().Be(0);
        (await ScalarAsync("""SELECT count(*) FROM "TimeEntries" e LEFT JOIN "Users" u ON u."Id" = e."UserId" WHERE u."Id" IS NULL""", slug)).Should().Be(0);
        (await RunAsync("--drop", "--org-slug", slug)).Should().Be(1);
    }

    [Fact]
    public async Task Seed_CoreHoursOff_RandomPassword()
    {
        _ = _factory.Server;
        const string slug = "perf-pg-nocore";
        PerfSeedResult result;
        await using (var conn = new NpgsqlConnection(Superuser))
            result = await PerfSeeder.SeedAsync(conn, new PerfSeedOptions { Employees = 4, Years = 1, OrgSlug = slug, CoreHours = false });
        try
        {
            result.Password.Should().HaveLength(16);
            (await ScalarAsync($"""SELECT count(*) FROM "CoreHoursChecks" WHERE "OrganizationId" = {OrgOf}""", slug)).Should().Be(0);
            (await ScalarAsync("""SELECT count(*) FROM "Organizations" WHERE "Slug" = @slug AND "CoreHoursEnabled" """, slug)).Should().Be(0);
            var owner = _factory.CreateClient();
            (await owner.PostAsJsonAsync("/api/v1/Auth/login", new { email = PerfSeedResult.OwnerEmail(slug), password = result.Password }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await using var conn = new NpgsqlConnection(Superuser);
            (await PerfSeeder.DropAsync(conn, slug)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task SeedCommand_RefusesProductionWithoutForce_AndBadArguments()
    {
        (await PerfSeedCommand.RunAsync(["perf-seed", "--employees", "5"], Config(), new Env("Production"))).Should().Be(2);
        (await RunAsync("--employees", "two")).Should().Be(2);
        (await RunAsync("--years")).Should().Be(2);
        (await RunAsync("--core-hours", "maybe")).Should().Be(2);
        (await RunAsync("--drop", "--org-slug", "mueller-software")).Should().Be(2);
        (await RunAsync("--drop")).Should().Be(2);
        (await PerfSeedCommand.RunAsync(["perf-seed"], new ConfigurationBuilder().Build(), new Env("Testing"))).Should().Be(2);
        await FluentActions.Awaiting(async () =>
        {
            await using var conn = new NpgsqlConnection(Superuser);
            await PerfSeeder.DropAsync(conn, "mueller-software");
        }).Should().ThrowAsync<ArgumentException>();

        var (command, error) = PerfSeedCommand.Parse(
            ["perf-seed", "--employees", "25", "--years", "5", "--seed", "3", "--end-date", "2026-06-30", "--password", "secret-pw", "--core-hours", "off", "--force"]);
        error.Should().BeNull();
        command!.Force.Should().BeTrue();
        command.Drop.Should().BeFalse();
        command.Options.Slug.Should().Be("perf-25-5");
        command.Options.EndDate.Should().Be(new DateOnly(2026, 6, 30));
        command.Options.Password.Should().Be("secret-pw");
        command.Options.CoreHours.Should().BeFalse();
        PerfSeedCommand.Parse(["--org-slug", "Not A Slug"]).Error.Should().NotBeNull();
        PerfSeedCommand.Parse(["--drop", "--org-slug", "perf-1-1"]).Command!.Drop.Should().BeTrue();
    }

    [Fact]
    public void GermanHolidays_EasterBasedDaysPerYear()
    {
        PerfSeeder.EasterSunday(2026).Should().Be(new DateOnly(2026, 4, 5));
        PerfSeeder.EasterSunday(2008).Should().Be(new DateOnly(2008, 3, 23));
        var holidays = PerfSeeder.GermanHolidays(2008, 2008);
        holidays.Should().Contain(h => h.Name == "Karfreitag" && h.Date == new DateOnly(2008, 3, 21));
        // 2008: Ascension Day fell on May 1st, which is already a (recurring) holiday
        holidays.Count(h => h.Date.Month == 5 && h.Date.Day == 1).Should().Be(1);
    }
}
