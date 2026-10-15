using System.Text.Json.Serialization;
using TimeTracking.Api.Infrastructure;

namespace TimeTracking.Api.Models.Dtos;

public record GroupResponse(
    int Id,
    int OrganizationId,
    string Name,
    string? Description,
    string? Color,
    DateTime CreatedAt,
    int MemberCount,
    bool IsGroupAdmin,
    /// <summary>Whether group admins of this group may add/remove regular members (effective, inherited from the parent when not set).</summary>
    bool GroupAdminCanManageMembers,
    /// <summary>The parent group (sub-groups, at most 3 levels), null = top level.</summary>
    int? ParentGroupId = null,
    /// <summary>Whether the caller may add/remove members and create sub-groups here (org admin, or admin of this group or an ancestor with member management).</summary>
    bool CanManageMembers = false
);

public record GroupDetailResponse(
    int Id,
    int OrganizationId,
    string Name,
    string? Description,
    string? Color,
    DateTime CreatedAt,
    // Direct members first, then the members of sub-groups (ViaGroupName set)
    IList<GroupMemberResponse> Members,
    int? ParentGroupId = null
);

public record GroupMemberResponse(
    int UserId,
    string FirstName,
    string LastName,
    // Null when the caller may not see it (member visibility matrix)
    string? Email,
    string? ProfileImageUrl,
    string Role,
    DateTime JoinedAt,
    // Set for a member of a sub-group (inherited membership): the sub-group they are in
    string? ViaGroupName = null
);

public record CreateGroupRequest(
    string Name,
    string? Description,
    string? Color,
    // Parent group (same organization), null = top level
    int? ParentGroupId = null
);

public record UpdateGroupRequest(
    string Name,
    string? Description,
    string? Color,
    // New parent group (same organization); a group cannot move into its own subtree. Left out (null) the
    // parent stays — moving to the top level is the explicit MoveToTopLevel = true (then without ParentGroupId).
    int? ParentGroupId = null,
    bool MoveToTopLevel = false
);

/// <summary>What happens to the sub-groups of a group that is deleted.</summary>
public enum SubGroupAction
{
    /// <summary>They move up one level (to the deleted group's parent).</summary>
    MoveUp = 0,
    /// <summary>They are deleted with it (the whole subtree).</summary>
    Delete = 1
}

public record AddGroupMemberRequest(
    int UserId,
    string Role = "Member"
);

public record UpdateGroupMemberRoleRequest(
    string Role
);

public record GroupPermissionsResponse(
    int GroupId,
    string GroupName,
    // Permission overrides (null = inherited from the parent group, at the top from the org)
    string? EditPastEntriesMode,
    string? EditPauseMode,
    string? CsvImportMode,
    string? WorkScheduleChangeMode,
    string? InitialOvertimeMode,
    string? AbsenceSelfServiceMode,
    // Member visibility matrix overrides per kind: true = allow, false = deny, null = inherit
    IList<GroupVisibilityRuleResponse> Visibility,
    // Group Admin capability flags
    bool? GroupAdminCanApproveAbsences,
    bool? GroupAdminCanApproveTimeEdits,
    bool? GroupAdminCanApproveScheduleChanges,
    bool? GroupAdminCanManageMembers,
    bool? GroupAdminCanViewAllTimeEntries,
    bool? GroupAdminCanExportData,
    // Core working hours override: null = inherit, "Off" or "Own" (with start/end as "HH:mm").
    string? CoreHoursMode = null,
    string? CoreHoursStart = null,
    string? CoreHoursEnd = null,
    int? ParentGroupId = null
);

public record UpdateGroupPermissionsRequest(
    [property: JsonConverter(typeof(EnumOverrideConverter<RuleMode>))] RuleMode? EditPastEntriesMode,
    [property: JsonConverter(typeof(EnumOverrideConverter<RuleMode>))] RuleMode? EditPauseMode,
    [property: JsonConverter(typeof(EnumOverrideConverter<RuleMode>))] RuleMode? CsvImportMode,
    [property: JsonConverter(typeof(EnumOverrideConverter<RuleMode>))] RuleMode? WorkScheduleChangeMode,
    [property: JsonConverter(typeof(EnumOverrideConverter<RuleMode>))] RuleMode? InitialOvertimeMode,
    [property: JsonConverter(typeof(EnumOverrideConverter<RuleMode>))] RuleMode? AbsenceSelfServiceMode,
    // Rows of the visibility matrix to change; per cell true = allow, false = deny, null = inherit
    IList<GroupVisibilityRuleRequest>? Visibility,
    bool? GroupAdminCanApproveAbsences,
    bool? GroupAdminCanApproveTimeEdits,
    bool? GroupAdminCanApproveScheduleChanges,
    bool? GroupAdminCanManageMembers,
    bool? GroupAdminCanViewAllTimeEntries,
    bool? GroupAdminCanExportData,
    // Core working hours override; -1 = inherit from the organization. Own needs start and end.
    [property: JsonConverter(typeof(EnumOverrideConverter<CoreHoursMode>))] CoreHoursMode? CoreHoursMode = null,
    // "HH:mm"; only stored with CoreHoursMode Own.
    string? CoreHoursStart = null,
    string? CoreHoursEnd = null,
    // Capability flags to reset to "inherit" (from the parent group; at the top: disabled), by
    // property name, e.g. ["groupAdminCanApproveAbsences"]
    IList<string>? InheritCapabilities = null
);

// Resolved effective permissions for a user (combining org + group)
public record EffectivePermissionsResponse(
    string EditPastEntriesMode,
    string EditPauseMode,
    string CsvImportMode,
    string WorkScheduleChangeMode,
    string InitialOvertimeMode,
    string AbsenceSelfServiceMode,
    // What normal members see about this person (their groups' overrides applied)
    IList<VisibilityRuleResponse> Visibility,
    bool CanApproveAbsences,
    bool CanApproveTimeEdits,
    bool CanApproveScheduleChanges,
    bool CanManageGroupMembers,
    bool CanViewAllTimeEntries,
    bool CanExportData
);

/// <summary>A group's override of one matrix row: true = allow, false = deny, null = inherit from the organization.</summary>
public record GroupVisibilityRuleResponse(string Kind, bool? InGroups, bool? OutsideGroups);

/// <summary>Sets a group's override of one matrix row (both cells; null = inherit). Kind is a MemberInfo name.</summary>
public record GroupVisibilityRuleRequest(string Kind, bool? InGroups, bool? OutsideGroups);
