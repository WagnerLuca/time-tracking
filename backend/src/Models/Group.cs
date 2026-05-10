using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

public enum GroupRole
{
    Member = 0,
    Admin = 1
}

public class Group
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Permission overrides (null = inherit from organization)
    public RuleMode? EditPastEntriesMode { get; set; }
    public RuleMode? EditPauseMode { get; set; }
    public RuleMode? CsvImportMode { get; set; }
    public RuleMode? WorkScheduleChangeMode { get; set; }
    public RuleMode? InitialOvertimeMode { get; set; }
    public VisibilityMode? VacationVisibility { get; set; }
    public VisibilityMode? SickDayVisibility { get; set; }
    public VisibilityMode? TimeEntryVisibility { get; set; }

    // Group Admin capability flags (null = false / disabled)
    public bool? GroupAdminCanApproveAbsences { get; set; }
    public bool? GroupAdminCanApproveTimeEdits { get; set; }
    public bool? GroupAdminCanApproveScheduleChanges { get; set; }
    public bool? GroupAdminCanManageMembers { get; set; }
    public bool? GroupAdminCanViewAllTimeEntries { get; set; }
    public bool? GroupAdminCanExportData { get; set; }

    // Navigation properties
    [JsonIgnore]
    public Organization Organization { get; set; } = null!;

    [JsonIgnore]
    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
}

public class GroupMember
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int UserId { get; set; }
    public GroupRole Role { get; set; } = GroupRole.Member;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [JsonIgnore]
    public Group Group { get; set; } = null!;

    [JsonIgnore]
    public User User { get; set; } = null!;
}
