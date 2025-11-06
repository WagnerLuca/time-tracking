using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

public class Organization
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string Slug { get; set; }  // URL-friendly identifier
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTime UpdatedAt { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    // Settings
    public string? LogoUrl { get; set; }
    public string? Website { get; set; }
    
    // Navigation properties
    [JsonIgnore]
    public ICollection<UserOrganization> UserOrganizations { get; set; } = new List<UserOrganization>();
}
