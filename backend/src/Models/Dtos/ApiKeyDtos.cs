using System.ComponentModel.DataAnnotations;

namespace TimeTracking.Api.Models.Dtos;

/// <summary>An organization API key (terminal key); never includes the secret.</summary>
public record ApiKeyResponse
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public required string KeyPrefix { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastUsedAt { get; init; }
}

/// <summary>Returned once when a key is created — the only time the plaintext key is visible.</summary>
public record CreatedApiKeyResponse : ApiKeyResponse
{
    public required string Key { get; init; }
}

/// <summary>Request payload for creating an organization API key.</summary>
public record CreateApiKeyRequest
{
    [Required, MaxLength(100)]
    public required string Name { get; init; }
}

/// <summary>Request payload for assigning (or clearing, with null) a member's badge ID.</summary>
public record SetBadgeRequest
{
    [MaxLength(100)]
    public string? BadgeId { get; init; }
}

/// <summary>A badge scan from a terminal.</summary>
public record PunchRequest
{
    [Required, MaxLength(100)]
    public required string BadgeId { get; init; }
}

/// <summary>Outcome of a badge scan: the member's timer was started or stopped.</summary>
public record PunchResponse
{
    /// <summary>"Started" or "Stopped".</summary>
    public required string Action { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required TimeEntryResponse Entry { get; init; }
}
