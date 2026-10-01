
namespace TimeTracking.Api.Models.Dtos;

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
    public double TargetSat { get; init; }
    public double TargetSun { get; init; }
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
    public double? TargetSat { get; init; }
    public double? TargetSun { get; init; }
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
    public double? TargetSat { get; init; }
    public double? TargetSun { get; init; }
}
