namespace TimeTracking.Api.Models.Dtos;

// Organization DTOs
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
    public DateTime CreatedAt { get; init; }
    public required List<OrganizationMemberResponse> Members { get; init; }
    public List<PauseRuleResponse>? PauseRules { get; init; }
}

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

public record UserOrganizationResponse
{
    public int OrganizationId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Slug { get; init; }
    public required string Role { get; init; }
    public DateTime JoinedAt { get; init; }
    public int MemberCount { get; init; }
}

public record CreateOrganizationRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Slug { get; init; }
    public string? Website { get; init; }
    public string? LogoUrl { get; init; }
}

public record UpdateOrganizationRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Slug { get; init; }
    public string? Website { get; init; }
    public string? LogoUrl { get; init; }
}

public record AddMemberRequest
{
    public int UserId { get; init; }
    public OrganizationRole Role { get; init; } = OrganizationRole.Member;
}

public record UpdateMemberRoleRequest
{
    public OrganizationRole Role { get; init; }
}

public record UpdateOrganizationSettingsRequest
{
    public bool? AutoPauseEnabled { get; init; }
    public RuleMode? EditPastEntriesMode { get; init; }
    public RuleMode? EditPauseMode { get; init; }
    public RuleMode? InitialOvertimeMode { get; init; }
    public RuleMode? JoinPolicy { get; init; }
}

public record PauseRuleResponse
{
    public int Id { get; init; }
    public int OrganizationId { get; init; }
    public double MinHours { get; init; }
    public int PauseMinutes { get; init; }
}

public record CreatePauseRuleRequest
{
    public double MinHours { get; init; }
    public int PauseMinutes { get; init; }
}

public record UpdatePauseRuleRequest
{
    public double MinHours { get; init; }
    public int PauseMinutes { get; init; }
}

// Work schedule DTOs
public record WorkScheduleResponse
{
    public int UserId { get; init; }
    public int OrganizationId { get; init; }
    public double? WeeklyWorkHours { get; init; }
    public double TargetMon { get; init; }
    public double TargetTue { get; init; }
    public double TargetWed { get; init; }
    public double TargetThu { get; init; }
    public double TargetFri { get; init; }
    public double InitialOvertimeHours { get; init; }
    public required string InitialOvertimeMode { get; init; }
}

public record UpdateWorkScheduleRequest
{
    public double? WeeklyWorkHours { get; init; }
    /// <summary>
    /// If true, distribute WeeklyWorkHours equally across Mon-Fri.
    /// If false, use the individual TargetXxx fields.
    /// </summary>
    public bool DistributeEvenly { get; init; } = true;
    public double? TargetMon { get; init; }
    public double? TargetTue { get; init; }
    public double? TargetWed { get; init; }
    public double? TargetThu { get; init; }
    public double? TargetFri { get; init; }
    public double? InitialOvertimeHours { get; init; }
}

// Admin time overview
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

public record SetInitialOvertimeRequest
{
    public double InitialOvertimeHours { get; init; }
}
