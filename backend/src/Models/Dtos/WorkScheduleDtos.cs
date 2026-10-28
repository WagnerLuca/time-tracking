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

/// <summary>
/// A member's work schedules as the model knows them (issue #82): the current one (valid today, null without), at most one
/// future one (null without) and the earlier ones, newest first (read-only).
/// </summary>
public record WorkSchedulePlanResponse
{
    public WorkScheduleResponse? Current { get; init; }
    public WorkScheduleResponse? Future { get; init; }
    public required List<WorkScheduleResponse> History { get; init; }
    /// <summary>The closed months the current period reaches into make it uncorrectable (admins change it from a later day).</summary>
    public bool CurrentInClosedMonth { get; init; }
    public double InitialOvertimeHours { get; init; }
    public required string InitialOvertimeMode { get; init; }
    public required string WorkScheduleChangeMode { get; init; }
}

/// <summary>Target hours of a schedule: a weekly total spread over Mon–Fri, or one value per weekday.</summary>
public record ScheduleTargets
{
    public double? WeeklyWorkHours { get; init; }
    /// <summary>Spread <see cref="WeeklyWorkHours"/> evenly over Monday–Friday (weekend 0); else the per-day values.</summary>
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

/// <summary>"Ändern ab …": a new schedule from a day (default today; members today or later, admins any open day).</summary>
public record ChangeWorkScheduleRequest : ScheduleTargets
{
    public DateOnly? ValidFrom { get; init; }
    /// <summary>Replace an existing future schedule (else 409 <c>schedule.futureExists</c>).</summary>
    public bool ReplaceFuture { get; init; }
}

/// <summary>An admin's correction of the current schedule: new targets, optionally a new first day.</summary>
public record CorrectWorkScheduleRequest : ScheduleTargets
{
    public DateOnly? ValidFrom { get; init; }
}
