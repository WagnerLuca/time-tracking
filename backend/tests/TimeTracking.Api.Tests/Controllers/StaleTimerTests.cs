using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>InMemory app without the minute loop of <see cref="FailureEntryService"/>, so a stale timer stays running until the test acts.</summary>
public class NoFailureLoopApiFactory : TimeTrackingApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            foreach (var job in services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(FailureEntryService)).ToList())
                services.Remove(job);
        });
    }
}

/// <summary>
/// <see cref="EntryTimes.LongestEntry"/> bounds the overlap and core-hours queries, so no stored entry may be longer than it.
/// A timer whose business-day end passed while nothing closed it (server down for days) must therefore not be stopped
/// "now" (days long) by a live stop or a new start: it is closed as a failure entry at its business-day end, exactly as
/// <see cref="FailureEntryService.CloseOverdueEntriesAsync"/> would have done.
/// </summary>
public class StaleTimerTests : IClassFixture<NoFailureLoopApiFactory>
{
    private static readonly TimeZoneInfo Berlin = FailureEntryService.ResolveTimeZone("Europe/Berlin");
    private readonly NoFailureLoopApiFactory _factory;

    public StaleTimerTests(NoFailureLoopApiFactory factory) => _factory = factory;

    [Fact]
    public void LongestEntry_CoversTheLongestEntryAnyPathCanStore()
    {
        // Every edit path enforces MaxDuration (24 h); the business-day-end close may add a DST hour on top
        EntryTimes.LongestEntry.Should().BeGreaterThanOrEqualTo(EntryTimes.MaxDuration + TimeSpan.FromDays(1));
    }

    private async Task<(HttpClient Client, int UserId, int OrgId, int EntryId, DateTime Start)> StaleTimerAsync(string prefix)
    {
        var (client, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}@t.test");
        var org = await TestHelpers.CreateOrganizationAsync(client, $"{prefix} Org", $"{prefix}-org");
        (await client.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings",
            new { businessDayEnd = "04:00", timeZone = "Europe/Berlin" })).EnsureSuccessStatusCode();

        // Started three days ago at 10:00 Berlin: its business day ended at 04:00 the next morning
        var startLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Berlin)).AddDays(-3).ToDateTime(new TimeOnly(10, 0));
        var start = TimeZoneInfo.ConvertTimeToUtc(startLocal, Berlin);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
        var entry = new TimeEntry
        {
            UserId = login.User.Id, OrganizationId = org.Id, StartTime = start, IsRunning = true, CreatedAt = start, UpdatedAt = start
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();
        return (client, login.User.Id, org.Id, entry.Id, start);
    }

    private async Task<List<TimeEntry>> EntriesAsync(int userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
        return await db.TimeEntries.AsNoTracking().Where(e => e.UserId == userId).OrderBy(e => e.StartTime).ToListAsync();
    }

    private static void ShouldBeClosedAsFailure(TimeEntry stale, DateTime start)
    {
        var dayEnd = FailureEntryService.BusinessDayEndUtc(start, new TimeOnly(4, 0), Berlin);
        stale.IsRunning.Should().BeFalse();
        stale.IsFailure.Should().BeTrue("a timer that ran past its business-day end is a failure entry, as the auto-stop makes it");
        stale.EndTime.Should().Be(dayEnd, "it ends at its business-day end, not now (days later)");
        (stale.EndTime!.Value - stale.StartTime).Should().BeLessThanOrEqualTo(EntryTimes.MaxDuration);
    }

    [Fact]
    public async Task Stop_OfATimerPastItsBusinessDayEnd_ClosesItAsAFailureAtThatEnd()
    {
        var (client, userId, _, entryId, start) = await StaleTimerAsync("stale-stop");

        var stop = await client.PostAsJsonAsync("/api/v1/TimeTracking/stop", new { });
        stop.IsSuccessStatusCode.Should().BeTrue($"stopping a stale timer is not an error (got {(int)stop.StatusCode})");

        var entries = await EntriesAsync(userId);
        entries.Should().ContainSingle();
        entries[0].Id.Should().Be(entryId);
        ShouldBeClosedAsFailure(entries[0], start);
        entries.Should().OnlyContain(e => e.EndTime == null || e.EndTime.Value - e.StartTime <= EntryTimes.MaxDuration);
        await ShouldHaveNoticeButNoMailAsync(userId, entryId, "stale-stop@t.test");
    }

    private async Task ShouldHaveNoticeButNoMailAsync(int userId, int entryId, string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
        (await db.Notifications.AsNoTracking().Where(n => n.UserId == userId && n.Type == "FailureEntry").ToListAsync())
            .Should().ContainSingle().Which.RelatedEntityId.Should().Be(entryId);
        _factory.Mail.Sent.Where(m => m.To == email && m.Subject.Contains("Timer")).Should().BeEmpty("the member is acting right now: in-app notice only");
    }

    [Fact]
    public async Task Start_WhileATimerPastItsBusinessDayEndRuns_ClosesThatAsAFailure_AndRunsTheNewOneNormally()
    {
        var (client, userId, orgId, entryId, start) = await StaleTimerAsync("stale-start");
        var before = DateTime.UtcNow;

        var started = await client.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationId = orgId });
        started.StatusCode.Should().Be(HttpStatusCode.Created);

        var entries = await EntriesAsync(userId);
        entries.Should().HaveCount(2);
        var stale = entries.Single(e => e.Id == entryId);
        ShouldBeClosedAsFailure(stale, start);
        var fresh = entries.Single(e => e.Id != entryId);
        fresh.IsRunning.Should().BeTrue();
        fresh.IsFailure.Should().BeFalse();
        fresh.EndTime.Should().BeNull();
        fresh.StartTime.Should().BeOnOrAfter(before.AddSeconds(-1)).And.BeOnOrBefore(DateTime.UtcNow.AddSeconds(1));
        entries.Should().OnlyContain(e => e.EndTime == null || e.EndTime.Value - e.StartTime <= EntryTimes.MaxDuration);
        await ShouldHaveNoticeButNoMailAsync(userId, entryId, "stale-start@t.test");
    }

    [Fact]
    public async Task Stop_WithinTheBusinessDay_StillStopsNormally()
    {
        var (client, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "stale-normal@t.test");
        var org = await TestHelpers.CreateOrganizationAsync(client, "stale-normal Org", "stale-normal-org");
        (await client.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationId = org.Id })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/api/v1/TimeTracking/stop", new { })).EnsureSuccessStatusCode();
        var entry = (await EntriesAsync(login.User.Id)).Single();
        entry.IsRunning.Should().BeFalse();
        entry.IsFailure.Should().BeFalse();
        entry.EndTime.Should().NotBeNull();
    }
}
