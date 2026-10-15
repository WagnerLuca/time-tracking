using System.ComponentModel.DataAnnotations;

namespace TimeTracking.Api.Models.Dtos;

/// <summary>Absence day entry (sick day, vacation, etc.).</summary>
public record AbsenceDayResponse
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public int OrganizationId { get; init; }
    public DateOnly Date { get; init; }
    public required string Type { get; init; }
    public bool IsHalfDay { get; init; }
    public string? Note { get; init; }
    public string? UserFirstName { get; init; }
    public string? UserLastName { get; init; }

    // Custom org-defined absence type (when Type == Other and CustomTypeId set)
    public int? CustomTypeId { get; init; }
    public string? CustomTypeName { get; init; }
    public string? CustomTypeColor { get; init; }
    public string? CreditMode { get; init; }

    // Audit trail
    public DateTime CreatedAt { get; init; }
    public int? CreatedByUserId { get; init; }
    public string? CreatedByName { get; init; }
    public int? CreatedViaRequestId { get; init; }
}

/// <summary>Request payload for creating a personal absence day.</summary>
public record CreateAbsenceDayRequest
{
    public DateOnly Date { get; init; }
    public AbsenceType Type { get; init; } = AbsenceType.SickDay;
    public bool IsHalfDay { get; init; }

    /// <summary>Optional org-defined custom absence type.</summary>
    public int? CustomTypeId { get; init; }

    [MaxLength(500)]
    public string? Note { get; init; }
}

/// <summary>Request payload for an admin to create an absence day for any member.</summary>
/// <summary>
/// Several absence days at once (e.g. a vacation week): every weekday from <see cref="From"/> to
/// <see cref="To"/> is created, all or nothing. <see cref="UserId"/> is only used by the admin endpoint.
/// </summary>
public record CreateAbsenceRangeRequest
{
    public int? UserId { get; init; }
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public AbsenceType Type { get; init; } = AbsenceType.Vacation;
    public bool IsHalfDay { get; init; }
    public int? CustomTypeId { get; init; }

    [MaxLength(500)]
    public string? Note { get; init; }
}

/// <summary>Absence days to delete together (a span), all or nothing.</summary>
public record DeleteAbsencesRequest
{
    [Required, MinLength(1), MaxLength(366)]
    public required List<int> Ids { get; init; }
}

public record AdminCreateAbsenceDayRequest
{
    public int UserId { get; init; }
    public DateOnly Date { get; init; }
    public AbsenceType Type { get; init; } = AbsenceType.SickDay;
    public bool IsHalfDay { get; init; }

    /// <summary>Optional org-defined custom absence type.</summary>
    public int? CustomTypeId { get; init; }

    [MaxLength(500)]
    public string? Note { get; init; }
}

/// <summary>Org-defined custom absence type (e.g. "Business Trip").</summary>
public record AbsenceTypeDefinitionResponse
{
    public int Id { get; init; }
    public int OrganizationId { get; init; }
    public required string Name { get; init; }
    public string? Color { get; init; }
    public required string CreditMode { get; init; }
    public bool CountsAsVacation { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>Request payload for creating/updating a custom absence type.</summary>
public record UpsertAbsenceTypeDefinitionRequest
{
    [Required, MaxLength(100)]
    public required string Name { get; init; }

    [MaxLength(20)]
    public string? Color { get; init; }

    public AbsenceCreditMode CreditMode { get; init; } = AbsenceCreditMode.FillToTarget;
    public bool CountsAsVacation { get; init; }
}
