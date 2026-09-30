using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>A multi-day absence is created all or nothing, weekdays only.</summary>
public class AbsenceRangeTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public AbsenceRangeTests(TimeTrackingApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Range_CreatesWeekdaysOnly_AndFailsWhollyOnConflict()
    {
        var (owner, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "range@abs.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Range Org", "range-org");

        // Fri 6 Mar – Tue 10 Mar 2026: Fri, Mon, Tue
        var created = await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/absences/range",
            new { from = "2026-03-06", to = "2026-03-10", type = 1 });
        created.EnsureSuccessStatusCode();
        (await created.Content.ReadFromJsonAsync<List<JsonElement>>())!.Select(a => a.GetProperty("date").GetString())
            .Should().Equal("2026-03-06", "2026-03-09", "2026-03-10");

        // Admins can add a range for a member too (Wed 11 – Fri 13 Mar)
        var adminRange = await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/absences/admin/range",
            new { userId = login.User.Id, from = "2026-03-11", to = "2026-03-13", type = 1 });
        adminRange.EnsureSuccessStatusCode();
        var reversed = await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/absences/range",
            new { from = "2026-03-16", to = "2026-03-06", type = 1 });
        reversed.StatusCode.Should().Be(HttpStatusCode.BadRequest);   // reversed range

        // 12–17 Mar overlaps 12/13 Mar → none of it is created, not even 16/17 Mar
        var again = await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/absences/range",
            new { from = "2026-03-12", to = "2026-03-17", type = 1 });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var list = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{org.Slug}/absences?limit=100");
        list.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("date").GetString())
            .Should().NotContain(new[] { "2026-03-16", "2026-03-17" });
    }
}
