using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Custom absence types credit the day's target per their CreditMode and may count against the
/// vacation budget; recurring 29 February holidays do not crash non-leap years.
/// </summary>
public class AbsenceCreditTests : IClassFixture<TimeTrackingApiFactory>
{
    private const int None = 0, FillToTarget = 1, FullCredit = 2;

    private readonly TimeTrackingApiFactory _factory;

    public AbsenceCreditTests(TimeTrackingApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Owner, string Slug, int OwnerId)> OrgWithScheduleAsync(string prefix)
    {
        var (owner, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}@credit.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, $"{prefix} Org", $"{prefix}-org");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { csvImportMode = 2, defaultVacationDays = 20 })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/work-schedules",
            new { validFrom = "2026-01-01", weeklyWorkHours = 40, distributeEvenly = true })).EnsureSuccessStatusCode();
        return (owner, org.Slug, login.User.Id);
    }

    private static async Task<int> TypeAsync(HttpClient owner, string slug, string name, int creditMode, bool countsAsVacation = false) =>
        (await (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/absence-types",
            new { name, creditMode, countsAsVacation })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

    private static async Task<string> DayAsync(HttpClient owner, string slug, string date)
    {
        var csv = await (await owner.PostAsJsonAsync("/api/v1/ExportImport/export",
            new { lang = "en", type = "daily", organizationSlug = slug, from = date, to = date })).Content.ReadAsStringAsync();
        return csv.Split('\n').Single(l => l.StartsWith(date));
    }

    [Fact]
    public async Task CustomTypes_CreditTargetPerCreditMode()
    {
        var (owner, slug, ownerId) = await OrgWithScheduleAsync("credit-modes");
        var trip = await TypeAsync(owner, slug, "Business Trip", FillToTarget);
        var training = await TypeAsync(owner, slug, "Training", FullCredit);
        var marker = await TypeAsync(owner, slug, "Marker", None);

        // Mon 2 Mar: 3h tracked + business trip → topped up to 8h, no overtime
        (await owner.PostAsJsonAsync($"/api/v1/ExportImport/import/confirm?orgSlug={slug}",
            new[] { new { date = "2026-03-02", startTime = "08:00", endTime = "11:00", pauseMinutes = 0, description = "prep" } })).EnsureSuccessStatusCode();
        foreach (var (date, type) in new[] { ("2026-03-02", trip), ("2026-03-03", training), ("2026-03-04", marker) })
            (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences/admin",
                new { userId = ownerId, date, type = 2, customTypeId = type })).EnsureSuccessStatusCode();

        // Target 8h, 3h tracked: the trip fills the missing 5h, so the effective target is 3h and overtime 0
        (await DayAsync(owner, slug, "2026-03-02")).Should().Contain("Mon,3.00,3.00,0,3.00,0.00").And.Contain("Business Trip");
        (await DayAsync(owner, slug, "2026-03-03")).Should().Contain("Tue,0.00,").And.Contain("Training");      // full credit → target 0
        (await DayAsync(owner, slug, "2026-03-04")).Should().Contain("Wed,8.00,").And.Contain("Marker");        // no credit → target stays 8
    }

    [Fact]
    public async Task CustomTypeCountingAsVacation_ReducesVacationBalance()
    {
        var (owner, slug, ownerId) = await OrgWithScheduleAsync("credit-vac");
        var sabbatical = await TypeAsync(owner, slug, "Unpaid leave", FullCredit, countsAsVacation: true);
        var date = $"{DateTime.UtcNow.Year}-12-01";
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences/admin",
            new { userId = ownerId, date, type = 2, customTypeId = sabbatical })).EnsureSuccessStatusCode();

        var org = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}");
        var me = org.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("id").GetInt32() == ownerId);
        me.GetProperty("vacationDaysUsed").GetDouble().Should().Be(1);
    }

    [Fact]
    public async Task RecurringLeapDayHoliday_DoesNotBreakNonLeapYears()
    {
        var (owner, slug, _) = await OrgWithScheduleAsync("credit-leap");
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/holidays",
            new { date = "2024-02-29", name = "Leap Day", isRecurring = true })).EnsureSuccessStatusCode();

        (await owner.GetAsync($"/api/v1/Organizations/{slug}")).EnsureSuccessStatusCode();
        (await DayAsync(owner, slug, "2026-02-27")).Should().NotBeNullOrEmpty();
    }
}
