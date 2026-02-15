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
    
    /// <summary>
    /// When true, automatic pause deductions are applied based on PauseRules
    /// </summary>
    public bool AutoPauseEnabled { get; set; } = false;
    
    /// <summary>
    /// When true, members can edit their past time entries
    /// </summary>
    public bool AllowEditPastEntries { get; set; } = false;
    
    // Navigation properties
    [JsonIgnore]
    public ICollection<UserOrganization> UserOrganizations { get; set; } = new List<UserOrganization>();
    
    [JsonIgnore]
    public ICollection<PauseRule> PauseRules { get; set; } = new List<PauseRule>();
}
