using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// The accounting role on real Postgres (migration <c>AccountantRole</c> applied) under row-level security:
/// an accountant who is a plain member sees every member's hours and absences of their organization —
/// including a member who has left — while an outsider accountant of another org sees nothing of it.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AccountingPostgresTests
{
    private readonly PostgresApiFactory _factory;

    public AccountingPostgresTests(PostgresApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Accountant_SeesAllRowsOfTheOrg_UnderRls_IncludingFormerMembers()
    {
        var (owner, member, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "pg-acct",
            new { editPastEntriesMode = 2, memberTimeEntryVisibility = false }, "pg.test");
        var (accountant, accLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-acct-b@pg.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, accLogin)).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/members/{accLogin.User.Id}/accountant", new { isAccountant = true }))
            .EnsureSuccessStatusCode();

        var lastMonth = ClosedPeriods.MonthOf(OrgClock.For((string?)null).Today()).AddMonths(-1);
        var day = lastMonth.AddDays(9);
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) day = day.AddDays(1);
        var start = day.ToDateTime(new TimeOnly(6, 0), DateTimeKind.Utc);
        (await member.PostAsJsonAsync("/api/v1/TimeTracking/manual", new { startTime = start, endTime = start.AddHours(4), organizationId = orgId }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences/admin",
            new { userId = memberId, date = day.AddDays(1).ToString("yyyy-MM-dd"), type = 0 })).EnsureSuccessStatusCode();
        (await owner.DeleteAsync($"/api/v1/Organizations/{slug}/members/{memberId}")).EnsureSuccessStatusCode();

        var summary = await accountant.GetFromJsonAsync<JsonElement>(
            $"/api/v1/organizations/{slug}/accounting/summary?year={lastMonth.Year}&month={lastMonth.Month}");
        var row = summary.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("userId").GetInt32() == memberId);
        row.GetProperty("isFormerMember").GetBoolean().Should().BeTrue();
        row.GetProperty("workedHours").GetDouble().Should().Be(4);
        row.GetProperty("sickDays").GetDouble().Should().Be(1);

        // The flag is per organization: an accountant elsewhere gets nothing here
        var (outsider, outsiderLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-acct-x@pg.test");
        var other = await TestHelpers.CreateOrganizationAsync(outsider, "pg-acct other", "pg-acct-other");
        (await outsider.PutAsJsonAsync($"/api/v1/Organizations/{other.Slug}/members/{outsiderLogin.User.Id}/accountant", new { isAccountant = true }))
            .EnsureSuccessStatusCode();
        (await outsider.GetAsync($"/api/v1/organizations/{slug}/accounting/summary?year={lastMonth.Year}&month={lastMonth.Month}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
