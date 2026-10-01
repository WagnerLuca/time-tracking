using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Failure entries: timers still running at the organization's business day end are
/// stopped there, flagged, excluded from totals, and cleared once the member corrects them.
/// </summary>
public class FailureEntryTests : IClassFixture<TimeTrackingApiFactory>
{
    private static readonly TimeZoneInfo Berlin = FailureEntryService.ResolveTimeZone("Europe/Berlin");
    private readonly TimeTrackingApiFactory _factory;

    public FailureEntryTests(TimeTrackingApiFactory factory) => _factory = factory;

    // ── Cutoff calculation ───────────────────────────────────────────────

    [Theory]
    // midnight day end, winter (UTC+1): 08:00 local → 00:00 next day local
    [InlineData("2026-03-02T07:00:00Z", "00:00", "2026-03-02T23:00:00Z")]
    // gastro 04:00 day end: evening start ends next morning
    [InlineData("2026-03-02T21:00:00Z", "04:00", "2026-03-03T03:00:00Z")]
    // gastro 04:00 day end: start after midnight still ends the same morning
    [InlineData("2026-03-03T01:00:00Z", "04:00", "2026-03-03T03:00:00Z")]
    // summer time (UTC+2)
    [InlineData("2026-07-01T06:00:00Z", "00:00", "2026-07-01T22:00:00Z")]
    // day end 02:30 does not exist on spring-forward day (29 Mar 2026) → next valid time, 03:00 CEST
    [InlineData("2026-03-28T20:00:00Z", "02:30", "2026-03-29T01:00:00Z")]
    public void BusinessDayEndUtc_ReturnsFirstDayEndAfterStart(string startUtc, string dayEnd, string expectedUtc)
    {
        var start = DateTime.Parse(startUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var expected = DateTime.Parse(expectedUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal);

        FailureEntryService.BusinessDayEndUtc(start, TimeOnly.Parse(dayEnd), Berlin).Should().Be(expected);
    }

    // ── End to end ──────────────────────────────────────────────────────

    private async Task<(HttpClient Client, int UserId, int OrgId, string Slug)> SetupAsync(string prefix, string dayEnd = "04:00")
    {
        var (client, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}@test.com");
        var org = await TestHelpers.CreateOrganizationAsync(client, $"{prefix} Bar", $"{prefix}-bar");
        (await client.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings",
            new { businessDayEnd = dayEnd, timeZone = "Europe/Berlin" })).EnsureSuccessStatusCode();
        return (client, login.User.Id, org.Id, org.Slug);
    }

    private async Task<int> AddRunningEntryAsync(int userId, int orgId, DateTime startUtc)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
        var entry = new TimeEntry
        {
            UserId = userId, OrganizationId = orgId, StartTime = startUtc, IsRunning = true,
            Description = "Late shift", CreatedAt = startUtc, UpdatedAt = startUtc
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task CloseOverdueAsync(DateTime nowUtc)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
        await FailureEntryService.CloseOverdueEntriesAsync(db, nowUtc);
    }

    [Fact]
    public async Task ForgottenTimer_IsStoppedAtDayEnd_FlaggedExcludedAndCorrectable()
    {
        var (client, userId, orgId, slug) = await SetupAsync("failure-flow");
        var start = new DateTime(2026, 3, 2, 21, 0, 0, DateTimeKind.Utc);     // 22:00 Berlin
        var entryId = await AddRunningEntryAsync(userId, orgId, start);

        await CloseOverdueAsync(new DateTime(2026, 3, 3, 12, 0, 0, DateTimeKind.Utc));

        // Flagged, stopped at 04:00 Berlin, duration unknown
        var failures = await client.GetFromJsonAsync<List<JsonElement>>("/api/v1/TimeTracking/failures");
        failures.Should().ContainSingle();
        var f = failures![0];
        f.GetProperty("id").GetInt32().Should().Be(entryId);
        f.GetProperty("isFailure").GetBoolean().Should().BeTrue();
        f.GetProperty("isRunning").GetBoolean().Should().BeFalse();
        f.GetProperty("endTime").GetDateTime().ToUniversalTime().Should().Be(new DateTime(2026, 3, 3, 3, 0, 0, DateTimeKind.Utc));
        f.GetProperty("netDurationMinutes").ValueKind.Should().Be(JsonValueKind.Null);

        // Owner was notified
        var notifications = await client.GetStringAsync("/api/v1/Notifications");
        notifications.Should().Contain("FailureEntry");

        // Reports flag it and count nothing for it
        var entriesCsv = await (await client.PostAsJsonAsync("/api/v1/ExportImport/export",
            new { lang = "en", type = "entries", organizationSlug = slug, from = "2026-03-01", to = "2026-03-04" })).Content.ReadAsStringAsync();
        entriesCsv.Should().Contain("Needs Correction");

        // Correcting the end time clears the flag and makes it count
        var fix = await client.PutAsJsonAsync($"/api/v1/TimeTracking/{entryId}",
            new { endTime = new DateTime(2026, 3, 3, 1, 0, 0, DateTimeKind.Utc) });
        fix.EnsureSuccessStatusCode();
        var fixedEntry = await fix.Content.ReadFromJsonAsync<JsonElement>();
        fixedEntry.GetProperty("isFailure").GetBoolean().Should().BeFalse();
        fixedEntry.GetProperty("netDurationMinutes").GetDouble().Should().Be(240);

        (await client.GetFromJsonAsync<List<JsonElement>>("/api/v1/TimeTracking/failures")).Should().BeEmpty();
    }

    [Fact]
    public async Task TimerWithinBusinessDay_KeepsRunning()
    {
        var (client, userId, orgId, _) = await SetupAsync("failure-keep");
        await AddRunningEntryAsync(userId, orgId, new DateTime(2026, 3, 2, 21, 0, 0, DateTimeKind.Utc));

        await CloseOverdueAsync(new DateTime(2026, 3, 3, 2, 59, 0, DateTimeKind.Utc));   // 03:59 Berlin

        (await client.GetFromJsonAsync<List<JsonElement>>("/api/v1/TimeTracking/failures")).Should().BeEmpty();
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/TimeTracking/current")).GetProperty("isRunning").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task DailyReport_ExcludesFailureEntry_AndMarksDay()
    {
        var (client, userId, orgId, slug) = await SetupAsync("failure-report", dayEnd: "00:00");
        await AddRunningEntryAsync(userId, orgId, new DateTime(2026, 3, 4, 7, 0, 0, DateTimeKind.Utc));
        await CloseOverdueAsync(new DateTime(2026, 3, 5, 12, 0, 0, DateTimeKind.Utc));

        var csv = await (await client.PostAsJsonAsync("/api/v1/ExportImport/export",
            new { lang = "en", type = "daily", organizationSlug = slug, from = "2026-03-04", to = "2026-03-04" })).Content.ReadAsStringAsync();

        var day = csv.Split('\n').Single(l => l.StartsWith("2026-03-04"));
        day.Should().Contain("Needs Correction");
        day.Should().Contain(",0.00,");   // worked hours stay 0 until corrected
    }

    [Theory]
    [InlineData("{\"timeZone\":\"Mars/Olympus\"}")]
    [InlineData("{\"businessDayEnd\":\"25:00\"}")]
    public async Task Settings_RejectInvalidDayEndOrTimeZone(string body)
    {
        var (client, _, _, slug) = await SetupAsync($"failure-bad{Math.Abs(body.GetHashCode())}");
        var response = await client.PutAsync($"/api/v1/Organizations/{slug}/settings",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
