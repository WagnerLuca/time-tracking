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
