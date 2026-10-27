using System.ComponentModel.DataAnnotations;

namespace TimeTracking.Api.Models.Dtos;

/// <summary>A Betriebsurlaub: enforced vacation of the organization or one group, written as vacation days.</summary>
public record CompanyLeaveResponse
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    /// <summary>The group (and its sub-groups); null = the whole organization.</summary>
    public int? GroupId { get; init; }
    /// <summary>Members left out (org admins only; null for others).</summary>
    public List<int>? ExcludedUserIds { get; init; }
}

public record CreateCompanyLeaveRequest
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public int? GroupId { get; init; }
}

public record UpdateCompanyLeaveRequest
{
    [MaxLength(200)]
    public string? Name { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    /// <summary>Move it to this group; left out = unchanged (the whole organization: <see cref="ForWholeOrganization"/>).</summary>
    public int? GroupId { get; init; }
    public bool ForWholeOrganization { get; init; }
}
