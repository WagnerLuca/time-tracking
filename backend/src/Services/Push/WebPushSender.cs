using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.AspNetCore.WebUtilities;
using WebPushMessage = Lib.Net.Http.WebPush.PushMessage;
using WebPushSubscription = Lib.Net.Http.WebPush.PushSubscription;

namespace TimeTracking.Api.Services;

/// <summary>VAPID keys (<c>WebPush:PublicKey/PrivateKey/Subject</c>, env <c>WebPush__…</c>).</summary>
public sealed class WebPushSettings
{
    /// <summary>Uncompressed P-256 public key, base64url (65 bytes).</summary>
    public string? PublicKey { get; set; }
    /// <summary>P-256 private key, base64url (32 bytes). Never commit it.</summary>
    public string? PrivateKey { get; set; }
    /// <summary>Contact for the push services: <c>mailto:…</c> or an https URL.</summary>
    public string? Subject { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}

/// <summary>Creates VAPID key pairs (<c>dotnet TimeTracking.Api.dll generate-vapid-keys</c>, dev startup).</summary>
public static class VapidKeys
{
    public static (string PublicKey, string PrivateKey) Generate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(true);
        return (WebEncoders.Base64UrlEncode([0x04, .. p.Q.X!, .. p.Q.Y!]), WebEncoders.Base64UrlEncode(p.D!));
    }

    /// <summary>
    /// Development only: keys from <paramref name="path"/> (gitignored), created on first start,
    /// so a dev backend can push without configuration and keeps its keys across restarts
    /// (when the folder is not writable, the keys last for this run only).
    /// </summary>
    public static (string PublicKey, string PrivateKey) LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
            return (saved["publicKey"], saved["privateKey"]);
        }
        var keys = Generate();
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["publicKey"] = keys.PublicKey, ["privateKey"] = keys.PrivateKey
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only content root (the dev container): keys for this run only
        }
        return keys;
    }
}

/// <summary>
/// Web Push via Lib.Net.Http.WebPush (VAPID, aes128gcm). 404/410 from the push service means
/// the browser dropped the subscription; anything else is logged and retried with the next push.
/// </summary>
public sealed class WebPushSender : IPushSender
{
    private readonly PushServiceClient? _client;
    private readonly ILogger<WebPushSender> _logger;

    public WebPushSender(HttpClient http, WebPushSettings settings, ILogger<WebPushSender> logger)
    {
        _logger = logger;
        if (!settings.IsConfigured) return;
        PublicKey = settings.PublicKey;
        _client = new PushServiceClient(http)
        {
            DefaultAuthentication = new VapidAuthentication(settings.PublicKey, settings.PrivateKey) { Subject = settings.Subject },
            DefaultTimeToLive = (int)TimeSpan.FromHours(12).TotalSeconds
        };
    }

    public string? PublicKey { get; }

    public async Task<PushSendResult> SendAsync(Models.PushSubscription subscription, string payload, CancellationToken ct)
    {
        if (_client == null) return PushSendResult.Failed;
        var target = new WebPushSubscription { Endpoint = subscription.Endpoint };
        target.SetKey(PushEncryptionKeyName.P256DH, subscription.P256dh);
        target.SetKey(PushEncryptionKeyName.Auth, subscription.Auth);
        try
        {
            await _client.RequestPushMessageDeliveryAsync(target, new WebPushMessage(payload) { Urgency = PushMessageUrgency.High }, ct);
            return PushSendResult.Sent;
        }
        catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return PushSendResult.Gone;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Push to {Host} failed", new Uri(subscription.Endpoint).Host);
            return PushSendResult.Failed;
        }
    }
}
