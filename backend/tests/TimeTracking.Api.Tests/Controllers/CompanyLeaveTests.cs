using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Betriebsurlaub (CompanyLeaves): generated vacation days on the members' working days, for the organization or a group,
/// with exclusions; a sick day replaces one; the budget may go negative; the rows follow every change of the definition.
/// </summary>
public class CompanyLeaveTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public CompanyLeaveTests(TimeTrackingApiFactory factory) => _factory = factory;

    private static readonly int Year = DateTime.UtcNow.Year + 1;   // a future year: no closed or ended month in the way

    private async Task<(HttpClient Owner, HttpClient Member, string Slug, int MemberId)> OrgAsync(string prefix)
    {
        var (owner, member, slug, _, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, prefix,
            new { holidayRegion = "DE" });
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedules",
            new { validFrom = $"{Year - 1}-01-01", weeklyWorkHours = 40, distributeEvenly = true })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/members/{memberId}/vacation-days", new { days = 2 }))
            .EnsureSuccessStatusCode();
        return (owner, member, slug, memberId);
    }

    private static async Task<List<JsonElement>> AbsencesAsync(HttpClient client, string slug, int userId, string from, string to) =>
        (await client.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/absences?userId={userId}&from={from}&to={to}&limit=200"))
            .GetProperty("items").EnumerateArray().ToList();

    private static int Weekdays(DateOnly from, DateOnly to) =>
        Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays)
            .Count(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));

    private static List<string> Dates(IEnumerable<JsonElement> absences) =>
        absences.Select(a => a.GetProperty("date").GetString()!).Order().ToList();

    private static async Task<int> CreateAsync(HttpClient owner, string slug, string from, string to, int? groupId = null)
    {
        var res = await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/company-leaves", new { name = "Betriebsurlaub", from, to, groupId });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task OrgWideLeave_WritesVacationOnWorkingDays_SkippingWeekendsAndHolidays_EvenBeyondTheBudget()
    {
        var (owner, member, slug, memberId) = await OrgAsync("cl-org");
        // 24.12.–31.12.: the weekend and both Christmas days are no vacation days
        var id = await CreateAsync(owner, slug, $"{Year}-12-24", $"{Year}-12-31");

        var rows = await AbsencesAsync(owner, slug, memberId, $"{Year}-12-01", $"{Year}-12-31");
        var expected = Enumerable.Range(24, 8).Select(d => new DateOnly(Year, 12, d))
            .Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && d.Day is not (25 or 26))
            .Select(d => d.ToString("yyyy-MM-dd")).ToList();
        Dates(rows).Should().Equal(expected);
        rows.Should().OnlyContain(a => a.GetProperty("type").GetString() == "Vacation" && a.GetProperty("note").GetString() == "Betriebsurlaub");

        // Budget of 2 days: the remaining days go negative
        var vacation = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}/members/{memberId}/vacation?year={Year}");
        (vacation.GetProperty("available").GetDouble() - vacation.GetProperty("taken").GetDouble() - vacation.GetProperty("planned").GetDouble())
            .Should().BeLessThan(0);

        // The member cannot delete such a day; an admin leaves them out instead
        var one = rows[0].GetProperty("id").GetInt32();
        var delete = await member.DeleteAsync($"/api/v1/organizations/{slug}/absences/{one}");
        delete.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await delete.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("companyLeave.managed");

        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/company-leaves/{id}/exclusions", new { userIds = new[] { memberId } }))
            .EnsureSuccessStatusCode();
        (await AbsencesAsync(owner, slug, memberId, $"{Year}-12-01", $"{Year}-12-31")).Should().BeEmpty();
        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/company-leaves/{id}/exclusions", new { userIds = Array.Empty<int>() }))
            .EnsureSuccessStatusCode();
        Dates(await AbsencesAsync(owner, slug, memberId, $"{Year}-12-01", $"{Year}-12-31")).Should().Equal(expected);

        // Shortened, the days outside go; deleted, all go
        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/company-leaves/{id}", new { to = $"{Year}-12-28" })).EnsureSuccessStatusCode();
        Dates(await AbsencesAsync(owner, slug, memberId, $"{Year}-12-01", $"{Year}-12-31")).Should().Equal(expected.Where(d => string.CompareOrdinal(d, $"{Year}-12-28") <= 0));
        (await owner.DeleteAsync($"/api/v1/organizations/{slug}/company-leaves/{id}")).EnsureSuccessStatusCode();
        (await AbsencesAsync(owner, slug, memberId, $"{Year}-12-01", $"{Year}-12-31")).Should().BeEmpty();
    }

    [Fact]
    public async Task SickDay_ReplacesALeaveDay_AndTheLeaveDayComesBackWhenItIsRemoved()
    {
        var (owner, member, slug, memberId) = await OrgAsync("cl-sick");
        var monday = new DateOnly(Year, 8, 1);
        while (monday.DayOfWeek != DayOfWeek.Monday) monday = monday.AddDays(1);
        await CreateAsync(owner, slug, monday.ToString("yyyy-MM-dd"), monday.AddDays(4).ToString("yyyy-MM-dd"));

        var tuesday = monday.AddDays(1).ToString("yyyy-MM-dd");
        var sick = await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences", new { date = tuesday, type = 0 });
        sick.EnsureSuccessStatusCode();
        var rows = await AbsencesAsync(owner, slug, memberId, monday.ToString("yyyy-MM-dd"), monday.AddDays(4).ToString("yyyy-MM-dd"));
        rows.Should().HaveCount(5);
        rows.Single(a => a.GetProperty("date").GetString() == tuesday).GetProperty("type").GetString().Should().Be("SickDay");

        var sickId = (await sick.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await member.DeleteAsync($"/api/v1/organizations/{slug}/absences/{sickId}")).EnsureSuccessStatusCode();
        (await AbsencesAsync(owner, slug, memberId, tuesday, tuesday)).Single().GetProperty("type").GetString().Should().Be("Vacation");
    }

    [Fact]
    public async Task GroupLeave_OnlyForTheGroup_AndANewMemberGetsTheFutureDays()
    {
        var (owner, _, slug, memberId) = await OrgAsync("cl-group");
        var (newcomer, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "cl-group-new@t.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, login)).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{login.User.Id}/work-schedules",
            new { validFrom = $"{Year - 1}-01-01", weeklyWorkHours = 40, distributeEvenly = true })).EnsureSuccessStatusCode();
        var group = await TestHelpers.CreateGroupAsync(owner, slug, "Werkstatt", new { }, (memberId, "Member"));
        await CreateAsync(owner, slug, $"{Year}-08-03", $"{Year}-08-14", group);

        (await AbsencesAsync(owner, slug, memberId, $"{Year}-08-01", $"{Year}-08-31")).Should().HaveCount(Weekdays(new DateOnly(Year, 8, 3), new DateOnly(Year, 8, 14)));
        (await AbsencesAsync(owner, slug, login.User.Id, $"{Year}-08-01", $"{Year}-08-31")).Should().BeEmpty();

        // Joining the group: the (future) days are written for them too
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{group}/members", new { userId = login.User.Id, role = "Member" }))
            .EnsureSuccessStatusCode();
        (await AbsencesAsync(owner, slug, login.User.Id, $"{Year}-08-01", $"{Year}-08-31")).Should().HaveCount(Weekdays(new DateOnly(Year, 8, 3), new DateOnly(Year, 8, 14)));
        // Leaving it removes them again
        (await owner.DeleteAsync($"/api/v1/Organizations/{slug}/groups/{group}/members/{login.User.Id}")).EnsureSuccessStatusCode();
        (await AbsencesAsync(owner, slug, login.User.Id, $"{Year}-08-01", $"{Year}-08-31")).Should().BeEmpty();
        _ = newcomer;
    }

    [Fact]
    public async Task ExistingAbsence_Wins_AndTheRangeIsLimited()
    {
        var (owner, member, slug, memberId) = await OrgAsync("cl-existing");
        var day = new DateOnly(Year, 9, 1);
        while (day.DayOfWeek != DayOfWeek.Wednesday) day = day.AddDays(1);
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences", new { date = day.ToString("yyyy-MM-dd"), type = 0 }))
            .EnsureSuccessStatusCode();
        await CreateAsync(owner, slug, day.ToString("yyyy-MM-dd"), day.ToString("yyyy-MM-dd"));
        (await AbsencesAsync(owner, slug, memberId, day.ToString("yyyy-MM-dd"), day.ToString("yyyy-MM-dd")))
            .Single().GetProperty("type").GetString().Should().Be("SickDay");

        var tooLong = await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/company-leaves",
            new { name = "x", from = $"{Year}-01-01", to = $"{Year}-06-30" });
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tooLong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("companyLeave.range");
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/company-leaves",
            new { name = "x", from = $"{Year}-01-01", to = $"{Year}-01-02" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/company-leaves")).EnumerateArray()
            .Should().OnlyContain(l => l.GetProperty("excludedUserIds").ValueKind == JsonValueKind.Null);
    }
}
