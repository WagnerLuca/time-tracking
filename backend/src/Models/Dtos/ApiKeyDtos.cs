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

/// <summary>Rename a key (its name is the terminal name in the punch protocol and on entries).</summary>
public record UpdateApiKeyRequest
{
    [Required, MaxLength(100)]
    public required string Name { get; init; }
}

/// <summary>A card scan from a terminal.</summary>
public record PunchRequest
{
    /// <summary>The card value (UID or a value derived from the card's secret); treated as opaque.</summary>
    [Required, MaxLength(100)]
    public required string BadgeId { get; init; }

    /// <summary>
    /// When the card was scanned (UTC), for scans a terminal queued while offline. Accepted from
    /// 72 hours ago to 2 minutes ahead; must not be before the member's last accepted punch.
    /// Without it the arrival time counts.
    /// </summary>
    public DateTime? ScannedAt { get; init; }

    /// <summary>The terminal's id for this scan, unique per key: a replay returns the stored result.</summary>
    [MaxLength(64)]
    public string? ClientPunchId { get; init; }
}

/// <summary>Outcome of a card scan.</summary>
public record PunchResponse
{
    /// <summary>"Started", "Stopped" or "ClosedFailure" (a queued scan closed an auto-stopped entry).</summary>
    public required string Action { get; init; }
    /// <summary>True when this was a second scan within a minute: nothing changed, this is the previous result.</summary>
    public bool Repeated { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required TimeEntryResponse Entry { get; init; }
}

/// <summary>A member's terminal card. The card value itself is never returned, only its last characters.</summary>
public record BadgeCardResponse
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public required string CardHint { get; init; }
    public string? Label { get; init; }
    public bool IsBlocked { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastUsedAt { get; init; }
}

/// <summary>Add a card to a member: typed in (<see cref="CardId"/>) or adopted from an unknown scan (<see cref="FromPunchId"/>).</summary>
public record AddBadgeCardRequest
{
    [MaxLength(100)]
    public string? CardId { get; init; }
    public long? FromPunchId { get; init; }
    [MaxLength(100)]
    public string? Label { get; init; }
}

/// <summary>Change a card's label or block/unblock it; unset fields stay.</summary>
public record UpdateBadgeCardRequest
{
    [MaxLength(100)]
    public string? Label { get; init; }
    public bool? IsBlocked { get; init; }
}

/// <summary>A card a terminal scanned recently that belongs to nobody (grouped per card).</summary>
public record UnknownCardResponse
{
    /// <summary>The latest scan of the card; pass it as <c>fromPunchId</c> to assign the card.</summary>
    public long PunchId { get; init; }
    public required string CardHint { get; init; }
    public string? TerminalName { get; init; }
    public DateTime LastSeenAt { get; init; }
    public int Count { get; init; }
}

/// <summary>One row of the punch protocol.</summary>
public record TerminalPunchResponse
{
    public long Id { get; init; }
    public DateTime ScannedAt { get; init; }
    public DateTime ReceivedAt { get; init; }
    /// <summary>Started, Stopped, ClosedFailure, Repeated, UnknownCard, Blocked or Rejected.</summary>
    public required string Result { get; init; }
    public string? TerminalName { get; init; }
    public required string CardHint { get; init; }
    public string? CardLabel { get; init; }
    public int? UserId { get; init; }
    public string? MemberName { get; init; }
    public int? TimeEntryId { get; init; }
}
