using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

public enum OrganizationRole
{
    Member = 0,
    Admin = 1,
    Owner = 2  // For the creator of the organization
}

public static class UserOrganizationLimits
{
    public const int PersonnelNumberMaxLength = 50;
}

public class UserOrganization
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int OrganizationId { get; set; }
    public OrganizationRole Role { get; set; } = OrganizationRole.Member;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    
    public bool IsActive { get; set; } = true;

    /// <summary>When the member left (set by the save hook whenever <see cref="IsActive"/> turns false, cleared on rejoining).</summary>
    public DateTime? LeftAt { get; set; }

    /// <summary>
    /// First day of employment (Eintrittsdatum), set by an admin: the vacation of that year is pro rata (§ 5 BUrlG). Null =
    /// employed before any year shown (joining the app is no employment start).
    /// </summary>
    public DateOnly? EmploymentStart { get; set; }
    /// <summary>Last day of employment (Austrittsdatum), set by an admin; null = still employed.</summary>
    public DateOnly? EmploymentEnd { get; set; }

    // Initial overtime balance in hours (set by user, applied before first tracked entry)
    public double InitialOvertimeHours { get; set; }
    
    // Number of vacation days this member is entitled to per year
    public double VacationDaysPerYear { get; set; }

    /// <summary>Optional personnel number for payroll (admin-set, unique per organization when set).</summary>
    public string? PersonnelNumber { get; set; }

    /// <summary>
    /// Accounting role ("Buchhaltung", docs/ACCOUNTING_ROLE_PLAN.md): orthogonal to <see cref="Role"/>.
    /// Grants the accounting panel (org-wide reports and payroll regardless of <see cref="Organization.MemberTimeEntryVisibility"/>,
    /// month closing, vacation overview) and nothing else.
    /// </summary>
    public bool IsAccountant { get; set; }

    /// <summary>
    /// Overtime balance checkpoints (<c>BalanceCheckpoints</c>): bumped by every save that changes this member's
    /// entries, absences or schedules in an ended month; a checkpoint is only valid while its
    /// <see cref="BalanceCheckpoint.Version"/> matches.
    /// </summary>
    public int BalanceVersion { get; set; }
    
    // Navigation properties
    [JsonIgnore]
    public User User { get; set; } = null!;
    
    [JsonIgnore]
    public Organization Organization { get; set; } = null!;
}
