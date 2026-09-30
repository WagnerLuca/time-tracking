namespace TimeTracking.Api.Models;

public enum UserTokenPurpose
{
    EmailVerification = 0,
    PasswordReset = 1,
    /// <summary>Confirms a new email address (<see cref="UserToken.NewEmail"/>) before it replaces the old one.</summary>
    EmailChange = 2
}

/// <summary>
/// Single-use token sent by email (verification, password reset, email change).
/// Only the SHA-256 hash is stored; the plaintext exists only in the link sent to the user.
/// </summary>
public class UserToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public UserTokenPurpose Purpose { get; set; }
    public required string TokenHash { get; set; }
    public string? NewEmail { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}
