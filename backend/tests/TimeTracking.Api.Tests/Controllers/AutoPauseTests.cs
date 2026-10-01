using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>Automatic breaks are required per local day (ArbZG §4) and real gaps count as break.</summary>
public class AutoPauseTests : IClassFixture<TimeTrackingApiFactory>
{
    private static readonly OrgClock Berlin = OrgClock.For("Europe/Berlin");
    private readonly TimeTrackingApiFactory _factory;

    public AutoPauseTests(TimeTrackingApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Client, string Slug, int OrgId)> OrgWithRulesAsync(string tag)
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{tag}@pause.test");
        var org = await TestHelpers.CreateOrganizationAsync(client, $"Pause {tag}", $"pause-{tag}");
        (await client.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { editPastEntriesMode = 2, editPauseMode = 2, memberTimeEntryVisibility = true, autoPauseEnabled = true })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/pause-rules", new { minHours = 6, pauseMinutes = 30 })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/pause-rules", new { minHours = 9, pauseMinutes = 45 })).EnsureSuccessStatusCode();
        return (client, org.Slug, org.Id);
    }

    /// <summary>Tracks an entry via start/stop, then edits it to the given local times (which applies the pause rules).</summary>
    private static async Task<TimeEntryResponseDto> TrackAsync(HttpClient client, int orgId, DateOnly day, string from, string to)
    {
        (await client.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationId = orgId })).EnsureSuccessStatusCode();
        var stopped = await (await client.PostAsJsonAsync("/api/v1/TimeTracking/stop", new { })).Content.ReadFromJsonAsync<TimeEntryResponseDto>(TestHelpers.JsonOptions);
        var at = (string hhmm) => Berlin.ToUtc(day.ToDateTime(TimeOnly.ParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture)));
        var response = await client.PutAsJsonAsync($"/api/v1/TimeTracking/{stopped!.Id}", new { startTime = at(from), endTime = at(to) });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TimeEntryResponseDto>(TestHelpers.JsonOptions))!;
    }

    [Fact]
    public async Task TwoEntries_TheGapCountsAsBreak_OnlyTheRestIsAutomatic()
    {
        var (client, slug, orgId) = await OrgWithRulesAsync("gap");
        var day = Berlin.Today().AddDays(-7);

        var morning = await TrackAsync(client, orgId, day, "08:00", "12:00");
        morning.PauseDurationMinutes.Should().Be(0);   // 4 h alone need no break
        // 8 h 15 min in total → 30 min required, 15 already taken as the gap
        var afternoon = await TrackAsync(client, orgId, day, "12:15", "16:30");
        afternoon.PauseDurationMinutes.Should().Be(15);

        // Every aggregation sees the same net day: 8.25 h − 15 min auto pause = 8 h
        var csv = await (await client.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "daily", organizationSlug = slug, lang = "en",
            from = day.ToString("yyyy-MM-dd"), to = day.ToString("yyyy-MM-dd")
        })).Content.ReadAsStringAsync();
        var row = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1].Split(',');
        double.Parse(row[5], CultureInfo.InvariantCulture).Should().Be(8.0);   // NetWorkedHours

        var overview = await client.GetFromJsonAsync<List<MemberTimeOverviewResponseDto>>(
            $"/api/v1/Organizations/{slug}/time-overview?from={Berlin.StartOfDayUtc(day):O}&to={Berlin.StartOfDayUtc(day.AddDays(1)):O}", TestHelpers.JsonOptions);
        overview!.Single().NetTrackedMinutes.Should().Be(480);
    }

    [Fact]
    public async Task OneLongEntry_GetsTheFullBreak()
    {
        var (client, _, orgId) = await OrgWithRulesAsync("long");
        var entry = await TrackAsync(client, orgId, Berlin.Today().AddDays(-3), "07:00", "17:00");
        entry.PauseDurationMinutes.Should().Be(45);
    }

    [Fact]
    public async Task ExactlySixHours_NeedsNoBreak_ButMoreDoes()
    {
        var (client, _, orgId) = await OrgWithRulesAsync("exact");
        (await TrackAsync(client, orgId, Berlin.Today().AddDays(-4), "08:00", "14:00")).PauseDurationMinutes.Should().Be(0);
        (await TrackAsync(client, orgId, Berlin.Today().AddDays(-5), "08:00", "14:01")).PauseDurationMinutes.Should().Be(30);
    }

    [Fact]
    public async Task ShortGaps_AreNotBreaks_AndManualPausesOfOtherEntriesCount()
    {
        var (client, _, orgId) = await OrgWithRulesAsync("short");
        var day = Berlin.Today().AddDays(-6);

        var first = await TrackAsync(client, orgId, day, "08:00", "12:00");
        // A manual 20 min pause on the first entry stays and is credited
        (await client.PutAsJsonAsync($"/api/v1/TimeTracking/{first.Id}", new { pauseDurationMinutes = 20 })).EnsureSuccessStatusCode();
        // 5 min gap does not count; 7 h 55 min → 30 required − 20 already set
        var second = await TrackAsync(client, orgId, day, "12:05", "16:00");
        second.PauseDurationMinutes.Should().Be(10);

        var page = await client.GetFromJsonAsync<PaginatedResponseDto<TimeEntryResponseDto>>("/api/v1/TimeTracking?limit=10", TestHelpers.JsonOptions);
        page!.Items.Single(e => e.Id == first.Id).PauseDurationMinutes.Should().Be(20);
    }

    [Fact]
    public void CreditedGaps_HandleOverlaps()
    {
        var t = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        PauseRules.CreditedGapMinutes([(t, t.AddHours(4)), (t.AddHours(1), t.AddHours(2)), (t.AddHours(4.5), t.AddHours(6))])
            .Should().Be(30);
    }
}
