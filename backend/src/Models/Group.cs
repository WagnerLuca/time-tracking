using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

public enum GroupRole
{
    Member = 0,
    Admin = 1
}

/// <summary>A group's core working hours override (null on the group = inherit from the organization).</summary>
public enum CoreHoursMode
{
    /// <summary>No core hours for members of this group.</summary>
    Off = 0,
    /// <summary>The group's own <see cref="Group.CoreHoursStart"/>–<see cref="Group.CoreHoursEnd"/>.</summary>
    Own = 1
}

public class Group
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }
    /// <summary>The parent group (same organization), null = top level. Every setting left on
    /// "inherit" takes the parent's effective value, see <c>GroupTree</c>. At most 3 levels.</summary>
    public int? ParentGroupId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Permission overrides (null = inherit from the parent group, at the top from the organization)
    public RuleMode? EditPastEntriesMode { get; set; }
    public RuleMode? EditPauseMode { get; set; }
    public RuleMode? CsvImportMode { get; set; }
    public RuleMode? WorkScheduleChangeMode { get; set; }
    public RuleMode? InitialOvertimeMode { get; set; }
    public RuleMode? AbsenceSelfServiceMode { get; set; }
    // Member visibility matrix overrides, one allow and one deny set per column (a kind in
    // neither = inherit from the organization). See MemberVisibility.
    public MemberInfo VisibilityAllowInGroups { get; set; }
    public MemberInfo VisibilityDenyInGroups { get; set; }
    public MemberInfo VisibilityAllowOutsideGroups { get; set; }
    public MemberInfo VisibilityDenyOutsideGroups { get; set; }
    public CoreHoursMode? CoreHoursMode { get; set; }
    /// <summary>Only used with <see cref="Models.CoreHoursMode.Own"/>.</summary>
    public TimeOnly? CoreHoursStart { get; set; }
    public TimeOnly? CoreHoursEnd { get; set; }

    // Group Admin capability flags (null = inherit from the parent group; at the top level null = disabled)
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
    public Group? ParentGroup { get; set; }

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
