using System.ComponentModel.DataAnnotations;

namespace TimeTracking.Api.Models.Dtos;

/// <summary>Summary view of an organization.</summary>
public record OrganizationResponse
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Slug { get; init; }
    public string? Website { get; init; }
    public string? LogoUrl { get; init; }
    public DateTime CreatedAt { get; init; }
    /// <summary>Members the caller can see; null in the directory for organizations the caller does not belong to.</summary>
    public int? MemberCount { get; init; }
    public required string JoinPolicy { get; init; }
}

/// <summary>Detailed view of an organization including members, settings and pause rules.</summary>
public record OrganizationDetailResponse
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Slug { get; init; }
    public string? Website { get; init; }
    public string? LogoUrl { get; init; }
    public bool AutoPauseEnabled { get; init; }
    public required string EditPastEntriesMode { get; init; }
    public required string EditPauseMode { get; init; }
    public required string InitialOvertimeMode { get; init; }
    public required string JoinPolicy { get; init; }
    public required string WorkScheduleChangeMode { get; init; }
    public bool MemberTimeEntryVisibility { get; init; }
    public bool Require2fa { get; init; }
    public required string CsvImportMode { get; init; }
    public string AbsenceSelfServiceMode { get; init; } = "Allowed";
    /// <summary>The member visibility matrix (org defaults), one row per kind of information.</summary>
    public List<VisibilityRuleResponse> Visibility { get; init; } = new();
    public double DefaultVacationDays { get; init; }
    /// <summary>Business day end as local "HH:mm" in <see cref="TimeZone"/>.</summary>
    public string BusinessDayEnd { get; init; } = "00:00";
    public string TimeZone { get; init; } = "Europe/Berlin";
    /// <summary>Core working hours (see CoreHours); start/end as local "HH:mm".</summary>
    public bool CoreHoursEnabled { get; init; }
    public string CoreHoursStart { get; init; } = "09:00";
    public string CoreHoursEnd { get; init; } = "15:00";
    public int CoreHoursToleranceMinutes { get; init; } = 5;
    /// <summary>Terminal settings (docs/TERMINAL.md).</summary>
    public int TerminalDoubleScanSeconds { get; init; } = TerminalLimits.DefaultDoubleScanSeconds;
    public int TerminalOfflineHours { get; init; } = TerminalLimits.DefaultOfflineHours;
    public int TerminalUnknownCardLimit { get; init; } = TerminalLimits.DefaultUnknownCardLimit;
    public DateTime? SettingsUpdatedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public required List<OrganizationMemberResponse> Members { get; init; }
    public List<PauseRuleResponse>? PauseRules { get; init; }
}

/// <summary>A group a member belongs to, for tag display (e.g. "Frontend Admin").</summary>
public record MemberGroupResponse
{
    public int GroupId { get; init; }
    public required string Name { get; init; }
    public string? Color { get; init; }
    public bool IsGroupAdmin { get; init; }
    /// <summary>Set for an inherited membership (an ancestor of a direct group): the direct group it comes through.</summary>
    public string? ViaGroupName { get; init; }
}

/// <summary>Organization member profile and role.</summary>
public record OrganizationMemberResponse
{
    public int Id { get; init; }
    /// <summary>Null when the caller may not see it (member visibility matrix).</summary>
    public string? Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? ProfileImageUrl { get; init; }
    /// <summary>Org role; shown whenever the member is.</summary>
    public string? Role { get; init; }
    public DateTime JoinedAt { get; init; }
    public double InitialOvertimeHours { get; init; }
    /// <summary>Groups this member belongs to within the organization.</summary>
    public List<MemberGroupResponse> Groups { get; init; } = new();
    public double VacationDaysPerYear { get; init; }
    public double VacationDaysUsed { get; init; }
    /// <summary>Vacation days already taken this year (date &lt;= today).</summary>
    public double VacationDaysTaken { get; init; }
    /// <summary>Vacation days planned for the rest of this year (date &gt; today).</summary>
    public double VacationDaysPlanned { get; init; }
    public double VacationDaysRemaining { get; init; }
    /// <summary>
    /// Whether the caller may see this member's vacation balance (self, admins, or
    /// per the member visibility matrix, kind VacationBalance). When false, the vacation figures are zeroed.
    /// </summary>
    public bool VacationVisible { get; init; }
    /// <summary>Number of terminal cards; only filled in for org admins.</summary>
    public int CardCount { get; init; }
    /// <summary>
    /// The kinds of information (<see cref="MemberInfo"/> names) the caller may see about this member;
    /// the UI shows only those sections. Hidden members are not in the list at all.
    /// </summary>
    public List<string> VisibleInfo { get; init; } = new();
    /// <summary>Whether a timer is running right now; null when presence is not visible to the caller.</summary>
    public bool? IsPresent { get; init; }
    /// <summary>Personnel number for payroll; only filled in for org admins.</summary>
    public string? PersonnelNumber { get; init; }
    /// <summary>Whether the caller may add, correct and delete this member's time entries (never for themselves).</summary>
    public bool CanCorrectEntries { get; init; }
}

/// <summary>One row of the member visibility matrix: may members see this kind about people in / outside their groups.</summary>
public record VisibilityRuleResponse(string Kind, bool InGroups, bool OutsideGroups);

/// <summary>Sets one row of the organization's visibility matrix. Kind is a <see cref="MemberInfo"/> name, e.g. "Vacation".</summary>
public record VisibilityRuleRequest(string Kind, bool InGroups, bool OutsideGroups);

/// <summary>Lightweight org view from the user's perspective.</summary>
public record UserOrganizationResponse
{
    public int OrganizationId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Slug { get; init; }
    public required string Role { get; init; }
    public DateTime JoinedAt { get; init; }
    public int MemberCount { get; init; }
    public bool Require2fa { get; init; }
}

/// <summary>Request payload for creating a new organization.</summary>
public record CreateOrganizationRequest
{
    /// <summary>Display name of the organization.</summary>
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    /// <summary>Optional description.</summary>
    [MaxLength(1000)]
    public string? Description { get; init; }

    /// <summary>URL-friendly slug (lowercase alphanumeric with hyphens).</summary>
    [Required, MaxLength(100), RegularExpression(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", ErrorMessage = "Slug must be lowercase alphanumeric with hyphens only.")]
    public required string Slug { get; init; }

    [MaxLength(500), Url]
    public string? Website { get; init; }

    [MaxLength(500), Url]
    public string? LogoUrl { get; init; }
}

/// <summary>Request payload for updating an existing organization.</summary>
public record UpdateOrganizationRequest
{
    [MaxLength(200)]
    public string? Name { get; init; }

    [MaxLength(1000)]
    public string? Description { get; init; }

    [MaxLength(100), RegularExpression(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", ErrorMessage = "Slug must be lowercase alphanumeric with hyphens only.")]
    public string? Slug { get; init; }

    [MaxLength(500), Url]
    public string? Website { get; init; }

    [MaxLength(500), Url]
    public string? LogoUrl { get; init; }
}

/// <summary>Request payload for changing a member's role.</summary>
public record UpdateMemberRoleRequest
{
    public OrganizationRole Role { get; init; }
}

/// <summary>Request payload for updating org-level settings and policies.</summary>
public record UpdateOrganizationSettingsRequest
{
    public bool? AutoPauseEnabled { get; init; }
    public RuleMode? EditPastEntriesMode { get; init; }
    public RuleMode? EditPauseMode { get; init; }
    public RuleMode? InitialOvertimeMode { get; init; }
    public RuleMode? JoinPolicy { get; init; }
    public RuleMode? WorkScheduleChangeMode { get; init; }
    public bool? MemberTimeEntryVisibility { get; init; }
    public bool? Require2fa { get; init; }
    public RuleMode? CsvImportMode { get; init; }
    public RuleMode? AbsenceSelfServiceMode { get; init; }
    /// <summary>Rows of the member visibility matrix to change (others stay as they are).</summary>
    public List<VisibilityRuleRequest>? Visibility { get; init; }
    public double? DefaultVacationDays { get; init; }
    /// <summary>Business day end as local "HH:mm" (e.g. "04:00").</summary>
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Use HH:mm, e.g. 04:00.")]
    public string? BusinessDayEnd { get; init; }
    /// <summary>IANA time zone id, e.g. "Europe/Berlin".</summary>
    [MaxLength(64)]
    public string? TimeZone { get; init; }
    public bool? CoreHoursEnabled { get; init; }
    /// <summary>Core hours start as local "HH:mm"; must be before <see cref="CoreHoursEnd"/>.</summary>
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Use HH:mm, e.g. 04:00.")]
    public string? CoreHoursStart { get; init; }
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Use HH:mm, e.g. 04:00.")]
    public string? CoreHoursEnd { get; init; }
    /// <summary>Allowed late start / early leave / gap in minutes.</summary>
    [Range(0, 120)]
    public int? CoreHoursToleranceMinutes { get; init; }
    /// <summary>Terminal double-scan window in seconds (5-600).</summary>
    public int? TerminalDoubleScanSeconds { get; init; }
    /// <summary>Terminal offline window in hours (1-168).</summary>
    public int? TerminalOfflineHours { get; init; }
    /// <summary>Unknown/blocked scans per key and 10 minutes (5-200).</summary>
    public int? TerminalUnknownCardLimit { get; init; }
}

/// <summary>Per-member time tracking statistics for the admin overview.</summary>
public record MemberTimeOverviewResponse
{
    public int UserId { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    /// <summary>Email / role / weekly hours are null where the member visibility matrix hides them from the caller.</summary>
    public string? Email { get; init; }
    public string? Role { get; init; }
    public double? WeeklyWorkHours { get; init; }
    public double TotalTrackedMinutes { get; init; }
    public double NetTrackedMinutes { get; init; }
    public int EntryCount { get; init; }
    /// <summary>Unresolved failure entries in the period (excluded from the totals).</summary>
    public int FailureEntryCount { get; init; }
}

/// <summary>Request payload for setting a member's initial overtime hours.</summary>
public record SetInitialOvertimeRequest
{
    public double InitialOvertimeHours { get; init; }
}

/// <summary>Request payload for setting a member's vacation day allowance.</summary>
public record SetVacationDaysRequest
{
    [Range(0, 365)]
    public double Days { get; init; }
}

/// <summary>Sets a member's personnel number; empty or null removes it.</summary>
public record SetPersonnelNumberRequest
{
    [MaxLength(UserOrganizationLimits.PersonnelNumberMaxLength)]
    public string? PersonnelNumber { get; init; }
}

public record MemberPersonnelNumberResponse
{
    public int UserId { get; init; }
    public string? PersonnelNumber { get; init; }
}

/// <summary>Organization settings after an update (rule modes as their string names).</summary>
public record OrganizationSettingsResponse
{
    public bool AutoPauseEnabled { get; init; }
    public required string EditPastEntriesMode { get; init; }
    public required string EditPauseMode { get; init; }
    public required string InitialOvertimeMode { get; init; }
    public required string JoinPolicy { get; init; }
    public required string WorkScheduleChangeMode { get; init; }
    public bool MemberTimeEntryVisibility { get; init; }
    public bool Require2fa { get; init; }
    public required string CsvImportMode { get; init; }
    public required string AbsenceSelfServiceMode { get; init; }
    public List<VisibilityRuleResponse> Visibility { get; init; } = new();
    public required string BusinessDayEnd { get; init; }
    public required string TimeZone { get; init; }
    /// <summary>Core working hours (see CoreHours); start/end as local "HH:mm".</summary>
    public bool CoreHoursEnabled { get; init; }
    public string CoreHoursStart { get; init; } = "09:00";
    public string CoreHoursEnd { get; init; } = "15:00";
    public int CoreHoursToleranceMinutes { get; init; } = 5;
    public int TerminalDoubleScanSeconds { get; init; }
    public int TerminalOfflineHours { get; init; }
    public int TerminalUnknownCardLimit { get; init; }
}

/// <summary>A member's initial overtime after it was set.</summary>
public record MemberInitialOvertimeResponse
{
    public int UserId { get; init; }
    public double InitialOvertimeHours { get; init; }
}

/// <summary>A member's vacation day allowance after it was set.</summary>
public record MemberVacationDaysResponse
{
    public int UserId { get; init; }
    public double VacationDaysPerYear { get; init; }
}

/// <summary>The dashboard's team overview (issue #61): the members the caller leads, with balance and presence.</summary>
public record TeamOverviewResponse
{
    /// <summary>Groups to filter by: every active group for org admins, the administered ones for group admins.</summary>
    public List<TeamGroupResponse> Groups { get; init; } = new();
    public List<TeamMemberResponse> Members { get; init; } = new();
}

public record TeamGroupResponse(int Id, string Name);

/// <summary>One team card. Every nullable value is null where the member visibility matrix hides it from the caller.</summary>
public record TeamMemberResponse
{
    public int UserId { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? ProfileImageUrl { get; init; }
    /// <summary>The member's groups among <see cref="TeamOverviewResponse.Groups"/>.</summary>
    public List<int> GroupIds { get; init; } = new();
    /// <summary>"Saldo gesamt" — the same number as GET members/{id}/balance (cumulativeOvertimeHours); needs TrackedTime.</summary>
    public double? BalanceHours { get; init; }
    /// <summary>A timer runs in this organization right now (failure entries never count); needs Presence.</summary>
    public bool? Present { get; init; }
    /// <summary>Start of the running timer (UTC) when present.</summary>
    public DateTime? PresentSince { get; init; }
    /// <summary>Unresolved failure entries; needs TrackedTime.</summary>
    public int? FailureCount { get; init; }
    /// <summary>Today's core working hours check found a violation that still holds; needs TrackedTime.</summary>
    public bool? CoreHoursViolationToday { get; init; }
}
