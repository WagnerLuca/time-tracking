using System.Net.Http.Json;
using FluentAssertions;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Reports, CSV import/export and "today"/"this week" follow the organization's time zone:
/// an entry from 00:30 to 02:00 in Berlin is on that Berlin day and reads 00:30 everywhere,
/// although it is stored as 23:30 UTC on the previous day.
/// </summary>
public class TimeZoneTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public TimeZoneTests(TimeTrackingApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("2026-03-02 00:30", "2026-03-01T23:30:00Z")] // winter, UTC+1
    [InlineData("2026-07-02 00:30", "2026-07-01T22:30:00Z")] // summer, UTC+2
    [InlineData("2026-03-29 02:30", "2026-03-29T01:00:00Z")] // inside the spring-forward gap → 03:00 CEST
    public void OrgClock_ConvertsLocalWallClockToUtc(string local, string expectedUtc)
    {
        var clock = OrgClock.For("Europe/Berlin");
        clock.ToUtc(DateTime.Parse(local)).Should().Be(DateTime.Parse(expectedUtc).ToUniversalTime());
    }

    [Fact]
    public void OrgClock_DayRange_CoversTheLocalDayAcrossDst()
    {
        var clock = OrgClock.For("Europe/Berlin");
        var (start, end) = clock.DayRangeUtc(new DateOnly(2026, 3, 29), new DateOnly(2026, 3, 29));
        start.Should().Be(new DateTime(2026, 3, 28, 23, 0, 0, DateTimeKind.Utc));
        (end - start).Should().Be(TimeSpan.FromHours(23));   // the short day
    }

    [Fact]
    public async Task CsvImport_Export_AndDailyReport_UseOrganizationLocalTime()
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "tz@tz.test");
        var org = await TestHelpers.CreateOrganizationAsync(client, "TZ Org", "tz-org");
        (await client.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings",
            new { csvImportMode = 2, timeZone = "Europe/Berlin" })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync($"/api/v1/ExportImport/import/confirm?orgSlug={org.Slug}", new[]
        {
            new { date = "2026-03-02", startTime = "00:30", endTime = "02:00", pauseMinutes = 0, description = "Night shift" }
        })).EnsureSuccessStatusCode();

        // Stored as the right instant
        var history = await client.GetFromJsonAsync<PaginatedResponseDto<TimeEntryResponseDto>>(
            "/api/v1/TimeTracking?limit=10", TestHelpers.JsonOptions);
        history!.Items.Single().StartTime.ToUniversalTime().Should().Be(new DateTime(2026, 3, 1, 23, 30, 0, DateTimeKind.Utc));

        // Entries export shows the local day and times
        var entriesCsv = await (await client.PostAsJsonAsync("/api/v1/ExportImport/export",
            new { lang = "en", type = "entries", organizationSlug = org.Slug, from = "2026-03-02", to = "2026-03-02" })).Content.ReadAsStringAsync();
        entriesCsv.Should().Contain("2026-03-02,Mon,00:30,02:00");

        // Daily report puts it on the local day
        var dailyCsv = await (await client.PostAsJsonAsync("/api/v1/ExportImport/export",
            new { lang = "en", type = "daily", organizationSlug = org.Slug, from = "2026-03-01", to = "2026-03-02" })).Content.ReadAsStringAsync();
        var lines = dailyCsv.Split('\n');
        lines.Single(l => l.StartsWith("2026-03-02")).Should().Contain(",1.50,");
        lines.Single(l => l.StartsWith("2026-03-01")).Should().Contain(",0.00,");

        // Re-importing the same local row is recognised as a duplicate
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("Date,Start Time,End Time\n2026-03-02,00:30,02:00\n")), "file", "e.csv");
        var preview = await (await client.PostAsync($"/api/v1/ExportImport/import/preview?orgSlug={org.Slug}", form))
            .Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        preview.GetProperty("duplicateCount").GetInt32().Should().Be(1);
    }
}
