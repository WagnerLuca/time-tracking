using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

public class User
{
    public int Id { get; set; }
    public required string Email { get; set; }

    [JsonIgnore]
    public string? PasswordHash { get; set; }  // Nullable for OAuth-only users

    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public bool EmailConfirmed { get; set; } = false;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTime UpdatedAt { get; set; }

    public bool IsActive { get; set; } = true;

    public string? ProfileImageUrl { get; set; }

    public DateTime? LastLoginAt { get; set; }

    // Two-factor authentication
    [JsonIgnore]
    public string? TotpSecretKey { get; set; }
    public bool TwoFactorEnabled { get; set; } = false;
    public DateTime? TwoFactorEnabledAt { get; set; }
    [JsonIgnore]
    public string? TwoFactorBackupCodes { get; set; }

    /// <summary>
    /// Changes whenever credentials or 2FA change; access tokens carry it ("sst") and stop
    /// working once it no longer matches, so old sessions end immediately.
    /// </summary>
    [JsonIgnore]
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Failed 2FA codes in a row; five lock 2FA verification for 15 minutes.</summary>
    [JsonIgnore]
    public int TwoFactorFailedAttempts { get; set; }

    [JsonIgnore]
    public DateTime? TwoFactorLockedUntil { get; set; }

    /// <summary>A 2FA login token issued before this instant has been used (tokens are single-use).</summary>
    [JsonIgnore]
    public DateTime? LastTwoFactorLoginAt { get; set; }

    /// <summary>TOTP time step of the last accepted code; a code can never be used twice.</summary>
    [JsonIgnore]
    public long? TotpLastStep { get; set; }

    // Navigation properties
    [JsonIgnore]
    public ICollection<UserOrganization> UserOrganizations { get; set; } = new List<UserOrganization>();
}
