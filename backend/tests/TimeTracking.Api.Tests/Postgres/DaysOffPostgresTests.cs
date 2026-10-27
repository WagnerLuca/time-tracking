using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// Days off on real Postgres under row-level security: the migration's indexes (one org-wide holiday per day, one per
/// group; two half-day absences per day), Betriebsurlaub rows written for other members inside an admin's request and
/// by a group change, and a member's calendar reading group holidays by membership history.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DaysOffPostgresTests
{
    private readonly PostgresApiFactory _factory;

    public DaysOffPostgresTests(PostgresApiFactory factory) => _factory = factory;

    private static readonly int Year = DateTime.UtcNow.Year + 1;
    /// <summary>The first Monday of August next year (a Monday–Friday leave = 5 working days).</summary>
    private static readonly DateOnly Monday = Enumerable.Range(1, 7).Select(d => new DateOnly(Year, 8, d)).First(d => d.DayOfWeek == DayOfWeek.Monday);

    [Fact]
    public async Task GroupHolidaysAndLeaves_WriteAndReadUnderRls()
    {
        var (owner, member, slug, _, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "pg-daysoff",
            new { holidayRegion = "DE-BW" }, "pg.test");
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedules",
            new { validFrom = $"{Year - 1}-01-01", weeklyWorkHours = 40, distributeEvenly = true })).EnsureSuccessStatusCode();
        var group = await TestHelpers.CreateGroupAsync(owner, slug, "Lager", new { holidayRegion = "DE-BE" });

        // One org-wide holiday per day (NULLs not distinct), one per group and day
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/holidays", new { date = $"{Year}-12-31", name = "Silvester", isHalfDay = true }))
            .EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/holidays", new { date = $"{Year}-12-31", name = "Lager zu", groupId = group }))
            .EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/holidays", new { date = $"{Year}-12-31", name = "Doppelt" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // The admin's request writes the member's vacation rows (RLS: rows of another member of the organization)
        var leave = await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/company-leaves",
            new { name = "Inventur", from = Monday.ToString("yyyy-MM-dd"), to = Monday.AddDays(4).ToString("yyyy-MM-dd"), groupId = group });
        leave.EnsureSuccessStatusCode();
        (await member.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/absences?from={Year}-08-01&to={Year}-08-31"))
            .GetProperty("items").GetArrayLength().Should().Be(0);

        // Joining the group: membership period, Berlin region, the group's holiday and the leave's days
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{group}/members", new { userId = memberId, role = "Member" }))
            .EnsureSuccessStatusCode();
        (await member.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/absences?from={Year}-08-01&to={Year}-08-31"))
            .GetProperty("items").GetArrayLength().Should().Be(5);
        var days = await member.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/holidays/days?from={Year}-12-31&to={Year}-12-31");
        days.GetProperty("region").GetString().Should().Be("DE-BE");
        var silvester = days.GetProperty("days").EnumerateArray().Single();
        silvester.GetProperty("isHalfDay").GetBoolean().Should().BeFalse();  // the group's own day wins over the org's half day
        silvester.GetProperty("name").GetString().Should().Be("Lager zu");

        // A sick half and a vacation half on one day (unique per member, day and half)
        var day = new DateOnly(Year, 9, 1);
        while (day.DayOfWeek != DayOfWeek.Thursday) day = day.AddDays(1);
        foreach (var type in new[] { 1, 0 })
            (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences", new { date = day.ToString("yyyy-MM-dd"), type, isHalfDay = true }))
                .EnsureSuccessStatusCode();
    }
}
