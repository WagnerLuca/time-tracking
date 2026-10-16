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
        // New organizations come with the statutory rules: 6 h → 30 min, 9 h → 45 min
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
    public async Task ALaterGap_RemovesTheEarlierAutomaticBreak()
    {
        var (client, _, orgId) = await OrgWithRulesAsync("regap");
        var day = Berlin.Today().AddDays(-9);

        var morning = await TrackAsync(client, orgId, day, "08:00", "14:30");
        morning.PauseDurationMinutes.Should().Be(30);   // 6.5 h alone
        // 8.5 h in total need 30 min, and the 30 min gap already is that break
        (await TrackAsync(client, orgId, day, "15:00", "17:00")).PauseDurationMinutes.Should().Be(0);

        var history = await client.GetFromJsonAsync<PaginatedResponseDto<TimeEntryResponseDto>>("/api/v1/TimeTracking", TestHelpers.JsonOptions);
        history!.Items.Single(e => e.Id == morning.Id).PauseDurationMinutes.Should().Be(0);
    }

    [Fact]
    public async Task OneLongEntry_GetsTheFullBreak()
    {
        var (client, _, orgId) = await OrgWithRulesAsync("long");
        var entry = await TrackAsync(client, orgId, Berlin.Today().AddDays(-3), "07:00", "17:00");
        entry.PauseDurationMinutes.Should().Be(45);
    }

    [Fact]
    public async Task ExactlySixHours_NeedsNoBreak_AndOneMinuteMoreCostsOneMinute()
    {
        var (client, _, orgId) = await OrgWithRulesAsync("exact");
        (await TrackAsync(client, orgId, Berlin.Today().AddDays(-4), "08:00", "14:00")).PauseDurationMinutes.Should().Be(0);
        // Issue #76: the break never pushes the net time below the rule's threshold
        (await TrackAsync(client, orgId, Berlin.Today().AddDays(-5), "08:00", "14:01")).PauseDurationMinutes.Should().Be(1);
    }

    /// <summary>Issue #76: each rule deducts at most the time beyond its threshold; the largest such deduction wins.</summary>
    [Theory]
    [InlineData("14:15", 15)]   // 6 h 15 → net 6 h
    [InlineData("14:30", 30)]   // 6 h 30 → net 6 h
    [InlineData("15:00", 30)]   // 7 h    → net 6 h 30
    [InlineData("17:00", 30)]   // 9 h    → 30 (the 9 h rule needs more than 9 h)
    [InlineData("17:20", 30)]   // 9 h 20 → max(20, 30) = 30, net 8 h 50
    [InlineData("17:40", 40)]   // 9 h 40 → max(40, 30) = 40, net 9 h
    [InlineData("17:45", 45)]   // 9 h 45 → net 9 h
    [InlineData("18:00", 45)]   // 10 h   → net 9 h 15
    public async Task TheBreak_IsCappedAtTheTimeBeyondTheThreshold(string end, int expectedPause)
    {
        var (client, _, orgId) = await OrgWithRulesAsync($"cap{end.Replace(":", "")}");
        (await TrackAsync(client, orgId, Berlin.Today().AddDays(-4), "08:00", end)).PauseDurationMinutes.Should().Be(expectedPause);
    }

    [Fact]
    public void NetTime_NeverDropsWhenMoreTimeIsTracked()
    {
        var rules = new[] { (6.0, 30), (9.0, 45) };
        var previousNet = 0;
        for (var minutes = 0; minutes <= 12 * 60; minutes++)
        {
            var net = minutes - PauseRules.RequiredPauseMinutes(rules, minutes / 60.0);
            net.Should().BeGreaterThanOrEqualTo(previousNet, $"{minutes} min tracked");
            previousNet = net;
        }
        PauseRules.RequiredPauseMinutes(rules, 6.25).Should().Be(15);
        PauseRules.RequiredPauseMinutes(rules, 6 + 0.5 / 60).Should().Be(0);   // half a minute over: nothing whole to deduct
        PauseRules.RequiredPauseMinutes([], 10).Should().Be(0);
    }

    [Fact]
    public async Task TwoEntries_JustOverTheThreshold_DeductOnlyTheExcess()
    {
        var (client, slug, orgId) = await OrgWithRulesAsync("capsplit");
        var day = Berlin.Today().AddDays(-11);

        // 3 h + 3 h 10 with a 5 min gap (not a break) → 6 h 10 → 10 min, on the longer entry
        var first = await TrackAsync(client, orgId, day, "08:00", "11:00");
        var second = await TrackAsync(client, orgId, day, "11:05", "14:15");
        second.PauseDurationMinutes.Should().Be(10);

        var page = await client.GetFromJsonAsync<PaginatedResponseDto<TimeEntryResponseDto>>("/api/v1/TimeTracking?limit=10", TestHelpers.JsonOptions);
        page!.Items.Single(e => e.Id == first.Id).PauseDurationMinutes.Should().Be(0);

        var overview = await client.GetFromJsonAsync<List<MemberTimeOverviewResponseDto>>(
            $"/api/v1/Organizations/{slug}/time-overview?from={Berlin.StartOfDayUtc(day):O}&to={Berlin.StartOfDayUtc(day.AddDays(1)):O}", TestHelpers.JsonOptions);
        overview!.Single().NetTrackedMinutes.Should().Be(360);
    }

    [Fact]
    public async Task GapsAndManualPauses_CountAgainstTheCappedBreak()
    {
        var (client, _, orgId) = await OrgWithRulesAsync("capcredit");

        // 3 h + 3 h 20 = 6 h 20 → 20 min required; the 15 min gap covers 15 → 5 left
        var day = Berlin.Today().AddDays(-12);
        await TrackAsync(client, orgId, day, "08:00", "11:00");
        (await TrackAsync(client, orgId, day, "11:15", "14:35")).PauseDurationMinutes.Should().Be(5);

        // A manual 20 min pause already covers the 20 min that 6 h 20 (5 min gap) require
        var other = Berlin.Today().AddDays(-13);
        var first = await TrackAsync(client, orgId, other, "08:00", "11:00");
        (await client.PutAsJsonAsync($"/api/v1/TimeTracking/{first.Id}", new { pauseDurationMinutes = 20 })).EnsureSuccessStatusCode();
        (await TrackAsync(client, orgId, other, "11:05", "14:25")).PauseDurationMinutes.Should().Be(0);
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
