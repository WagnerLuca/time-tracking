namespace TimeTracking.Api.Models.Dtos;

/// <summary>An in-app notification for the current user.</summary>
public record NotificationResponse
{
    public int Id { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public required string Type { get; init; }
    public bool IsRead { get; init; }
    public DateTime CreatedAt { get; init; }
    public int? OrganizationId { get; init; }
    /// <summary>Time entry id (FailureEntry, TimerReminder) or request id (RequestAccepted, RequestDeclined) for deep links.</summary>
    public int? RelatedEntityId { get; init; }
}

/// <summary>Number of unread notifications.</summary>
public record UnreadCountResponse
{
    public int Count { get; init; }
}

/// <summary>Number of request notifications marked as seen.</summary>
public record MarkedSeenResponse
{
    public int Marked { get; init; }
}
