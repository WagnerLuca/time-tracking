using System.ComponentModel.DataAnnotations;

namespace TimeTracking.Api.Models.Dtos;

/// <summary>A company holiday of the organization or of one group (public holidays come from the region, see <see cref="DayOffResponse"/>).</summary>
public record HolidayResponse
{
    public int Id { get; init; }
    public int OrganizationId { get; init; }
    public DateOnly Date { get; init; }
    public required string Name { get; init; }
    public bool IsRecurring { get; init; }
    public bool IsHalfDay { get; init; }
    /// <summary>The group (and its sub-groups) it applies to; null = the whole organization.</summary>
    public int? GroupId { get; init; }
    /// <summary>Members left out of it (org admins only; null for others).</summary>
    public List<int>? ExcludedUserIds { get; init; }
}

/// <summary>Request payload for creating a holiday.</summary>
public record CreateHolidayRequest
{
    public DateOnly Date { get; init; }

    [Required, MaxLength(200)]
    public required string Name { get; init; }

    public bool IsRecurring { get; init; }
    public bool IsHalfDay { get; init; }
    /// <summary>Only for this group and its sub-groups; null = the whole organization.</summary>
    public int? GroupId { get; init; }
}

/// <summary>Request payload for updating a holiday.</summary>
public record UpdateHolidayRequest
{
    public DateOnly? Date { get; init; }

    [MaxLength(200)]
    public string? Name { get; init; }

    public bool? IsRecurring { get; init; }
    public bool? IsHalfDay { get; init; }
    /// <summary>Move it to this group; left out = unchanged (the whole organization: <see cref="ForWholeOrganization"/>).</summary>
    public int? GroupId { get; init; }
    public bool ForWholeOrganization { get; init; }
}

/// <summary>The members to leave out of a company holiday or a Betriebsurlaub (replaces the list).</summary>
public record SetExclusionsRequest
{
    public List<int> UserIds { get; init; } = [];
}

/// <summary>A region whose public holidays can be imported.</summary>
public record HolidayPresetResponse
{
    /// <summary>Stable key ("DE", "DE-BY", "AT", "CH"); the UI translates the region name by it.</summary>
    public required string Key { get; init; }
    /// <summary>German name, a fallback for clients that do not know the key.</summary>
    public required string Name { get; init; }
}

/// <summary>A public holiday of a region (calculated).</summary>
public record PublicHolidayResponse(DateOnly Date, string Name);

/// <summary>A holiday on one member's day: "Public" (their region) or "Company" (organization or group).</summary>
public record DayOffResponse(DateOnly Date, string Name, bool IsHalfDay, string Source);

/// <summary>A member's holidays in a range, with the region that applies today.</summary>
public record MemberDaysOffResponse
{
    public int UserId { get; init; }
    /// <summary>Public holiday region today (schedule, groups or organization); null = none.</summary>
    public string? Region { get; init; }
    /// <summary>The member's groups name different regions (and the schedule none), so the organization's applies.</summary>
    public bool RegionConflict { get; init; }
    public List<DayOffResponse> Days { get; init; } = [];
}
