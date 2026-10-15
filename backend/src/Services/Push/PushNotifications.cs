using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>
/// One push for one user, in both languages (the device's locale picks one). Short texts only,
/// never more than the in-app notification shows; <see cref="Url"/> is the app path opened on click.
/// </summary>
public sealed record PushMessage(int UserId, PushCategory Category, string Tag, string Url,
    string TitleDe, string BodyDe, string TitleEn, string BodyEn);

/// <summary>
/// Pushes waiting for <see cref="PushDispatcher"/>. Filled after a successful save (see
/// <c>TimeTrackingDbContext.SaveChangesAsync</c>) and by <c>RequestService</c> for new requests,
/// so a slow push service never delays a request.
/// </summary>
public sealed class PushQueue
{
    private readonly Channel<PushMessage> _channel = Channel.CreateUnbounded<PushMessage>();
    public ChannelReader<PushMessage> Reader => _channel.Reader;

    public void Enqueue(IEnumerable<PushMessage> messages)
    {
        foreach (var m in messages) _channel.Writer.TryWrite(m);
    }
}

/// <summary>The push texts of each in-app notification type (German + English).</summary>
public static partial class PushTexts
{
    [GeneratedRegex(@"(\d{2})\.(\d{2})\.(\d{4})")] private static partial Regex DateRx();
    [GeneratedRegex(@"\b(\d{2}:\d{2})\b")] private static partial Regex TimeRx();
    [GeneratedRegex(@"^(.+?) did not keep the core working hours")] private static partial Regex NameRx();

    public static PushMessage From(Notification n)
    {
        var date = DateRx().Match(n.Message);
        var dateKey = date.Success ? $"{date.Groups[3]}-{date.Groups[2]}-{date.Groups[1]}" : null;
        var tag = $"{n.Type}-{n.RelatedEntityId}";
        switch (n.Type)
        {
            case "FailureEntry":
                return new(n.UserId, PushCategory.Reminders, tag,
                    dateKey != null ? $"/history?date={dateKey}&entry={n.RelatedEntityId}" : "/history",
                    "Timer automatisch gestoppt", "Dein Timer lief über das Ende des Geschäftstags und zählt erst nach einer Korrektur.",
                    "Timer stopped automatically", "Your timer ran past the end of the business day and does not count until you correct it.");
            case "TimerReminder":
                var time = TimeRx().Match(n.Message);
                return new(n.UserId, PushCategory.Reminders, tag, "/time",
                    "Dein Timer läuft noch", time.Success ? $"Er wird um {time.Value} automatisch gestoppt (Ende des Geschäftstags)." : "Er wird zum Ende des Geschäftstags automatisch gestoppt.",
                    "Your timer is still running", time.Success ? $"It will be stopped automatically at {time.Value} (end of the business day)." : "It will be stopped automatically at the end of the business day.");
            case "CoreHoursViolation":
                var shown = date.Success ? date.Value : "";
                var own = n.RelatedEntityId == null || n.RelatedEntityId == n.UserId;
                var name = NameRx().Match(n.Message).Groups[1].Value;
                return own
                    ? new(n.UserId, PushCategory.CoreHours, tag, dateKey != null ? $"/history?date={dateKey}" : "/history",
                        "Kernarbeitszeit nicht eingehalten", $"Am {shown} wurde die Kernarbeitszeit nicht eingehalten.",
                        "Core working hours missed", $"The core working hours on {shown} were not kept.")
                    : new(n.UserId, PushCategory.CoreHours, tag, "/inbox",
                        "Kernarbeitszeit nicht eingehalten", $"{name} hat am {shown} die Kernarbeitszeit nicht eingehalten.",
                        "Core working hours missed", $"{name} did not keep the core working hours on {shown}.");
            case "RequestAccepted":
                return new(n.UserId, PushCategory.Requests, tag, "/inbox",
                    "Anfrage angenommen", "Deine Anfrage wurde angenommen.", "Request accepted", "Your request was accepted.");
            case "RequestDeclined":
                return new(n.UserId, PushCategory.Requests, tag, "/inbox",
                    "Anfrage abgelehnt", "Deine Anfrage wurde abgelehnt.", "Request declined", "Your request was declined.");
            case "SettingsChanged":
                return new(n.UserId, PushCategory.Organization, tag, "/inbox",
                    "Regeln aktualisiert", "Ein Administrator hat die Regeln deiner Organisation geändert.",
                    "Rules updated", "An administrator changed the rules of your organization.");
            default:
                return new(n.UserId, PushCategory.Organization, tag, "/inbox", n.Title, n.Message, n.Title, n.Message);
        }
    }

    /// <summary>A new request someone may decide (the inbox shows the requester and the type too).</summary>
    public static PushMessage NewRequest(int approverId, int requestId, string requester, RequestType type)
    {
        var (de, en) = NotificationMails.Label(type);
        return new(approverId, PushCategory.Requests, $"NewRequest-{requestId}", "/inbox",
            "Neue Anfrage", $"{requester}: {de}", "New request", $"{requester}: {en}");
    }
}

public enum PushSendResult { Sent, Gone, Failed }

/// <summary>Delivers one encrypted push (<see cref="WebPushSender"/>; tests use a fake).</summary>
public interface IPushSender
{
    /// <summary>VAPID public key (base64url) the browser subscribes with; null = push is off.</summary>
    string? PublicKey { get; }
    Task<PushSendResult> SendAsync(PushSubscription subscription, string payload, CancellationToken ct);
}

/// <summary>
/// Sends what <see cref="PushQueue"/> collects. Runs outside a request, so it is trusted system
/// work (RLS bypass), like the other hosted services.
/// </summary>
public sealed class PushDispatcher(PushQueue queue, IServiceScopeFactory scopes, ILogger<PushDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await queue.Reader.WaitToReadAsync(stoppingToken))
        {
            var batch = new List<PushMessage>();
            while (batch.Count < 500 && queue.Reader.TryRead(out var m)) batch.Add(m);
            try
            {
                using var scope = scopes.CreateScope();
                await DeliverAsync(scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>(),
                    scope.ServiceProvider.GetRequiredService<IPushSender>(), batch, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error while sending {Count} push notifications", batch.Count);
            }
        }
    }

    /// <summary>
    /// Sends each message to every device of its (active) user unless the user muted its
    /// category; deletes devices the push service reports gone (404/410). Returns pushes sent.
    /// </summary>
    public static async Task<int> DeliverAsync(TimeTrackingDbContext db, IPushSender sender, IReadOnlyList<PushMessage> messages, CancellationToken ct = default)
    {
        if (sender.PublicKey == null || messages.Count == 0) return 0;
        var userIds = messages.Select(m => m.UserId).Distinct().ToList();
        var muted = await db.Users.Where(u => u.IsActive && userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.PushMutedCategories, ct);
        var devices = (await db.PushSubscriptions.Where(s => userIds.Contains(s.UserId)).ToListAsync(ct))
            .ToLookup(s => s.UserId);

        var sent = 0;
        var gone = new HashSet<PushSubscription>();
        // ponytail: one push at a time; parallelise if an organization's notices ever take long
        foreach (var m in messages)
        {
            if (!muted.TryGetValue(m.UserId, out var off) || (off & m.Category) != 0) continue;
            foreach (var device in devices[m.UserId].Where(d => !gone.Contains(d)))
            {
                var en = device.Locale == "en";
                var payload = JsonSerializer.Serialize(new
                {
                    title = en ? m.TitleEn : m.TitleDe,
                    body = en ? m.BodyEn : m.BodyDe,
                    url = m.Url,
                    tag = m.Tag
                });
                switch (await sender.SendAsync(device, payload, ct))
                {
                    case PushSendResult.Sent:
                        device.LastUsedAt = DateTime.UtcNow;
                        sent++;
                        break;
                    case PushSendResult.Gone:
                        gone.Add(device);
                        db.PushSubscriptions.Remove(device);
                        break;
                }
            }
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }
}
