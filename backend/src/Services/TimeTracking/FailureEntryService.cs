using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>
/// Closes timers that run past their organization's business day end and marks them
/// as failure entries (<see cref="TimeEntry.IsFailure"/>): the member forgot to stop
/// tracking. A failure entry counts as 0 worked time until the member corrects its end
/// time, so a forgotten timer can neither run forever nor inflate dashboards and reports.
/// </summary>
public class FailureEntryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FailureEntryService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

    public FailureEntryService(IServiceScopeFactory scopeFactory, ILogger<FailureEntryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
                var closed = await CloseOverdueEntriesAsync(context, DateTime.UtcNow, stoppingToken);
                if (closed > 0)
                    _logger.LogInformation("Auto-stopped {Count} timer(s) at business day end.", closed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error while closing overdue timers.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    /// <summary>
    /// Stops every running entry whose business day has ended by <paramref name="nowUtc"/>,
    /// at that day end, flags it as a failure and notifies its owner. Returns the count.
    /// </summary>
    public static async Task<int> CloseOverdueEntriesAsync(TimeTrackingDbContext context, DateTime nowUtc, CancellationToken ct = default)
    {
        // ponytail: loads all running entries each minute; fine while running timers number in the hundreds
        var running = await context.TimeEntries
            .Include(e => e.Organization)
            .Where(e => e.IsRunning)
            .ToListAsync(ct);

        var closed = 0;
        foreach (var entry in running)
        {
            var dayEnd = entry.Organization?.BusinessDayEnd ?? TimeOnly.MinValue;
            var tz = ResolveTimeZone(entry.Organization?.TimeZone);
            var cutoff = BusinessDayEndUtc(entry.StartTime, dayEnd, tz);
            if (nowUtc < cutoff) continue;

            entry.EndTime = cutoff;
            entry.IsRunning = false;
            entry.IsFailure = true;
            entry.UpdatedAt = nowUtc;

            context.Notifications.Add(new Notification
            {
                UserId = entry.UserId,
                OrganizationId = entry.OrganizationId,
                Title = "Timer stopped automatically",
                Message = $"Your timer started {TimeZoneInfo.ConvertTimeFromUtc(entry.StartTime, tz):dd.MM.yyyy HH:mm} ran past the end of the business day " +
                          "and was stopped. It does not count until you correct its end time.",
                Type = "FailureEntry"
            });
            closed++;
        }

        if (closed > 0) await context.SaveChangesAsync(ct);
        return closed;
    }

    /// <summary>
    /// The first business day end (local <paramref name="dayEnd"/> in <paramref name="tz"/>)
    /// strictly after <paramref name="startUtc"/>, as UTC. E.g. day end 04:00: a 22:00 start
    /// ends at 04:00 the next morning, a 02:00 start at 04:00 the same morning.
    /// </summary>
    public static DateTime BusinessDayEndUtc(DateTime startUtc, TimeOnly dayEnd, TimeZoneInfo tz)
    {
        var startLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), tz);
        var endLocal = DateOnly.FromDateTime(startLocal).ToDateTime(dayEnd);
        if (endLocal <= startLocal) endLocal = endLocal.AddDays(1);

        // A day end inside a DST gap (e.g. 02:30 on spring-forward day) does not exist; use the next valid time
        while (tz.IsInvalidTime(endLocal)) endLocal = endLocal.AddMinutes(30);

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified), tz);
    }

    public static TimeZoneInfo ResolveTimeZone(string? id) => OrgClock.ResolveTimeZone(id);
}
