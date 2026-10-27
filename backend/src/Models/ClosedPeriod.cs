namespace TimeTracking.Api.Models;

/// <summary>
/// A closed month of an organization (e.g. after payroll, issue #57): time entries, absences and
/// work schedules touching it can only be changed by org admins, with a reason (see
/// <see cref="Services.ClosedPeriods"/>). Org admin data like API keys: no row-level security.
/// </summary>
public class ClosedPeriod
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }

    /// <summary>First day of the closed month (org-local calendar).</summary>
    public DateOnly Month { get; set; }

    public DateTime ClosedAt { get; set; }
    public int? ClosedByUserId { get; set; }

    /// <summary>When the month's figures were frozen (<c>FrozenFigures</c>); null only for months closed before issue #81,
    /// frozen once at startup.</summary>
    public DateTime? FrozenAt { get; set; }

    public Organization Organization { get; set; } = null!;
}

public enum ClosedPeriodEventKind
{
    Closed = 0,
    Reopened = 1,
    /// <summary>An org admin changed entries, absences or schedules inside the closed month.</summary>
    AdminChange = 2
}

/// <summary>Audit list of closing, reopening and admin changes inside closed months (who, when, why).</summary>
public class ClosedPeriodEvent
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public DateOnly Month { get; set; }
    public ClosedPeriodEventKind Kind { get; set; }
    public int? UserId { get; set; }
    public DateTime At { get; set; }
    public string? Reason { get; set; }

    /// <summary>AdminChange: the member whose data changed (null when several).</summary>
    public int? MemberUserId { get; set; }

    /// <summary>AdminChange: what changed, e.g. "TimeEntry:2,AbsenceDay:1".</summary>
    public string? Summary { get; set; }

    public Organization Organization { get; set; } = null!;
    public User? User { get; set; }
    public User? MemberUser { get; set; }
}
