using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>The signed-in user's push devices and categories (issue #67).</summary>
public class PushService(TimeTrackingDbContext db, IPushSender sender)
{
    public const int MaxDevicesPerUser = 10;

    /// <summary>
    /// Push services of the browsers we support. The server POSTs to the endpoint a client hands
    /// in, so anything else is refused (no requests to arbitrary hosts).
    /// </summary>
    private static readonly string[] PushHosts =
        ["fcm.googleapis.com", "android.googleapis.com", ".push.services.mozilla.com", ".notify.windows.com", ".push.apple.com"];

    public static bool IsPushServiceEndpoint(string endpoint) =>
        endpoint.Length <= 1000
        && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort
        && PushHosts.Any(h => h.StartsWith('.') ? uri.Host.EndsWith(h, StringComparison.OrdinalIgnoreCase) : uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase));

    private static int Base64UrlLength(string value)
    {
        try { return value.Length > 200 ? -1 : WebEncoders.Base64UrlDecode(value).Length; }
        catch (FormatException) { return -1; }
    }

    public ServiceResult<PushPublicKeyResponse> GetPublicKey() => ServiceResult.Ok(new PushPublicKeyResponse(sender.PublicKey));

    /// <summary>Registers (or refreshes) this device for the user; at most <see cref="MaxDevicesPerUser"/>, the least recently used goes.</summary>
    public async Task<ServiceResult> SubscribeAsync(int userId, PushSubscribeRequest request, string? userAgent)
    {
        if (sender.PublicKey == null)
            return ServiceResult.BadRequest("Push notifications are not configured on this server.");
        if (!IsPushServiceEndpoint(request.Endpoint ?? ""))
            return ServiceResult.BadRequest("This push service is not supported.");
        if (Base64UrlLength(request.P256dh ?? "") != 65 || Base64UrlLength(request.Auth ?? "") != 16)
            return ServiceResult.BadRequest("The push subscription is invalid.");

        var locale = request.Locale == "en" ? "en" : "de";
        var agent = userAgent is { Length: > 300 } ? userAgent[..300] : userAgent;
        // RLS hides other users' rows in Postgres (the unique index then refuses the insert below)
        var existing = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == request.Endpoint);
        if (existing != null && existing.UserId != userId)
            return ServiceResult.Conflict("This device receives notifications for another account.");
        if (existing == null)
        {
            var devices = await db.PushSubscriptions.Where(s => s.UserId == userId).OrderBy(s => s.LastUsedAt).ToListAsync();
            db.PushSubscriptions.RemoveRange(devices.Take(Math.Max(0, devices.Count - MaxDevicesPerUser + 1)));
            existing = new PushSubscription { UserId = userId, Endpoint = request.Endpoint!, P256dh = "", Auth = "" };
            db.PushSubscriptions.Add(existing);
        }
        existing.P256dh = request.P256dh!;
        existing.Auth = request.Auth!;
        existing.Locale = locale;
        existing.UserAgent = agent;
        existing.LastUsedAt = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return ServiceResult.Conflict("This device receives notifications for another account.");
        }
        return ServiceResult.Ok();
    }

    /// <summary>Removes this device (no error when it is not registered).</summary>
    public async Task<ServiceResult> UnsubscribeAsync(int userId, string endpoint)
    {
        db.PushSubscriptions.RemoveRange(await db.PushSubscriptions.Where(s => s.UserId == userId && s.Endpoint == endpoint).ToListAsync());
        await db.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<PushSettingsDto>> GetSettingsAsync(int userId)
    {
        var muted = await db.Users.Where(u => u.Id == userId).Select(u => u.PushMutedCategories).FirstOrDefaultAsync();
        return ServiceResult.Ok(ToDto(muted));
    }

    public async Task<ServiceResult<PushSettingsDto>> UpdateSettingsAsync(int userId, PushSettingsDto settings)
    {
        var user = await db.Users.FindAsync(userId);
        if (user == null) return ServiceResult.NotFound<PushSettingsDto>("User not found");
        user.PushMutedCategories =
            (settings.Reminders ? 0 : PushCategory.Reminders) | (settings.Requests ? 0 : PushCategory.Requests)
            | (settings.CoreHours ? 0 : PushCategory.CoreHours) | (settings.Organization ? 0 : PushCategory.Organization);
        await db.SaveChangesAsync();
        return ServiceResult.Ok(ToDto(user.PushMutedCategories));
    }

    private static PushSettingsDto ToDto(PushCategory muted) => new(
        !muted.HasFlag(PushCategory.Reminders), !muted.HasFlag(PushCategory.Requests),
        !muted.HasFlag(PushCategory.CoreHours), !muted.HasFlag(PushCategory.Organization));
}
