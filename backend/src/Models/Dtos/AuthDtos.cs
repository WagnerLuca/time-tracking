using System.ComponentModel.DataAnnotations;

namespace TimeTracking.Api.Models.Dtos;

/// <summary>Request payload for registering a new user account.</summary>
public record RegisterRequest
{
    /// <summary>User's email address (must be unique).</summary>
    [Required, EmailAddress, MaxLength(256)]
    public required string Email { get; init; }

    /// <summary>Password (min 8 characters).</summary>
    [Required, MinLength(8), MaxLength(128)]
    public required string Password { get; init; }

    /// <summary>User's first name.</summary>
    [Required, MaxLength(100)]
    public required string FirstName { get; init; }

    /// <summary>User's last name.</summary>
    [Required, MaxLength(100)]
    public required string LastName { get; init; }
}

/// <summary>Request payload for logging in with email and password.</summary>
public record LoginRequest
{
    /// <summary>Registered email address.</summary>
    [Required, EmailAddress, MaxLength(256)]
    public required string Email { get; init; }

    /// <summary>Account password.</summary>
    [Required]
    public required string Password { get; init; }
}

/// <summary>Response returned after a successful login or registration.</summary>
public record LoginResponse
{
    /// <summary>JWT access token.</summary>
    public required string AccessToken { get; init; }
    /// <summary>Opaque refresh token.</summary>
    public required string RefreshToken { get; init; }
    /// <summary>UTC expiry time of the access token.</summary>
    public required DateTime ExpiresAt { get; init; }
    /// <summary>Authenticated user's profile.</summary>
    public required UserInfo User { get; init; }
}

/// <summary>Authenticated user profile information.</summary>
public record UserInfo
{
    public required int Id { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? ProfileImageUrl { get; init; }
    public bool EmailConfirmed { get; init; }
    public bool TwoFactorEnabled { get; init; }
}

/// <summary>Request containing a refresh token (used for refresh and logout).</summary>
public record RefreshTokenRequest
{
    [Required]
    public required string RefreshToken { get; init; }
}

/// <summary>Response returned after a successful token refresh.</summary>
public record RefreshTokenResponse
{
    /// <summary>New JWT access token.</summary>
    public required string AccessToken { get; init; }
    /// <summary>New refresh token (rotated).</summary>
    public required string RefreshToken { get; init; }
    /// <summary>UTC expiry time of the new access token.</summary>
    public required DateTime ExpiresAt { get; init; }
}

/// <summary>Request payload for changing the current user's password.</summary>
public record ChangePasswordRequest
{
    /// <summary>The user's current password for verification.</summary>
    [Required]
    public required string CurrentPassword { get; init; }

    /// <summary>New password (min 8 characters).</summary>
    [Required, MinLength(8), MaxLength(128)]
    public required string NewPassword { get; init; }
}

/// <summary>Generic success/failure response for auth operations.</summary>
public record AuthResponse
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
}

/// <summary>Request payload for updating the current user's profile.</summary>
public record UpdateProfileRequest
{
    /// <summary>Updated first name.</summary>
    [MaxLength(100)]
    public string? FirstName { get; init; }

    /// <summary>Updated last name.</summary>
    [MaxLength(100)]
    public string? LastName { get; init; }

    /// <summary>Updated email address.</summary>
    [EmailAddress, MaxLength(255)]
    public string? Email { get; init; }
}

// ── Two-Factor Authentication DTOs ──

/// <summary>Response when login succeeds but 2FA verification is required.</summary>
public record TwoFactorRequiredResponse
{
    /// <summary>Temporary token to submit with the TOTP code.</summary>
    public required string TwoFactorToken { get; init; }
    /// <summary>Hint that 2FA verification is needed.</summary>
    public bool RequiresTwoFactor { get; init; } = true;
}

/// <summary>Request to verify a TOTP code during login.</summary>
public record TwoFactorVerifyRequest
{
    /// <summary>Temporary token from the login step.</summary>
    [Required]
    public required string TwoFactorToken { get; init; }

    /// <summary>6-digit TOTP code from authenticator app, or a backup code.</summary>
    [Required]
    public required string Code { get; init; }
}

/// <summary>Response returned when setting up 2FA — contains the shared secret and QR URI.</summary>
public record TwoFactorSetupResponse
{
    /// <summary>Base32-encoded shared secret for manual entry.</summary>
    public required string SharedKey { get; init; }

    /// <summary>otpauth:// URI for scanning as a QR code.</summary>
    public required string AuthenticatorUri { get; init; }
}

/// <summary>Request to confirm and enable 2FA by providing a valid TOTP code.</summary>
public record TwoFactorConfirmRequest
{
    /// <summary>6-digit TOTP code from the authenticator app to confirm setup.</summary>
    [Required, StringLength(6, MinimumLength = 6)]
    public required string Code { get; init; }
}

/// <summary>Response after successfully enabling 2FA, includes one-time backup codes.</summary>
public record TwoFactorConfirmResponse
{
    public bool Enabled { get; init; } = true;
    /// <summary>One-time backup codes the user should store securely.</summary>
    public required List<string> BackupCodes { get; init; }
}
