namespace TimeTracking.Api.Models.Dtos;

public record GroupResponse(
    int Id,
    int OrganizationId,
    string Name,
    string? Description,
    string? Color,
    DateTime CreatedAt,
    int MemberCount,
    bool IsGroupAdmin
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
    string Email,
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
    string? VacationVisibility,
    string? SickDayVisibility,
    string? TimeEntryVisibility,
    // Group Admin capability flags
    bool? GroupAdminCanApproveAbsences,
    bool? GroupAdminCanApproveTimeEdits,
    bool? GroupAdminCanApproveScheduleChanges,
    bool? GroupAdminCanManageMembers,
    bool? GroupAdminCanViewAllTimeEntries,
    bool? GroupAdminCanExportData
);

public record UpdateGroupPermissionsRequest(
    RuleMode? EditPastEntriesMode,
    RuleMode? EditPauseMode,
    RuleMode? CsvImportMode,
    RuleMode? WorkScheduleChangeMode,
    RuleMode? InitialOvertimeMode,
    RuleMode? AbsenceSelfServiceMode,
    VisibilityMode? VacationVisibility,
    VisibilityMode? SickDayVisibility,
    VisibilityMode? TimeEntryVisibility,
    bool? GroupAdminCanApproveAbsences,
    bool? GroupAdminCanApproveTimeEdits,
    bool? GroupAdminCanApproveScheduleChanges,
    bool? GroupAdminCanManageMembers,
    bool? GroupAdminCanViewAllTimeEntries,
    bool? GroupAdminCanExportData
);

// Resolved effective permissions for a user (combining org + group)
public record EffectivePermissionsResponse(
    string EditPastEntriesMode,
    string EditPauseMode,
    string CsvImportMode,
    string WorkScheduleChangeMode,
    string InitialOvertimeMode,
    string AbsenceSelfServiceMode,
    string VacationVisibility,
    string SickDayVisibility,
    string TimeEntryVisibility,
    bool CanApproveAbsences,
    bool CanApproveTimeEdits,
    bool CanApproveScheduleChanges,
    bool CanManageGroupMembers,
    bool CanViewAllTimeEntries,
    bool CanExportData
);
