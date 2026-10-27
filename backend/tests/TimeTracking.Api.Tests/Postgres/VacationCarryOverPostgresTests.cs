using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;
using Xunit.Abstractions;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// Vacation carry-over on real Postgres (migration <c>VacationCarryOver</c>): row-level security on VacationYears, the
/// migration's defaults for organizations that existed before, and a query count for the org detail and the
/// accounting vacation overview that depends neither on the number of members nor on the length of the chain.
/// </summary>
[Collection(PostgresCollection.Name)]
public class VacationCarryOverPostgresTests
{
    private readonly PostgresApiFactory _factory;
    private readonly ITestOutputHelper _output;
    private static readonly int Cy = OrgClock.For((string?)null).Today().Year;

    public VacationCarryOverPostgresTests(PostgresApiFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    private string SuperuserDb => new NpgsqlConnectionStringBuilder(_factory.SuperuserConnectionString) { Database = "timetracking" }.ConnectionString;

    private TimeTrackingDbContext Db(QueryCounter? counter = null)
    {
        var options = new DbContextOptionsBuilder<TimeTrackingDbContext>().UseNpgsql(SuperuserDb);
        if (counter != null) options.AddInterceptors(counter);
        return new TimeTrackingDbContext(options.Options);
    }

    private async Task<NpgsqlConnection> OpenAsAsync(int userId)
    {
        var conn = new NpgsqlConnection(_factory.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.user_id', @u, false), set_config('app.bypass_rls', 'off', false)";
        cmd.Parameters.AddWithValue("u", userId.ToString());
        await cmd.ExecuteNonQueryAsync();
        return conn;
    }

    private static async Task<long> CountAsync(NpgsqlConnection conn, int orgId)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM \"VacationYears\" WHERE \"OrganizationId\" = @org";
        cmd.Parameters.AddWithValue("org", orgId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task VacationYears_AreInvisibleAndUnwritable_ForOtherOrganizations()
    {
        var a = await TestHelpers.OrgWithMemberAsync(_factory, "pg-vy-a", new { }, "pg.test");
        var b = await TestHelpers.OrgWithMemberAsync(_factory, "pg-vy-b", new { }, "pg.test");
        foreach (var (owner, slug, member) in new[] { (a.Owner, a.Slug, a.MemberId), (b.Owner, b.Slug, b.MemberId) })
            (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/members/{member}/vacation/{Cy}", new { adjustment = 2, note = "rls" }))
                .EnsureSuccessStatusCode();

        // The member of A sees their org's row, none of B's
        await using (var asA = await OpenAsAsync(a.MemberId))
        {
            (await CountAsync(asA, a.OrgId)).Should().Be(1);
            (await CountAsync(asA, b.OrgId)).Should().Be(0);

            await using var insert = asA.CreateCommand();
            insert.CommandText = """
                INSERT INTO "VacationYears" ("OrganizationId", "UserId", "Year", "Entitlement", "Adjustment", "UpdatedAt")
                VALUES (@org, @user, @year, 99, 99, now())
                """;
            insert.Parameters.AddWithValue("org", b.OrgId);
            insert.Parameters.AddWithValue("user", b.MemberId);
            insert.Parameters.AddWithValue("year", Cy + 5);
            var act = async () => await insert.ExecuteNonQueryAsync();
            (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        }

        // The API reads the row through RLS as the member
        var mine = await a.Member.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{a.Slug}/members/{a.MemberId}/vacation?year={Cy}");
        mine.GetProperty("adjustment").GetDouble().Should().Be(2);
        mine.GetProperty("adjustmentNote").GetString().Should().Be("rls");

        // Leaving the organization revokes access to its rows
        (await a.Owner.DeleteAsync($"/api/v1/Organizations/{a.Slug}/members/{a.MemberId}")).EnsureSuccessStatusCode();
        await using var formerMember = await OpenAsAsync(a.MemberId);
        // Own rows stay visible to their owner (like AbsenceDays: owner OR active member)
        (await CountAsync(formerMember, a.OrgId)).Should().Be(1);
    }

    [Fact]
    public async Task Migration_GivesExistingOrganizationsUntil31MarchFrom2026_NewOrgsTheirOwnYear()
    {
        _ = _factory.Server;
        await using var conn = new NpgsqlConnection(SuperuserDb);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT column_name, column_default, is_nullable FROM information_schema.columns
            WHERE table_name = 'Organizations' AND column_name LIKE 'VacationCarryOver%'
            """;
        var columns = new Dictionary<string, (string? Default, string Nullable)>();
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                columns[reader.GetString(0)] = (reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2));

        columns["VacationCarryOverMode"].Default.Should().Be("1");   // UntilDate
        columns["VacationCarryOverExpiryMonth"].Default.Should().Be("3");
        columns["VacationCarryOverExpiryDay"].Default.Should().Be("31");
        columns["VacationCarryOverSince"].Default.Should().Be("2026");
        columns["VacationCarryOverMaxDays"].Should().Be((null, "YES"));

        // A new organization: its creation year (org-local), same rule
        var (owner, _, slug, _, _) = await TestHelpers.OrgWithMemberAsync(_factory, "pg-vy-new", new { }, "pg.test");
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}");
        detail.GetProperty("vacationCarryOverMode").GetString().Should().Be("UntilDate");
        detail.GetProperty("vacationCarryOverExpiry").GetString().Should().Be("03-31");
        detail.GetProperty("vacationCarryOverSince").GetInt32().Should().Be(Cy);
        detail.GetProperty("vacationCarryOverMaxDays").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>An org with <paramref name="members"/> members, each with vacation and a VacationYear row in every year since <paramref name="since"/>.</summary>
    private async Task<(string Slug, int OwnerId)> SeedAsync(string slug, int members, int since)
    {
        _ = _factory.Server;
        await using var db = Db();
        var org = new Organization { Name = slug, Slug = slug, VacationCarryOverSince = since, VacationCarryOverMaxDays = 10 };
        var owner = new User { Email = $"{slug}-owner@pg.test", FirstName = "O", LastName = "Owner" };
        db.AddRange(org, owner);
        db.UserOrganizations.Add(new UserOrganization { User = owner, Organization = org, Role = OrganizationRole.Owner, JoinedAt = new DateTime(since, 1, 2, 0, 0, 0, DateTimeKind.Utc) });
        db.Holidays.Add(new Holiday { Organization = org, Name = "Neujahr", Date = new DateOnly(since, 1, 1), IsRecurring = true });
        for (var i = 0; i < members; i++)
        {
            var user = new User { Email = $"{slug}-{i}@pg.test", FirstName = $"M{i}", LastName = "Test" };
            db.UserOrganizations.Add(new UserOrganization
            {
                User = user, Organization = org, VacationDaysPerYear = 30, JoinedAt = new DateTime(since, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            });
            for (var y = since; y <= Cy; y++)
            {
                db.VacationYears.Add(new VacationYear { Organization = org, User = user, Year = y, Adjustment = i % 3, UpdatedAt = DateTime.UtcNow });
                for (var d = 0; d < 8; d++)
                    db.AbsenceDays.Add(new AbsenceDay { Organization = org, User = user, Date = new DateOnly(y, 3, 2).AddDays(d * 7), Type = AbsenceType.Vacation });
            }
        }
        await db.SaveChangesAsync();
        return (slug, owner.Id);
    }

    private async Task<(int Detail, int Overview, int Years)> CountQueriesAsync(string slug, int ownerId)
    {
        var counter = new QueryCounter();
        await using var db = Db(counter);
        var orgs = new OrganizationService(db, NullLogger<OrganizationService>.Instance);
        var accounting = new AccountingService(db, new PayrollExportService(db, NullLogger<PayrollExportService>.Instance),
            NullLogger<AccountingService>.Instance);

        (await orgs.GetOrganizationAsync(slug, ownerId)).Data.Should().NotBeNull();   // warm-up (query compilation)
        counter.Count = 0;
        var detail = (await orgs.GetOrganizationAsync(slug, ownerId)).Data!;
        var detailCount = counter.Count;
        detail.Members.Where(m => m.Id != ownerId).Should().OnlyContain(m => m.VacationEntitlement == 30);

        (await accounting.GetVacationOverviewAsync(slug, ownerId, Cy)).Data.Should().NotBeNull();
        counter.Count = 0;
        var overview = (await accounting.GetVacationOverviewAsync(slug, ownerId, Cy)).Data!;
        var overviewCount = counter.Count;
        overview.Members.Should().HaveCount(detail.Members.Count);

        var org = await db.Organizations.AsNoTracking().SingleAsync(o => o.Slug == slug);
        var ids = detail.Members.Select(m => m.Id).ToList();
        await VacationBalances.YearsAsync(db, org, ids, Cy, OrgClock.For(org).Today());
        counter.Count = 0;
        await VacationBalances.YearsAsync(db, org, ids, Cy, OrgClock.For(org).Today());
        var yearsCount = counter.Count;

        _output.WriteLine($"{slug}: detail {detailCount}, accounting overview {overviewCount}, YearsAsync {yearsCount} queries");
        return (detailCount, overviewCount, yearsCount);
    }

    [Fact]
    public async Task QueryCount_IndependentOfMembersAndChainLength()
    {
        var small = await SeedAsync("pg-vy-q5", 5, Cy);
        var large = await SeedAsync("pg-vy-q30", 30, Cy);
        var longChain = await SeedAsync("pg-vy-q30l", 30, Cy - 5);

        var s = await CountQueriesAsync(small.Slug, small.OwnerId);
        var l = await CountQueriesAsync(large.Slug, large.OwnerId);
        var c = await CountQueriesAsync(longChain.Slug, longChain.OwnerId);

        l.Should().Be(s, "30 members cost as many queries as 5");
        c.Should().Be(l, "a 6-year chain costs as many queries as one year");
        s.Years.Should().BeLessThanOrEqualTo(6, "YearsAsync: memberships, holidays, schedules, frozen days, vacation days, rows");
    }
}
