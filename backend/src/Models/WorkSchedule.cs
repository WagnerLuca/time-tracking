using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

/// <summary>
/// A work schedule for a user within an organization.
/// ValidFrom is inclusive. ValidTo is inclusive (last day this schedule applies).
/// If ValidTo is null, the schedule is open-ended (applies from ValidFrom onwards).
/// Only one schedule can be active at a time per user per organization.
/// </summary>
public class WorkSchedule
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int OrganizationId { get; set; }

    /// <summary>First day this schedule is effective (inclusive).</summary>
    public DateOnly ValidFrom { get; set; }

    /// <summary>Last day this schedule is effective (inclusive). Null means open-ended.</summary>
    public DateOnly? ValidTo { get; set; }

    public double? WeeklyWorkHours { get; set; }
    public double TargetMon { get; set; }
    public double TargetTue { get; set; }
    public double TargetWed { get; set; }
    public double TargetThu { get; set; }
    public double TargetFri { get; set; }
    public double TargetSat { get; set; }
    public double TargetSun { get; set; }

    /// <summary>
    /// Public holiday region while this schedule applies (the Arbeitsort, <c>HolidayPresets</c> key); null = the member's
    /// groups', else the organization's. Dated with the schedule, so a move to another state never changes the past.
    /// </summary>
    public string? HolidayRegion { get; set; }

    /// <summary>
    /// Vacation days per year while this schedule applies (e.g. fewer after moving to a 3-day week); null = the year's
    /// figure (<c>VacationBalances</c>). A year's entitlement is split by these periods.
    /// </summary>
    public double? VacationDaysPerYear { get; set; }

    /// <summary>Target hours for that weekday.</summary>
    public double TargetFor(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => TargetMon,
        DayOfWeek.Tuesday => TargetTue,
        DayOfWeek.Wednesday => TargetWed,
        DayOfWeek.Thursday => TargetThu,
        DayOfWeek.Friday => TargetFri,
        DayOfWeek.Saturday => TargetSat,
        _ => TargetSun
    };

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [JsonIgnore]
    public User User { get; set; } = null!;
    [JsonIgnore]
    public Organization Organization { get; set; } = null!;
}
