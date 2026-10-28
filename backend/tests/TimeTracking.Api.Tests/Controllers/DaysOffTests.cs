using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimeTracking.Api.Data;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Holidays per member (docs/DAYS_OFF_DESIGN.md, HolidayCalendar): the public holidays of the region valid that day
/// (work schedule, groups, organization), company holidays of the organization or a group by membership history, and
/// exclusions.
/// </summary>
public class DaysOffTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public DaysOffTests(TimeTrackingApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Owner, HttpClient Member, string Slug, int OwnerId, int MemberId)> OrgAsync(string prefix, string? region = "DE-BY")
    {
        var (owner, member, slug, _, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, prefix, new { holidayRegion = region ?? "" });
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}")).GetProperty("members")
            .EnumerateArray().Single(m => m.GetProperty("id").GetInt32() != memberId).GetProperty("id").GetInt32();
        foreach (var id in new[] { ownerId, memberId })
            (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{id}/work-schedule/change",
                new { validFrom = "2025-01-01", weeklyWorkHours = 40, distributeEvenly = true })).EnsureSuccessStatusCode();
        return (owner, member, slug, ownerId, memberId);
    }

    private static async Task<JsonElement> DaysAsync(HttpClient client, string slug, string from, string to, int? userId = null) =>
        await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/organizations/{slug}/holidays/days?from={from}&to={to}" + (userId is int id ? $"&userId={id}" : ""));

    private static List<string> Dates(JsonElement days) =>
        days.GetProperty("days").EnumerateArray().Select(d => d.GetProperty("date").GetString()!).ToList();

    private static async Task<string> DailyCsvRowAsync(HttpClient client, string slug, string date, int? userId = null)
    {
        var csv = await (await client.PostAsJsonAsync("/api/v1/ExportImport/export",
            new { lang = "en", type = "daily", organizationSlug = slug, from = date, to = date, targetUserId = userId })).Content.ReadAsStringAsync();
        return csv.Split('\n').Single(l => l.StartsWith(date));
    }

    [Fact]
    public async Task OrgRegion_GivesPublicHolidays_WithoutAnyRows()
    {
        var (owner, _, slug, _, _) = await OrgAsync("do-region");
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/holidays")).GetArrayLength().Should().Be(0);

        var days = await DaysAsync(owner, slug, "2026-06-01", "2026-06-30");
        days.GetProperty("region").GetString().Should().Be("DE-BY");
        days.GetProperty("days").EnumerateArray().Should().ContainSingle(d =>
            d.GetProperty("date").GetString() == "2026-06-04" && d.GetProperty("name").GetString() == "Fronleichnam"
            && d.GetProperty("source").GetString() == "Public");
        // Every report counts it: Fronleichnam is a holiday with no target
        (await DailyCsvRowAsync(owner, slug, "2026-06-04")).Should().Contain("Fronleichnam").And.Contain("Thu,0.00,");
    }

    [Fact]
    public async Task ScheduleRegion_IsTheMembersArbeitsort_FromItsStart()
    {
        var (owner, member, slug, _, memberId) = await OrgAsync("do-schedule");
        // From July the member works in Berlin: a new contract period with its own region
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedule/change",
            new { validFrom = "2026-07-01", weeklyWorkHours = 40, distributeEvenly = true, holidayRegion = "de-be" })).EnsureSuccessStatusCode();

        var dates = Dates(await DaysAsync(member, slug, "2026-01-01", "2026-12-31"));
        dates.Should().Contain("2026-06-04");     // Fronleichnam (Bavaria, first half)
        dates.Should().NotContain("2026-11-01");  // Allerheiligen would be Bavarian
        dates.Should().Contain("2026-01-06");     // Heilige Drei Könige, still Bavaria
        var berlin = await DaysAsync(member, slug, "2027-03-08", "2027-03-08");
        Dates(berlin).Should().Equal("2027-03-08");  // Frauentag in Berlin
        (await DaysAsync(owner, slug, "2026-11-01", "2026-11-01", memberId)).GetProperty("region").GetString().Should().Be("DE-BE");
    }

    [Fact]
    public async Task MemberOwnSchedule_NeverMovesTheirRegion()
    {
        var (owner, member, slug, _, memberId) = await OrgAsync("do-self");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { workScheduleChangeMode = 2 })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedule/change",
            new { validFrom = "2026-02-01", weeklyWorkHours = 40, distributeEvenly = true, holidayRegion = "DE-BE" })).EnsureSuccessStatusCode();

        // The member's own new period (from tomorrow) ignores a region and keeps the previous period's
        var own = await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/work-schedule/change",
            new { validFrom = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-dd"), weeklyWorkHours = 30, distributeEvenly = true, holidayRegion = "DE-BY" });
        own.EnsureSuccessStatusCode();
        (await own.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("future").GetProperty("holidayRegion").GetString().Should().Be("DE-BE");
    }

    [Fact]
    public async Task GroupRegion_AppliesWhileAMemberBelongs_AndNeverRewritesThePast()
    {
        var (owner, member, slug, _, memberId) = await OrgAsync("do-group", region: "DE-BY");
        var berlin = await TestHelpers.CreateGroupAsync(owner, slug, "Team Berlin", new { holidayRegion = "DE-BE" }, (memberId, "Member"));

        (await DaysAsync(member, slug, "2027-03-08", "2027-03-08")).GetProperty("region").GetString().Should().Be("DE-BE");
        Dates(await DaysAsync(member, slug, "2027-03-08", "2027-03-08")).Should().Equal("2027-03-08");

        // Joined today: a day before today still was Bavarian
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            (await db.GroupMembershipPeriods.IgnoreQueryFilters().CountAsync(p => p.GroupId == berlin && p.UserId == memberId && p.Until == null))
                .Should().Be(1);
        }
        Dates(await DaysAsync(member, slug, "2026-06-04", "2026-06-04")).Should().Equal("2026-06-04");

        // Leaving the group closes the period; the future is Bavarian again
        (await owner.DeleteAsync($"/api/v1/Organizations/{slug}/groups/{berlin}/members/{memberId}")).EnsureSuccessStatusCode();
        (await DaysAsync(member, slug, "2027-03-08", "2027-03-08")).GetProperty("region").GetString().Should().Be("DE-BY");
        Dates(await DaysAsync(member, slug, "2027-03-08", "2027-03-08")).Should().BeEmpty();
    }

    [Fact]
    public async Task TwoGroupsWithDifferentRegions_FallBackToTheOrganization_AndSayItIsUnclear()
    {
        var (owner, member, slug, _, memberId) = await OrgAsync("do-conflict", region: "DE-NW");
        await TestHelpers.CreateGroupAsync(owner, slug, "Berlin", new { holidayRegion = "DE-BE" }, (memberId, "Member"));
        await TestHelpers.CreateGroupAsync(owner, slug, "Munich", new { holidayRegion = "DE-BY" }, (memberId, "Member"));

        var days = await DaysAsync(member, slug, "2027-01-01", "2027-01-31");
        days.GetProperty("regionConflict").GetBoolean().Should().BeTrue();
        days.GetProperty("region").GetString().Should().Be("DE-NW");
    }

    [Fact]
    public async Task GroupCompanyHoliday_OnlyForItsMembersAndSubGroups_WithExclusions()
    {
        var (owner, member, slug, ownerId, memberId) = await OrgAsync("do-company", region: null);
        var parent = await TestHelpers.CreateGroupAsync(owner, slug, "Office", new { });
        var created = await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups", new { name = "Office Team", parentGroupId = parent });
        var child = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{child}/members", new { userId = memberId, role = "Member" })).EnsureSuccessStatusCode();

        // 31.12. half day for the office (recurring), the whole organization gets 24.12. half
        var res = await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/holidays",
            new { date = "2026-12-31", name = "Silvester", isRecurring = true, isHalfDay = true, groupId = parent });
        res.EnsureSuccessStatusCode();
        var silvester = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/holidays",
            new { date = "2026-12-24", name = "Heiligabend", isRecurring = true, isHalfDay = true })).EnsureSuccessStatusCode();

        var memberDays = (await DaysAsync(member, slug, "2026-12-01", "2026-12-31")).GetProperty("days").EnumerateArray().ToList();
        memberDays.Select(d => d.GetProperty("date").GetString()).Should().Equal("2026-12-24", "2026-12-31");
        memberDays.Should().OnlyContain(d => d.GetProperty("isHalfDay").GetBoolean() && d.GetProperty("source").GetString() == "Company");
        Dates(await DaysAsync(owner, slug, "2026-12-01", "2026-12-31")).Should().Equal("2026-12-24");

        // The same day for the organization and a group is allowed, twice for the same group is not
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/holidays",
            new { date = "2026-12-31", name = "Again", groupId = parent })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Left out: the member no longer gets it
        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/holidays/{silvester}/exclusions", new { userIds = new[] { memberId } }))
            .EnsureSuccessStatusCode();
        Dates(await DaysAsync(member, slug, "2026-12-01", "2026-12-31")).Should().Equal("2026-12-24");
        var list = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/holidays");
        list.EnumerateArray().Single(h => h.GetProperty("id").GetInt32() == silvester).GetProperty("excludedUserIds")
            .EnumerateArray().Select(x => x.GetInt32()).Should().Equal(memberId);
        (await member.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/holidays")).EnumerateArray()
            .Should().OnlyContain(h => h.GetProperty("excludedUserIds").ValueKind == JsonValueKind.Null);
        // Only members can be left out
        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/holidays/{silvester}/exclusions", new { userIds = new[] { 999999 } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _ = ownerId;
    }

    [Fact]
    public async Task CompanyHalfDay_AndPublicHoliday_OnOneDay_CountOnce_FullWins()
    {
        var (owner, _, slug, _, _) = await OrgAsync("do-overlap", region: "DE");
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/holidays",
            new { date = "2026-12-25", name = "Firmenfeier", isHalfDay = true })).EnsureSuccessStatusCode();
        var days = (await DaysAsync(owner, slug, "2026-12-25", "2026-12-25")).GetProperty("days").EnumerateArray().Single();
        days.GetProperty("isHalfDay").GetBoolean().Should().BeFalse();
        days.GetProperty("name").GetString().Should().Be("1. Weihnachtstag");
    }

    [Fact]
    public async Task VacationOnAHalfHoliday_CostsHalfADay()
    {
        var (owner, _, slug, _, memberId) = await OrgAsync("do-halfvac", region: null);
        var year = DateTime.UtcNow.Year;
        // Every day a working day, so 24.12. is one whatever weekday it is this year
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedule/change",
            new { validFrom = $"{year}-01-01", distributeEvenly = false, targetMon = 8, targetTue = 8, targetWed = 8, targetThu = 8,
                  targetFri = 8, targetSat = 8, targetSun = 8 })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/holidays",
            new { date = $"{year}-12-24", name = "Heiligabend", isRecurring = true, isHalfDay = true })).EnsureSuccessStatusCode();
        foreach (var day in new[] { 23, 24 })
            (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences/admin",
                new { userId = memberId, date = $"{year}-12-{day}", type = 1 })).EnsureSuccessStatusCode();

        var org = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}");
        org.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("id").GetInt32() == memberId)
            .GetProperty("vacationDaysUsed").GetDouble().Should().Be(1.5);
    }

    [Fact]
    public async Task ColleaguesDays_NeedTheWorkScheduleVisibility()
    {
        var (owner, member, slug, ownerId, _) = await OrgAsync("do-visible");
        (await member.GetAsync($"/api/v1/organizations/{slug}/holidays/days?from=2026-01-01&to=2026-01-31&userId={ownerId}"))
            .StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings",
            new { visibility = TestHelpers.Matrix(("WorkSchedule", true, true)) })).EnsureSuccessStatusCode();
        (await member.GetAsync($"/api/v1/organizations/{slug}/holidays/days?from=2026-01-01&to=2026-01-31&userId={ownerId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.GetAsync($"/api/v1/organizations/{slug}/holidays/days?from=2020-01-01&to=2026-01-31"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnknownRegion_IsRefusedEverywhere()
    {
        var (owner, _, slug, _, memberId) = await OrgAsync("do-unknown");
        foreach (var response in new[]
                 {
                     await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { holidayRegion = "DE-XX" }),
                     await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedule/change",
                         new { validFrom = "2026-05-01", weeklyWorkHours = 40, distributeEvenly = true, holidayRegion = "XX" })
                 })
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("holiday.unknownRegion");
        }
        (await owner.GetAsync("/api/v1/organizations/holiday-presets/XX/dates?year=2026")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetFromJsonAsync<JsonElement>("/api/v1/organizations/holiday-presets/de-by-mh/dates?year=2026"))
            .EnumerateArray().Should().Contain(d => d.GetProperty("date").GetString() == "2026-08-15");
    }

    [Fact]
    public async Task RemoveCovered_DeletesImportedRowsTheRegionNowGives()
    {
        var (owner, _, slug, _, _) = await OrgAsync("do-covered", region: null);
        (await owner.PostAsync($"/api/v1/organizations/{slug}/holidays/import-preset?preset=DE-BY&year=2026&toYear=2027", null)).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/holidays",
            new { date = "2026-12-24", name = "Heiligabend", isRecurring = true, isHalfDay = true })).EnsureSuccessStatusCode();
        (await owner.PostAsync($"/api/v1/organizations/{slug}/holidays/remove-covered", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { holidayRegion = "DE-BY" })).EnsureSuccessStatusCode();
        var removed = await owner.PostAsync($"/api/v1/organizations/{slug}/holidays/remove-covered", null);
        removed.EnsureSuccessStatusCode();
        (await removed.Content.ReadFromJsonAsync<int>()).Should().BeGreaterThan(10);
        // The company's own half day stays; the region still gives every public holiday
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/holidays")).EnumerateArray()
            .Select(h => h.GetProperty("name").GetString()).Should().Equal("Heiligabend");
        Dates(await DaysAsync(owner, slug, "2027-01-01", "2027-01-06")).Should().Equal("2027-01-01", "2027-01-06");
    }
}
