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
    public int MemberCount { get; init; }
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
    public required string VacationVisibility { get; init; }
    public required string SickDayVisibility { get; init; }
    public double DefaultVacationDays { get; init; }
    /// <summary>Business day end as local "HH:mm" in <see cref="TimeZone"/>.</summary>
    public string BusinessDayEnd { get; init; } = "00:00";
    public string TimeZone { get; init; } = "Europe/Berlin";
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
}

/// <summary>Organization member profile and role.</summary>
public record OrganizationMemberResponse
{
    public int Id { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? ProfileImageUrl { get; init; }
    public required string Role { get; init; }
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
    /// per the effective VacationVisibility). When false, the vacation figures are zeroed.
    /// </summary>
    public bool VacationVisible { get; init; }
    /// <summary>Number of terminal cards; only filled in for org admins.</summary>
    public int CardCount { get; init; }
}

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
    public VisibilityMode? VacationVisibility { get; init; }
    public VisibilityMode? SickDayVisibility { get; init; }
    public double? DefaultVacationDays { get; init; }
    /// <summary>Business day end as local "HH:mm" (e.g. "04:00").</summary>
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Use HH:mm, e.g. 04:00.")]
    public string? BusinessDayEnd { get; init; }
    /// <summary>IANA time zone id, e.g. "Europe/Berlin".</summary>
    [MaxLength(64)]
    public string? TimeZone { get; init; }
}

/// <summary>Per-member time tracking statistics for the admin overview.</summary>
public record MemberTimeOverviewResponse
{
    public int UserId { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public required string Role { get; init; }
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
    public required string BusinessDayEnd { get; init; }
    public required string TimeZone { get; init; }
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
