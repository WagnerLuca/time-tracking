using System.ComponentModel.DataAnnotations;

namespace TimeTracking.Api.Models.Dtos;

/// <summary>Organization holiday entry.</summary>
public record HolidayResponse
{
    public int Id { get; init; }
    public int OrganizationId { get; init; }
    public DateOnly Date { get; init; }
    public required string Name { get; init; }
    public bool IsRecurring { get; init; }
    public bool IsHalfDay { get; init; }
}

/// <summary>Request payload for creating a holiday.</summary>
public record CreateHolidayRequest
{
    public DateOnly Date { get; init; }

    [Required, MaxLength(200)]
    public required string Name { get; init; }

    public bool IsRecurring { get; init; }
    public bool IsHalfDay { get; init; }
}

/// <summary>Request payload for updating a holiday.</summary>
public record UpdateHolidayRequest
{
    public DateOnly? Date { get; init; }

    [MaxLength(200)]
    public string? Name { get; init; }

    public bool? IsRecurring { get; init; }
    public bool? IsHalfDay { get; init; }
}

/// <summary>A country whose public holidays can be imported.</summary>
public record HolidayPresetResponse
{
    public required string Key { get; init; }
    public required string Name { get; init; }
}
