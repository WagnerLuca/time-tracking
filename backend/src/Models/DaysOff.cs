using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

/// <summary>
/// When a member belonged to a group (written by <c>TimeTrackingDbContext.SaveChangesAsync</c> for every added or removed
/// <see cref="GroupMember"/> and every deactivated group): group company holidays and group holiday regions apply to the
/// days of these periods, so a group change never rewrites a member's past. <see cref="Until"/> null = still a member.
/// </summary>
public class GroupMembershipPeriod
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int GroupId { get; set; }
    public int UserId { get; set; }
    public DateTime From { get; set; }
    public DateTime? Until { get; set; }

    [JsonIgnore]
    public Group Group { get; set; } = null!;
    [JsonIgnore]
    public Organization Organization { get; set; } = null!;
    [JsonIgnore]
    public User User { get; set; } = null!;
}

/// <summary>A member an admin left out of a company holiday (<see cref="HolidayId"/>) or a Betriebsurlaub (<see cref="CompanyLeaveId"/>).</summary>
public class DayOffExclusion
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int UserId { get; set; }
    public int? HolidayId { get; set; }
    public int? CompanyLeaveId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public Holiday? Holiday { get; set; }
    [JsonIgnore]
    public CompanyLeave? CompanyLeave { get; set; }
}

/// <summary>
/// Betriebsurlaub: enforced vacation for the whole organization or one group (and its sub-groups). It is written as
/// ordinary vacation <see cref="AbsenceDay"/> rows (<see cref="AbsenceDay.CompanyLeaveId"/>) on each member's working days,
/// so every vacation rule (budget, calendars, exports, closed months) applies without code of its own.
/// </summary>
public class CompanyLeave
{
    public const int NameMaxLength = 200;

    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int? GroupId { get; set; }
    public required string Name { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }

    [JsonIgnore]
    public Organization Organization { get; set; } = null!;
    [JsonIgnore]
    public Group? Group { get; set; }
    [JsonIgnore]
    public ICollection<DayOffExclusion> Exclusions { get; set; } = new List<DayOffExclusion>();
}
