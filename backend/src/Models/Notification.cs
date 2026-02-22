namespace TimeTracking.Api.Models;

public class Notification
{
    public int Id { get; set; }
    
    /// <summary>
    /// The user who receives this notification.
    /// </summary>
    public int UserId { get; set; }
    
    /// <summary>
    /// The organization this notification is related to (optional).
    /// </summary>
    public int? OrganizationId { get; set; }
    
    /// <summary>
    /// Short title of the notification.
    /// </summary>
    public required string Title { get; set; }
    
    /// <summary>
    /// Body text of the notification.
    /// </summary>
    public required string Message { get; set; }
    
    /// <summary>
    /// Notification type for categorization (e.g., "SettingsChanged", "MemberJoined").
    /// </summary>
    public required string Type { get; set; }
    
    /// <summary>
    /// Whether the user has read/dismissed this notification.
    /// </summary>
    public bool IsRead { get; set; } = false;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation
    public User User { get; set; } = null!;
    public Organization? Organization { get; set; }
}
