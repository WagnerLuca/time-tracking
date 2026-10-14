using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>
/// Closes timers that run past their organization's business day end and marks them
/// as failure entries (<see cref="TimeEntry.IsFailure"/>): the member forgot to stop
/// tracking. A failure entry counts as 0 worked time until the member corrects its end
/// time, so a forgotten timer can neither run forever nor inflate dashboards and reports.
/// The same minute loop also runs the daily core working hours check (<see cref="CoreHours.CheckDueAsync"/>).
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
                var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                var frontend = scope.ServiceProvider.GetRequiredService<EmailSettings>().FrontendBaseUrl;
                var now = DateTime.UtcNow;
                var reminded = await SendDayEndRemindersAsync(context, now, email, frontend, stoppingToken);
                if (reminded > 0)
                    _logger.LogInformation("Reminded {Count} member(s) of a timer still running before business day end.", reminded);
                var closed = await CloseOverdueEntriesAsync(context, now, stoppingToken, email, frontend);
                if (closed > 0)
                    _logger.LogInformation("Auto-stopped {Count} timer(s) at business day end.", closed);
                // Same loop and RLS bypass (no HttpContext = system work) for the daily core hours check
                var missed = await CoreHours.CheckDueAsync(context, now, email, frontend, stoppingToken);
                if (missed > 0)
                    _logger.LogInformation("Notified {Count} core working hours violation(s).", missed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error while closing overdue timers.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    /// <summary>How long before the business day end a still running timer triggers a reminder.</summary>
    public static readonly TimeSpan ReminderLead = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Reminds (in-app + mail) the owner of every timer still running within <see cref="ReminderLead"/>
    /// of its business day end, once per entry. Returns the count.
    /// </summary>
    public static async Task<int> SendDayEndRemindersAsync(TimeTrackingDbContext context, DateTime nowUtc,
        IEmailSender? email = null, string? frontendBaseUrl = null, CancellationToken ct = default)
    {
        var running = await context.TimeEntries
            .Include(e => e.Organization)
            .Include(e => e.User)
            .Where(e => e.IsRunning && e.DayEndReminderSentAt == null)
            .ToListAsync(ct);

        var due = new List<(TimeEntry Entry, DateTime CutoffLocal)>();
        foreach (var entry in running)
        {
            var tz = ResolveTimeZone(entry.Organization?.TimeZone);
            var cutoff = BusinessDayEndUtc(entry.StartTime, entry.Organization?.BusinessDayEnd ?? TimeOnly.MinValue, tz);
            if (nowUtc < cutoff - ReminderLead || nowUtc >= cutoff) continue;

            entry.DayEndReminderSentAt = nowUtc;
            var cutoffLocal = TimeZoneInfo.ConvertTimeFromUtc(cutoff, tz);
            context.Notifications.Add(new Notification
            {
                UserId = entry.UserId,
                OrganizationId = entry.OrganizationId,
                Title = "Timer still running",
                Message = $"Your timer is still running. It will be stopped automatically at {cutoffLocal:HH:mm} (end of the business day) and then needs a correction.",
                Type = "TimerReminder",
                RelatedEntityId = entry.Id
            });
            due.Add((entry, cutoffLocal));
        }

        if (due.Count == 0) return 0;
        await context.SaveChangesAsync(ct);   // marked before mailing: a crash may skip a mail, never repeat one

        if (email != null)
            foreach (var (entry, cutoffLocal) in due.Where(d => d.Entry.User.IsActive))
                await NotificationMails.TrySendAsync(email, entry.User.Email,
                    "Dein Timer läuft noch / Your timer is still running",
                    $"Dein Timer läuft noch und wird um {cutoffLocal:HH:mm} automatisch gestoppt (Ende des Geschäftstags). / " +
                    $"Your timer is still running and will be stopped automatically at {cutoffLocal:HH:mm} (end of the business day).\n\n" +
                    $"{frontendBaseUrl}/time", ct: ct);
        return due.Count;
    }

    /// <summary>
    /// Stops every running entry whose business day has ended by <paramref name="nowUtc"/>,
    /// at that day end, flags it as a failure and notifies its owner (in-app, plus a mail when
    /// <paramref name="email"/> is given). Returns the count.
    /// </summary>
    public static async Task<int> CloseOverdueEntriesAsync(TimeTrackingDbContext context, DateTime nowUtc, CancellationToken ct = default,
        IEmailSender? email = null, string? frontendBaseUrl = null)
    {
        // ponytail: loads all running entries each minute; fine while running timers number in the hundreds
        var running = await context.TimeEntries
            .Include(e => e.Organization)
            .Include(e => e.User)
            .Where(e => e.IsRunning)
            .ToListAsync(ct);
        var failed = new List<TimeEntry>();

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
                Type = "FailureEntry",
                RelatedEntityId = entry.Id
            });
            failed.Add(entry);
            closed++;
        }

        if (closed > 0) await context.SaveChangesAsync(ct);

        if (email != null)
            foreach (var entry in failed.Where(e => e.User.IsActive))
            {
                var tz = ResolveTimeZone(entry.Organization?.TimeZone);
                var startLocal = TimeZoneInfo.ConvertTimeFromUtc(entry.StartTime, tz);
                await NotificationMails.TrySendAsync(email, entry.User.Email,
                    "Timer automatisch gestoppt / Timer stopped automatically",
                    $"Dein Timer vom {startLocal:dd.MM.yyyy HH:mm} lief über das Ende des Geschäftstags und wurde gestoppt. " +
                    "Er zählt erst, wenn du die Endzeit korrigierst. / " +
                    $"Your timer started {startLocal:dd.MM.yyyy HH:mm} ran past the end of the business day and was stopped. " +
                    "It does not count until you correct its end time.\n\n" +
                    $"{frontendBaseUrl}/history?date={startLocal:yyyy-MM-dd}&entry={entry.Id}", ct: ct);
            }
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
