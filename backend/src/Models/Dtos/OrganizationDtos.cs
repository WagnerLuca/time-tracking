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
    public DateTime? SettingsUpdatedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public required List<OrganizationMemberResponse> Members { get; init; }
    public List<PauseRuleResponse>? PauseRules { get; init; }
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

/// <summary>Request payload for adding a user to an organization.</summary>
public record AddMemberRequest
{
    public int UserId { get; init; }
    public OrganizationRole Role { get; init; } = OrganizationRole.Member;
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
}

/// <summary>Automatic pause rule (deduct X minutes after Y hours worked).</summary>
public record PauseRuleResponse
{
    public int Id { get; init; }
    public int OrganizationId { get; init; }
    public double MinHours { get; init; }
    public int PauseMinutes { get; init; }
}

/// <summary>Request payload for creating a pause rule.</summary>
public record CreatePauseRuleRequest
{
    /// <summary>Minimum hours worked before this rule triggers.</summary>
    [Range(0.01, 24.0)]
    public double MinHours { get; init; }

    /// <summary>Pause minutes to deduct.</summary>
    [Range(1, 480)]
    public int PauseMinutes { get; init; }
}

/// <summary>Request payload for updating a pause rule.</summary>
public record UpdatePauseRuleRequest
{
    [Range(0.01, 24.0)]
    public double MinHours { get; init; }

    [Range(1, 480)]
    public int PauseMinutes { get; init; }
}

/// <summary>Work schedule defining target hours per weekday.</summary>
public record WorkScheduleResponse
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public int OrganizationId { get; init; }
    public DateOnly ValidFrom { get; init; }
    public DateOnly? ValidTo { get; init; }
    public double? WeeklyWorkHours { get; init; }
    public double TargetMon { get; init; }
    public double TargetTue { get; init; }
    public double TargetWed { get; init; }
    public double TargetThu { get; init; }
    public double TargetFri { get; init; }
    public double InitialOvertimeHours { get; init; }
    public required string InitialOvertimeMode { get; init; }
    public required string WorkScheduleChangeMode { get; init; }
}

/// <summary>Request payload for creating a work schedule.</summary>
public record CreateWorkScheduleRequest
{
    public DateOnly ValidFrom { get; init; }
    public DateOnly? ValidTo { get; init; }
    public double? WeeklyWorkHours { get; init; }
    public bool DistributeEvenly { get; init; } = true;
    public double? TargetMon { get; init; }
    public double? TargetTue { get; init; }
    public double? TargetWed { get; init; }
    public double? TargetThu { get; init; }
    public double? TargetFri { get; init; }
}

/// <summary>Request payload for updating a work schedule.</summary>
public record UpdateWorkScheduleRequest
{
    public DateOnly? ValidFrom { get; init; }
    public DateOnly? ValidTo { get; init; }
    public double? WeeklyWorkHours { get; init; }
    public bool DistributeEvenly { get; init; } = true;
    public double? TargetMon { get; init; }
    public double? TargetTue { get; init; }
    public double? TargetWed { get; init; }
    public double? TargetThu { get; init; }
    public double? TargetFri { get; init; }
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
}

/// <summary>Request payload for setting a member's initial overtime hours.</summary>
public record SetInitialOvertimeRequest
{
    public double InitialOvertimeHours { get; init; }
}

/// <summary>Organization holiday entry.</summary>
public record HolidayResponse
{
    public int Id { get; init; }
    public int OrganizationId { get; init; }
    public DateOnly Date { get; init; }
    public required string Name { get; init; }
    public bool IsRecurring { get; init; }
    public bool IsHalfDay { get; init; }
}

/// <summary>Request payload for creating a holiday.</summary>
public record CreateHolidayRequest
{
    public DateOnly Date { get; init; }

    [Required, MaxLength(200)]
    public required string Name { get; init; }

    public bool IsRecurring { get; init; }
    public bool IsHalfDay { get; init; }
}

/// <summary>Request payload for updating a holiday.</summary>
public record UpdateHolidayRequest
{
    public DateOnly? Date { get; init; }

    [MaxLength(200)]
    public string? Name { get; init; }

    public bool? IsRecurring { get; init; }
    public bool? IsHalfDay { get; init; }
}

/// <summary>Absence day entry (sick day, vacation, etc.).</summary>
public record AbsenceDayResponse
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public int OrganizationId { get; init; }
    public DateOnly Date { get; init; }
    public required string Type { get; init; }
    public bool IsHalfDay { get; init; }
    public string? Note { get; init; }
    public string? UserFirstName { get; init; }
    public string? UserLastName { get; init; }
}

/// <summary>Request payload for creating a personal absence day.</summary>
public record CreateAbsenceDayRequest
{
    public DateOnly Date { get; init; }
    public AbsenceType Type { get; init; } = AbsenceType.SickDay;
    public bool IsHalfDay { get; init; }

    [MaxLength(500)]
    public string? Note { get; init; }
}

/// <summary>Request payload for an admin to create an absence day for any member.</summary>
public record AdminCreateAbsenceDayRequest
{
    public int UserId { get; init; }
    public DateOnly Date { get; init; }
    public AbsenceType Type { get; init; } = AbsenceType.SickDay;
    public bool IsHalfDay { get; init; }

    [MaxLength(500)]
    public string? Note { get; init; }
}


