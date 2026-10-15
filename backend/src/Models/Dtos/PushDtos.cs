namespace TimeTracking.Api.Models.Dtos;

/// <summary>The VAPID public key the browser subscribes with; null when push is not configured.</summary>
public record PushPublicKeyResponse(string? PublicKey);

/// <summary>A browser's PushSubscription (<c>subscription.toJSON()</c>: endpoint + keys) and its language.</summary>
public record PushSubscribeRequest(string Endpoint, string P256dh, string Auth, string? Locale);

public record PushUnsubscribeRequest(string Endpoint);

/// <summary>Which push categories the user receives (on all of their devices).</summary>
public record PushSettingsDto(bool Reminders, bool Requests, bool CoreHours, bool Organization);
