using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

/// <summary>
/// A time-ranged work schedule for a user within an organization.
/// ValidFrom is inclusive. ValidTo is inclusive (last day this schedule applies).
/// If ValidTo is null, the schedule is open-ended (applies from ValidFrom onwards).
/// </summary>
public class WorkSchedulePeriod
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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [JsonIgnore]
    public User User { get; set; } = null!;
    [JsonIgnore]
    public Organization Organization { get; set; } = null!;
}
