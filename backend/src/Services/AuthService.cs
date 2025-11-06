using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;
using BCrypt.Net;

namespace TimeTracking.Api.Services;

public class AuthService : IAuthService
{
    private readonly TimeTrackingDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        TimeTrackingDbContext context,
        ITokenService tokenService,
        ILogger<AuthService> logger)
    {
        _context = context;
        _tokenService = tokenService;
        _logger = logger;
    }

    public async Task<(bool Success, string Message, LoginResponse? Response)> RegisterAsync(RegisterRequest request)
    {
        try
        {
            // Check if user already exists
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());

            if (existingUser != null)
            {
                return (false, "A user with this email already exists", null);
            }

            // Validate password strength (basic validation)
            if (request.Password.Length < 8)
            {
                return (false, "Password must be at least 8 characters long", null);
            }

            // Hash the password
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            // Create new user
            var user = new User
            {
                Email = request.Email,
                FirstName = request.FirstName,
                LastName = request.LastName,
                PasswordHash = passwordHash,
                EmailConfirmed = false,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Generate tokens
            var accessToken = _tokenService.GenerateAccessToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken(user.Id);

            _context.RefreshTokens.Add(refreshToken);
            await _context.SaveChangesAsync();

            _logger.LogInformation("User registered successfully: {Email}", user.Email);

            var response = new LoginResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken.Token,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15), // Should match JWT settings
                User = new UserInfo
                {
                    Id = user.Id,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    ProfileImageUrl = user.ProfileImageUrl,
                    EmailConfirmed = user.EmailConfirmed
                }
            };

            return (true, "Registration successful", response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during user registration");
            return (false, "An error occurred during registration", null);
        }
    }

    public async Task<(bool Success, string Message, LoginResponse? Response)> LoginAsync(LoginRequest request)
    {
        try
        {
            // Find user by email
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());

            if (user == null)
            {
                return (false, "Invalid email or password", null);
            }

            if (!user.IsActive)
            {
                return (false, "This account has been deactivated", null);
            }

            // Verify password
            if (string.IsNullOrEmpty(user.PasswordHash) || 
                !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return (false, "Invalid email or password", null);
            }

            // Update last login
            user.LastLoginAt = DateTime.UtcNow;

            // Generate tokens
            var accessToken = _tokenService.GenerateAccessToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken(user.Id);

            _context.RefreshTokens.Add(refreshToken);
            await _context.SaveChangesAsync();

            _logger.LogInformation("User logged in successfully: {Email}", user.Email);

            var response = new LoginResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken.Token,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15), // Should match JWT settings
                User = new UserInfo
                {
                    Id = user.Id,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    ProfileImageUrl = user.ProfileImageUrl,
                    EmailConfirmed = user.EmailConfirmed
                }
            };

            return (true, "Login successful", response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during user login");
            return (false, "An error occurred during login", null);
        }
    }

    public async Task<(bool Success, string Message, RefreshTokenResponse? Response)> RefreshTokenAsync(string refreshToken)
    {
        try
        {
            // Validate the refresh token
            var validatedToken = await _tokenService.ValidateRefreshTokenAsync(refreshToken);

            if (validatedToken == null)
            {
                return (false, "Invalid or expired refresh token", null);
            }

            var user = validatedToken.User;

            // Generate new tokens
            var newAccessToken = _tokenService.GenerateAccessToken(user);
            var newRefreshToken = _tokenService.GenerateRefreshToken(user.Id);

            // Revoke the old refresh token and save the new one
            await _tokenService.RevokeRefreshTokenAsync(refreshToken, newRefreshToken.Token);
            _context.RefreshTokens.Add(newRefreshToken);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Tokens refreshed for user: {UserId}", user.Id);

            var response = new RefreshTokenResponse
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken.Token,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15) // Should match JWT settings
            };

            return (true, "Token refreshed successfully", response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            return (false, "An error occurred during token refresh", null);
        }
    }

    public async Task<(bool Success, string Message)> ChangePasswordAsync(int userId, ChangePasswordRequest request)
    {
        try
        {
            var user = await _context.Users.FindAsync(userId);

            if (user == null)
            {
                return (false, "User not found");
            }

            if (!user.IsActive)
            {
                return (false, "This account has been deactivated");
            }

            // Verify current password
            if (string.IsNullOrEmpty(user.PasswordHash) || 
                !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            {
                return (false, "Current password is incorrect");
            }

            // Validate new password
            if (request.NewPassword.Length < 8)
            {
                return (false, "New password must be at least 8 characters long");
            }

            // Hash and update password
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;

            // Revoke all existing refresh tokens for security
            var userRefreshTokens = await _context.RefreshTokens
                .Where(rt => rt.UserId == userId && !rt.IsRevoked)
                .ToListAsync();

            foreach (var token in userRefreshTokens)
            {
                token.IsRevoked = true;
                token.RevokedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Password changed for user: {UserId}", userId);

            return (true, "Password changed successfully. Please log in again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during password change");
            return (false, "An error occurred while changing the password");
        }
    }

    public async Task<(bool Success, string Message)> RevokeRefreshTokenAsync(string refreshToken)
    {
        try
        {
            await _tokenService.RevokeRefreshTokenAsync(refreshToken);
            return (true, "Refresh token revoked successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token revocation");
            return (false, "An error occurred while revoking the token");
        }
    }
}
