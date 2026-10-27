using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

public enum AbsenceType
{
    SickDay = 0,
    Vacation = 1,
    Other = 2
}

/// <summary>
/// How a custom absence type affects the day's expected working hours.
/// </summary>
public enum AbsenceCreditMode
{
    /// <summary>Keep hours as is — informational marker only, no time credit.</summary>
    None = 0,
    /// <summary>Top up tracked hours to the day's expected target (e.g. Business Trip).</summary>
    FillToTarget = 1,
    /// <summary>Credit the full day target regardless of tracked hours (like vacation/sick).</summary>
    FullCredit = 2
}

/// <summary>
/// Organization-defined absence type (e.g. "Business Trip", "Training")
/// with configurable effect on expected hours.
/// </summary>
public class AbsenceTypeDefinition
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public required string Name { get; set; }
    public string? Color { get; set; }
    public AbsenceCreditMode CreditMode { get; set; } = AbsenceCreditMode.FillToTarget;
    /// <summary>If true, days of this type are deducted from the member's vacation budget.</summary>
    public bool CountsAsVacation { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public Organization Organization { get; set; } = null!;
}

public class AbsenceDay
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int OrganizationId { get; set; }

    /// <summary>Date of the absence (date only).</summary>
    public DateOnly Date { get; set; }

    public AbsenceType Type { get; set; } = AbsenceType.SickDay;

    /// <summary>Optional org-defined custom absence type. When set, Type is Other.</summary>
    public int? CustomTypeId { get; set; }

    [JsonIgnore]
    public AbsenceTypeDefinition? CustomType { get; set; }

    /// <summary>If true, this absence counts as half a day.</summary>
    public bool IsHalfDay { get; set; }

    /// <summary>
    /// The other half of a day that already has a half-day absence (e.g. half vacation + half sick): a day holds one full
    /// absence or two halves (unique per member, day and half).
    /// </summary>
    public bool SecondHalf { get; set; }

    /// <summary>Optional note, e.g. reason.</summary>
    public string? Note { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Audit: user who created this absence (self, an admin, or the approver of a request).</summary>
    public int? CreatedByUserId { get; set; }

    /// <summary>Audit: the approved OrgRequest this absence originated from, if any.</summary>
    public int? CreatedViaRequestId { get; set; }

    /// <summary>Written by this Betriebsurlaub (<see cref="Models.CompanyLeave"/>); kept in sync with it.</summary>
    public int? CompanyLeaveId { get; set; }
    [JsonIgnore]
    public CompanyLeave? CompanyLeave { get; set; }

    // Navigation
    [JsonIgnore]
    public User User { get; set; } = null!;
    [JsonIgnore]
    public Organization Organization { get; set; } = null!;
}
