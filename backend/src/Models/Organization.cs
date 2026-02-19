using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

/// <summary>
/// Tri-state rule mode for organization settings.
/// Disabled = feature off entirely.
/// RequiresApproval = user must request, admin approves.
/// Allowed = user can do it themselves.
/// </summary>
public enum RuleMode
{
    Disabled = 0,
    RequiresApproval = 1,
    Allowed = 2
}

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
    public bool AutoPauseEnabled { get; set; } = true;
    
    /// <summary>
    /// Controls whether members can edit their past time entries.
    /// Allowed = user can edit freely.
    /// RequiresApproval = user must request and admin approves.
    /// Disabled = editing past entries is not available.
    /// </summary>
    public RuleMode EditPastEntriesMode { get; set; } = RuleMode.Allowed;
    
    /// <summary>
    /// Controls whether members can override the auto-deducted pause duration.
    /// Allowed = user can edit freely.
    /// RequiresApproval = user must request and admin approves.
    /// Disabled = pause editing is not available.
    /// </summary>
    public RuleMode EditPauseMode { get; set; } = RuleMode.Allowed;
    
    /// <summary>
    /// Controls whether members can set their own initial overtime balance.
    /// Allowed = user can set freely.
    /// RequiresApproval = user must request and admin approves.
    /// Disabled = initial overtime is not available.
    /// </summary>
    public RuleMode InitialOvertimeMode { get; set; } = RuleMode.Allowed;
    
    /// <summary>
    /// Controls how users can join this organization.
    /// Allowed = anyone can join freely.
    /// RequiresApproval = user submits a join request that admin must approve.
    /// Disabled = only admins can manually add members.
    /// </summary>
    public RuleMode JoinPolicy { get; set; } = RuleMode.RequiresApproval;
    
    /// <summary>
    /// Controls whether members can change their own work schedule periods.
    /// Allowed = user can add/edit schedule periods freely.
    /// RequiresApproval = user must request and admin approves.
    /// Disabled = only admins can manage schedule periods.
    /// </summary>
    public RuleMode WorkScheduleChangeMode { get; set; } = RuleMode.Allowed;
    
    // Navigation properties
    [JsonIgnore]
    public ICollection<UserOrganization> UserOrganizations { get; set; } = new List<UserOrganization>();
    
    [JsonIgnore]
    public ICollection<PauseRule> PauseRules { get; set; } = new List<PauseRule>();
    
    [JsonIgnore]
    public ICollection<Holiday> Holidays { get; set; } = new List<Holiday>();
}
