namespace TimeTracking.Api.Models;

/// <summary>
/// Organization-scoped API key for unattended devices such as an RFID terminal.
/// A key can only punch members of its organization in/out by badge ID
/// (<see cref="UserOrganization.BadgeId"/>); it grants no other API access.
/// Only the SHA-256 hash is stored — the plaintext is shown once on creation.
/// </summary>
public class ApiKey
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public required string Name { get; set; }
    /// <summary>First characters of the key, so admins can tell keys apart.</summary>
    public required string KeyPrefix { get; set; }
    /// <summary>Hex-encoded SHA-256 of the full key.</summary>
    public required string KeyHash { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }

    public Organization Organization { get; set; } = null!;
}
