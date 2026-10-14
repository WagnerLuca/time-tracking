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
    /// <summary>Whether group admins of this group may add/remove regular members.</summary>
    bool GroupAdminCanManageMembers
);

public record GroupDetailResponse(
    int Id,
    int OrganizationId,
    string Name,
    string? Description,
    string? Color,
    DateTime CreatedAt,
    IList<GroupMemberResponse> Members
);

public record GroupMemberResponse(
    int UserId,
    string FirstName,
    string LastName,
    // Null when the caller may not see it (member visibility matrix)
    string? Email,
    string? ProfileImageUrl,
    string Role,
    DateTime JoinedAt
);

public record CreateGroupRequest(
    string Name,
    string? Description,
    string? Color
);

public record UpdateGroupRequest(
    string Name,
    string? Description,
    string? Color
);

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
    // Permission overrides (null = inherited from org)
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
    string? CoreHoursEnd = null
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
    string? CoreHoursEnd = null
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
