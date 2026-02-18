using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

public enum RequestStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2
}

public enum RequestType
{
    JoinOrganization = 0,
    EditPastEntry = 1,
    EditPause = 2,
    SetInitialOvertime = 3
}

public class OrgRequest
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    public int OrganizationId { get; set; }
    
    public RequestType Type { get; set; } = RequestType.JoinOrganization;
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    
    public string? Message { get; set; }
    
    /// <summary>
    /// Optional reference to a related entity (e.g. TimeEntry ID for edit requests)
    /// </summary>
    public int? RelatedEntityId { get; set; }
    
    /// <summary>
    /// Optional JSON data for the request payload (e.g. new overtime value, new pause minutes)
    /// </summary>
    public string? RequestData { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }
    public int? RespondedByUserId { get; set; }
    
    /// <summary>
    /// Tracks when the user was notified of the response. Null = not yet seen.
    /// </summary>
    public DateTime? UserNotifiedAt { get; set; }
    
    // Navigation properties
    [JsonIgnore]
    public User User { get; set; } = null!;
    
    [JsonIgnore]
    public Organization Organization { get; set; } = null!;
    
    [JsonIgnore]
    public User? RespondedByUser { get; set; }
}
