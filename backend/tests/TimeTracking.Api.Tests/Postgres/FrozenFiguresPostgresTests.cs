using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TimeTracking.Api.Data;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// Frozen figures of closed months (issue #81) on real Postgres under row-level security: closing writes the rows through the
/// raw-SQL unnest insert in the request's transaction, an admin's correction re-freezes, reopening deletes them, an accountant
/// can close, and the rows are invisible to another organization.
/// </summary>
[Collection(PostgresCollection.Name)]
public class FrozenFiguresPostgresTests
{
    private readonly PostgresApiFactory _factory;

    public FrozenFiguresPostgresTests(PostgresApiFactory factory) => _factory = factory;

    private static readonly DateOnly LastMonth = ClosedPeriods.MonthOf(OrgClock.For((string?)null).Today()).AddMonths(-1);
    private static readonly DateOnly LastDay = LastMonth.AddMonths(1).AddDays(-1);

    private static List<DateOnly> Weekdays =>
        Enumerable.Range(0, LastDay.Day).Select(LastMonth.AddDays)
            .Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();

    private static string Key(DateOnly d) => d.ToString("yyyy-MM-dd");
    private static DateTime Utc(DateOnly d, int hour) => d.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Utc);

    private TimeTrackingDbContext Superuser() => new(new DbContextOptionsBuilder<TimeTrackingDbContext>().UseNpgsql(
        new NpgsqlConnectionStringBuilder(_factory.SuperuserConnectionString) { Database = "timetracking" }.ConnectionString).Options);

    /// <summary>Org with a member on 8 h Mon–Fri who worked 4 h on the 2nd weekday of last month; returns the entry.</summary>
    private async Task<(HttpClient Owner, HttpClient Member, string Slug, int OrgId, int MemberId, int EntryId)> SetupAsync(
        string prefix, bool adminsSeeHours = true)
    {
        var (owner, member, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, prefix,
            new { editPastEntriesMode = 2, memberTimeEntryVisibility = adminsSeeHours }, "pg.test");
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedules",
            new { validFrom = Key(LastMonth.AddMonths(-1)), weeklyWorkHours = 40.0, distributeEvenly = true })).EnsureSuccessStatusCode();
        var worked = Weekdays[1];
        var manual = await member.PostAsJsonAsync("/api/v1/TimeTracking/manual",
            new { startTime = Utc(worked, 6), endTime = Utc(worked, 10), organizationId = orgId });
        manual.StatusCode.Should().Be(HttpStatusCode.Created, await manual.Content.ReadAsStringAsync());
        var entryId = (await manual.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("entry").GetProperty("id").GetInt32();
        return (owner, member, slug, orgId, memberId, entryId);
    }

    private static async Task CloseAsync(HttpClient caller, string slug)
    {
        var closed = await caller.PostAsJsonAsync($"/api/v1/organizations/{slug}/closed-periods", new { from = LastMonth, to = LastMonth });
        closed.StatusCode.Should().Be(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> SummaryRowAsync(HttpClient caller, string slug, int memberId) =>
        (await caller.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/accounting/summary?year={LastMonth.Year}&month={LastMonth.Month}"))
        .GetProperty("members").EnumerateArray().Single(m => m.GetProperty("userId").GetInt32() == memberId);

    [Fact]
    public async Task Close_ReFreeze_Reopen_UnderRls_AHolidayAddedLaterChangesNoFigure()
    {
        var (owner, _, slug, orgId, memberId, entryId) = await SetupAsync("pg-frozen");
        var missing = Weekdays[3];
        await CloseAsync(owner, slug);

        await using (var db = Superuser())
        {
            (await db.FrozenDays.CountAsync(d => d.OrganizationId == orgId && d.UserId == memberId)).Should().Be(LastDay.Day);
            (await db.FrozenMonths.Where(m => m.OrganizationId == orgId && m.Month == LastMonth).Select(m => m.UserId).ToListAsync())
                .Should().Contain(memberId).And.HaveCount(2, "the owner is an active member too");
            (await db.ClosedPeriods.SingleAsync(p => p.OrganizationId == orgId)).FrozenAt.Should().NotBeNull();
        }
        var before = (await SummaryRowAsync(owner, slug, memberId)).GetRawText();

        // A holiday in the closed month changes nothing of it
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/holidays", new { date = Key(missing), name = "Später" })).EnsureSuccessStatusCode();
        (await SummaryRowAsync(owner, slug, memberId)).GetRawText().Should().Be(before);

        // An admin's correction with a reason re-freezes the member's month in the same save
        var correction = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/organizations/{slug}/members/{memberId}/entries/{entryId}")
        {
            Content = JsonContent.Create(new { endTime = Utc(Weekdays[1], 12) })
        };
        correction.Headers.Add("X-Change-Reason", Uri.EscapeDataString("Nachtrag"));
        (await owner.SendAsync(correction)).EnsureSuccessStatusCode();
        await using (var db = Superuser())
        {
            (await db.FrozenDays.SingleAsync(d => d.OrganizationId == orgId && d.UserId == memberId && d.Date == Weekdays[1]))
                .NetWorkedHours.Should().Be(6);
            (await db.FrozenDays.CountAsync(d => d.OrganizationId == orgId && d.UserId == memberId)).Should().Be(LastDay.Day);
        }
        (await SummaryRowAsync(owner, slug, memberId)).GetProperty("workedHours").GetDouble()
            .Should().BeApproximately(JsonDocument.Parse(before).RootElement.GetProperty("workedHours").GetDouble() + 2, 0.001);

        // Reopening deletes the rows: live again, the holiday now counts
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/closed-periods/reopen", new { month = LastMonth, reason = "Korrektur" }))
            .EnsureSuccessStatusCode();
        await using (var db = Superuser())
        {
            (await db.FrozenDays.CountAsync(d => d.OrganizationId == orgId)).Should().Be(0);
            (await db.FrozenMonths.CountAsync(m => m.OrganizationId == orgId)).Should().Be(0);
        }
        (await SummaryRowAsync(owner, slug, memberId)).GetProperty("targetHours").GetDouble()
            .Should().BeApproximately(JsonDocument.Parse(before).RootElement.GetProperty("targetHours").GetDouble() - 8, 0.001);
    }

    [Fact]
    public async Task Accountant_ClosesAMonth_RowsAreWritten_AndInvisibleToAnotherOrganization()
    {
        var (owner, member, slug, orgId, memberId, _) = await SetupAsync("pg-frozen-acct", adminsSeeHours: false);
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/members/{memberId}/accountant", new { isAccountant = true })).EnsureSuccessStatusCode();
        await CloseAsync(member, slug);

        await using (var db = Superuser())
            (await db.FrozenMonths.CountAsync(m => m.OrganizationId == orgId && m.UserId == memberId && m.Month == LastMonth)).Should().Be(1);
        var frozen = await member.GetFromJsonAsync<List<JsonElement>>(
            $"/api/v1/organizations/{slug}/members/{memberId}/frozen-days?from={Key(LastMonth)}&to={Key(LastDay)}");
        frozen.Should().HaveCount(LastDay.Day);

        // As the app role: the member sees the rows, a user of another organization none, even without a filter
        var (strangerClient, stranger) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-frozen-x@pg.test");
        await TestHelpers.CreateOrganizationAsync(strangerClient, "pg-frozen-x Org", "pg-frozen-x-org");
        foreach (var table in new[] { "FrozenDays", "FrozenMonths" })
        {
            (await CountAsync(memberId, $"""SELECT count(*) FROM "{table}" WHERE "OrganizationId" = {orgId}""")).Should().BeGreaterThan(0);
            (await CountAsync(stranger.User.Id, $"""SELECT count(*) FROM "{table}" WHERE "OrganizationId" = {orgId}""")).Should().Be(0);
            (await CountAsync(null, $"""SELECT count(*) FROM "{table}" """)).Should().Be(0);
        }
    }

    /// <summary>A count as the app role with the given RLS user (null: anonymous).</summary>
    private async Task<long> CountAsync(int? userId, string sql)
    {
        await using var conn = new NpgsqlConnection(_factory.AppConnectionString);
        await conn.OpenAsync();
        await using (var set = conn.CreateCommand())
        {
            set.CommandText = "SELECT set_config('app.user_id', @u, false), set_config('app.bypass_rls', 'off', false)";
            set.Parameters.AddWithValue("u", userId?.ToString() ?? "");
            await set.ExecuteNonQueryAsync();
        }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
