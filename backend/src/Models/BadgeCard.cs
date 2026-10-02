namespace TimeTracking.Api.Models;

/// <summary>
/// A card (RFID tag, badge) a terminal identifies a member by. A member can hold several.
/// Card values are not secrets but are never stored in plain text: only the SHA-256 of the
/// normalised value (<see cref="Services.CardIds"/>) and its last four characters as a hint.
/// </summary>
public class BadgeCard
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int UserId { get; set; }
    /// <summary>Hex SHA-256 of the normalised card value, unique per organization.</summary>
    public required string CardHash { get; set; }
    /// <summary>Last four characters of the normalised value, so admins can tell cards apart.</summary>
    public required string CardHint { get; set; }
    public string? Label { get; set; }
    /// <summary>A blocked card (lost, stolen) is rejected by terminals.</summary>
    public bool IsBlocked { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public User User { get; set; } = null!;
}
