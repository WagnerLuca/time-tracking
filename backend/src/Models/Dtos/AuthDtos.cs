namespace TimeTracking.Api.Models.Dtos;

// Registration
public record RegisterRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
}

// Login
public record LoginRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
}

public record LoginResponse
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public required UserInfo User { get; init; }
}

public record UserInfo
{
    public required int Id { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? ProfileImageUrl { get; init; }
    public bool EmailConfirmed { get; init; }
}

// Refresh Token
public record RefreshTokenRequest
{
    public required string RefreshToken { get; init; }
}

public record RefreshTokenResponse
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTime ExpiresAt { get; init; }
}

// Change Password
public record ChangePasswordRequest
{
    public required string CurrentPassword { get; init; }
    public required string NewPassword { get; init; }
}

// General Response
public record AuthResponse
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
}
