
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
    /// <summary>Public holiday region of this period (the Arbeitsort); null = the groups' or the organization's.</summary>
    public string? HolidayRegion { get; init; }
    /// <summary>Vacation days per year while this period applies; null = the year's figure.</summary>
    public double? VacationDaysPerYear { get; init; }
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
    /// <summary>Admins only: holiday region (HolidayPresets key, "" = none). Left out (or set by a member): the previous period's.</summary>
    public string? HolidayRegion { get; init; }
    /// <summary>Admins only: vacation days per year of this period. Left out (or set by a member): the previous period's.</summary>
    public double? VacationDaysPerYear { get; init; }
}

/// <summary>Request payload for updating a work schedule.</summary>
public record UpdateWorkScheduleRequest
{
    public DateOnly? ValidFrom { get; init; }
    /// <summary>New end date; leave it out to keep the current one.</summary>
    public DateOnly? ValidTo { get; init; }
    /// <summary>True removes the end date (the period runs open-ended); wins over <see cref="ValidTo"/>.</summary>
    public bool ClearValidTo { get; init; }
    /// <summary>Admins only: holiday region (HolidayPresets key); "" = none (the groups' or the organization's); left out = unchanged.</summary>
    public string? HolidayRegion { get; init; }
    /// <summary>Admins only: vacation days per year of this period; see <see cref="ClearVacationDaysPerYear"/>.</summary>
    public double? VacationDaysPerYear { get; init; }
    /// <summary>Admins only: true = the year's figure again.</summary>
    public bool ClearVacationDaysPerYear { get; init; }
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
