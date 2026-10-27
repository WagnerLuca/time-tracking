using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>Saturday/Sunday targets, and absence ranges / vacation usage counting only the member's working days.</summary>
public class WeekendScheduleTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public WeekendScheduleTests(TimeTrackingApiFactory factory) => _factory = factory;

    [Fact]
    public async Task TuesdayToSaturdaySchedule_VacationRangeSkipsDaysOffAndHolidays()
    {
        var (owner, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "weekend@test.com");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Weekend Shop", "weekend-shop");

        var schedule = await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/work-schedule/change", new
        {
            validFrom = "2026-01-01", weeklyWorkHours = 40, distributeEvenly = false,
            targetMon = 0, targetTue = 8, targetWed = 8, targetThu = 8, targetFri = 8, targetSat = 8, targetSun = 0
        });
        schedule.EnsureSuccessStatusCode();
        (await schedule.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("current").GetProperty("targetSat").GetDouble().Should().Be(8);

        (await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/holidays",
            new { date = "2026-03-11", name = "Betriebsfeier", isRecurring = false })).EnsureSuccessStatusCode();

        // Fri 6 – Wed 11 Mar 2026: Fri, Sat and Tue count; Sun and Mon are days off, Wed a holiday
        var range = await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/absences/range",
            new { from = "2026-03-06", to = "2026-03-11", type = 1 });
        range.EnsureSuccessStatusCode();
        (await range.Content.ReadFromJsonAsync<List<JsonElement>>())!.Select(a => a.GetProperty("date").GetString())
            .Should().Equal("2026-03-06", "2026-03-07", "2026-03-10");

        // A range of days off only is rejected
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/absences/range",
            new { from = "2026-03-15", to = "2026-03-16", type = 1 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // A single vacation day entered on a day off does not use up the budget
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/absences",
            new { date = "2026-03-23", type = 1 })).EnsureSuccessStatusCode();   // Monday = day off

        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{org.Slug}");
        var me = detail.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("id").GetInt32() == login.User.Id);
        me.GetProperty("vacationDaysUsed").GetDouble().Should().Be(3);
    }
}
