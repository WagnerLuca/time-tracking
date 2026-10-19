using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

/// <summary>
/// A member's cumulative overtime at the end of one ended org-local month (derived data, see
/// <c>Services/Common/BalanceCheckpoints.cs</c>): the balance of later days starts here instead of at the member's first
/// tracked day. Valid only while <see cref="Version"/>, <see cref="OrgVersion"/> and <see cref="Algorithm"/> match
/// <see cref="UserOrganization.BalanceVersion"/>, <see cref="Organization.BalanceVersion"/> and
/// <c>BalanceCheckpoints.AlgorithmVersion</c>; invalid rows are ignored and cleaned up. RLS like TimeEntries.
/// </summary>
public class BalanceCheckpoint
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int UserId { get; set; }
    /// <summary>First day of the org-local month (which had ended when the row was written).</summary>
    public DateOnly Month { get; set; }
    /// <summary>The member's first tracked day the running total started from.</summary>
    public DateOnly FirstDay { get; set; }
    /// <summary>Overtime from <see cref="FirstDay"/> to the month's last day, without the initial overtime.</summary>
    public double OvertimeDelta { get; set; }
    public int Version { get; set; }
    public int OrgVersion { get; set; }
    public int Algorithm { get; set; }
    public DateTime ComputedAt { get; set; }

    [JsonIgnore]
    public Organization Organization { get; set; } = null!;
    [JsonIgnore]
    public User User { get; set; } = null!;
}
