namespace TimeTracking.Api.Models;

/// <summary>
/// One device (browser profile) that receives Web Push notifications for a user (issue #67).
/// <see cref="Endpoint"/> is the push service's capability URL; <see cref="P256dh"/> and
/// <see cref="Auth"/> are the browser's encryption keys. Row-level security: owner only.
/// </summary>
public class PushSubscription
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public required string Endpoint { get; set; }
    public required string P256dh { get; set; }
    public required string Auth { get; set; }
    /// <summary>"de" or "en": the language the device's notifications are written in.</summary>
    public string Locale { get; set; } = "de";
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Last successful delivery (or re-registration from the app).</summary>
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}

/// <summary>Push categories a user can switch off (stored as <see cref="User.PushMutedCategories"/>).</summary>
[Flags]
public enum PushCategory
{
    None = 0,
    /// <summary>Timer reminder before the business day end, failure entries.</summary>
    Reminders = 1,
    /// <summary>New requests to approve, decisions on own requests.</summary>
    Requests = 2,
    /// <summary>Core working hours violations.</summary>
    CoreHours = 4,
    /// <summary>Organization rules changed.</summary>
    Organization = 8
}
