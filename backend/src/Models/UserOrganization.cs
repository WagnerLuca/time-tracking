using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

public enum OrganizationRole
{
    Member = 0,
    Admin = 1,
    Owner = 2  // For the creator of the organization
}

public class UserOrganization
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int OrganizationId { get; set; }
    public OrganizationRole Role { get; set; } = OrganizationRole.Member;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    [JsonIgnore]
    public User User { get; set; } = null!;
    
    [JsonIgnore]
    public Organization Organization { get; set; } = null!;
}
