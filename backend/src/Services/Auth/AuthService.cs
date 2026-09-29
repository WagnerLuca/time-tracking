using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;
using BCrypt.Net;

namespace TimeTracking.Api.Services;

public class AuthService : IAuthService
{
    private readonly TimeTrackingDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly ITotpService _totpService;
    private readonly ILogger<AuthService> _logger;
    private readonly JwtSettings _jwtSettings;

    public AuthService(
        TimeTrackingDbContext context,
        ITokenService tokenService,
        ITotpService totpService,
        ILogger<AuthService> logger,
        IOptions<JwtSettings> jwtSettings)
    {
        _context = context;
        _tokenService = tokenService;
        _totpService = totpService;
        _logger = logger;
        _jwtSettings = jwtSettings.Value;
    }

    public async Task<ServiceResult<LoginResponse>> RegisterAsync(RegisterRequest request)
    {
        var emailTaken = await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email.ToLower() == request.Email.ToLower());

        if (emailTaken)
            return ServiceResult.BadRequest<LoginResponse>("A user with this email already exists");

        var passwordError = ValidatePasswordStrength(request.Password);
        if (passwordError != null)
            return ServiceResult.BadRequest<LoginResponse>(passwordError);

        var user = new User
        {
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            EmailConfirmed = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User registered successfully: {Email}", user.Email);

        return ServiceResult.Ok(await IssueTokensAsync(user));
    }

    public async Task<ServiceResult<object>> LoginAsync(LoginRequest request)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());

        if (user == null)
            return ServiceResult.Unauthorized<object>("Invalid email or password");

        if (!user.IsActive)
            return ServiceResult.Unauthorized<object>("This account has been deactivated");

        if (!VerifyPassword(user, request.Password))
            return ServiceResult.Unauthorized<object>("Invalid email or password");

        user.LastLoginAt = DateTime.UtcNow;

        // 2FA enabled — hand out a short-lived token to submit with the TOTP code
        if (user.TwoFactorEnabled && !string.IsNullOrEmpty(user.TotpSecretKey))
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("2FA verification required for user: {Email}", user.Email);
            return ServiceResult.Ok<object>(new TwoFactorRequiredResponse
            {
                TwoFactorToken = _totpService.GenerateTwoFactorToken(user.Id)
            });
        }

        _logger.LogInformation("User logged in successfully: {Email}", user.Email);

        return ServiceResult.Ok<object>(await IssueTokensAsync(user));
    }

    public async Task<ServiceResult<RefreshTokenResponse>> RefreshTokenAsync(string refreshToken)
    {
        var validatedToken = await _tokenService.ValidateRefreshTokenAsync(refreshToken);
        if (validatedToken == null)
            return ServiceResult.Unauthorized<RefreshTokenResponse>("Invalid or expired refresh token");

        var user = validatedToken.User;
        var newAccessToken = _tokenService.GenerateAccessToken(user);
        var newRefreshToken = _tokenService.GenerateRefreshToken(user.Id);

        // Revoke the old refresh token and save the new one
        await _tokenService.RevokeRefreshTokenAsync(refreshToken, newRefreshToken.Token);
        _context.RefreshTokens.Add(newRefreshToken);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Tokens refreshed for user: {UserId}", user.Id);

        return ServiceResult.Ok(new RefreshTokenResponse
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken.Token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpiryMinutes)
        });
    }

    public async Task<ServiceResult<AuthResponse>> LogoutAsync(int userId, string refreshToken)
    {
        // Prevent authenticated users from revoking tokens that belong to another account.
        var tokenOwnerId = await _context.RefreshTokens
            .AsNoTracking()
            .Where(rt => rt.Token == refreshToken && !rt.IsRevoked)
            .Select(rt => (int?)rt.UserId)
            .FirstOrDefaultAsync();

        if (tokenOwnerId.HasValue && tokenOwnerId.Value != userId)
            return ServiceResult.Forbidden<AuthResponse>("You can only revoke your own refresh tokens.");

        await _tokenService.RevokeRefreshTokenAsync(refreshToken);
        return ServiceResult.Ok(new AuthResponse { Success = true, Message = "Refresh token revoked successfully" });
    }

    public async Task<ServiceResult<AuthResponse>> ChangePasswordAsync(int userId, ChangePasswordRequest request)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return ServiceResult.NotFound<AuthResponse>("User not found");

        if (!user.IsActive)
            return ServiceResult.BadRequest<AuthResponse>("This account has been deactivated");

        if (!VerifyPassword(user, request.CurrentPassword))
            return ServiceResult.BadRequest<AuthResponse>("Current password is incorrect");

        var passwordError = ValidatePasswordStrength(request.NewPassword);
        if (passwordError != null)
            return ServiceResult.BadRequest<AuthResponse>(passwordError);

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

        return ServiceResult.Ok(new AuthResponse { Success = true, Message = "Password changed successfully. Please log in again." });
    }

    public async Task<ServiceResult<AuthResponse>> DeleteAccountAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return ServiceResult.NotFound<AuthResponse>("User not found");

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} deleted their account", userId);
        return ServiceResult.Ok(new AuthResponse { Success = true, Message = "Account deleted successfully" });
    }

    public async Task<ServiceResult<UserInfo>> GetCurrentUserAsync(int userId)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        return user == null
            ? ServiceResult.Unauthorized<UserInfo>("User not found.")
            : ServiceResult.Ok(ToUserInfo(user));
    }

    public async Task<ServiceResult<UserInfo>> UpdateProfileAsync(int userId, UpdateProfileRequest request)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return ServiceResult.NotFound<UserInfo>("User not found");

        // Check if email is being changed and if it's already taken
        if (!string.IsNullOrWhiteSpace(request.Email) &&
            !request.Email.Equals(user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var emailTaken = await _context.Users
                .AnyAsync(u => u.Email.ToLower() == request.Email.ToLower() && u.Id != userId);
            if (emailTaken)
                return ServiceResult.BadRequest<UserInfo>("Email is already in use by another account.");

            user.Email = request.Email.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.FirstName))
            user.FirstName = request.FirstName.Trim();

        if (!string.IsNullOrWhiteSpace(request.LastName))
            user.LastName = request.LastName.Trim();

        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} updated their profile", userId);

        return ServiceResult.Ok(ToUserInfo(user));
    }

    // ── Two-Factor Authentication ──

    public async Task<ServiceResult<LoginResponse>> VerifyTwoFactorAsync(TwoFactorVerifyRequest request)
    {
        var userId = _totpService.ValidateTwoFactorToken(request.TwoFactorToken);
        if (userId == null)
            return ServiceResult.Unauthorized<LoginResponse>("Invalid or expired two-factor token");

        var user = await _context.Users.FindAsync(userId.Value);
        if (user == null || !user.IsActive || !user.TwoFactorEnabled)
            return ServiceResult.Unauthorized<LoginResponse>("Invalid two-factor request");

        // Try TOTP code first, then backup codes
        var codeValid = _totpService.VerifyCode(user.TotpSecretKey!, request.Code);

        if (!codeValid && !string.IsNullOrEmpty(user.TwoFactorBackupCodes))
        {
            var backupCodes = user.TwoFactorBackupCodes.Split(',').ToList();
            var normalizedCode = request.Code.Trim().ToLowerInvariant();
            if (backupCodes.Remove(normalizedCode))
            {
                codeValid = true;
                user.TwoFactorBackupCodes = string.Join(',', backupCodes);
            }
        }

        if (!codeValid)
            return ServiceResult.Unauthorized<LoginResponse>("Invalid verification code");

        _logger.LogInformation("2FA verification successful for user: {Email}", user.Email);

        return ServiceResult.Ok(await IssueTokensAsync(user));
    }

    public async Task<ServiceResult<TwoFactorSetupResponse>> SetupTwoFactorAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return ServiceResult.NotFound<TwoFactorSetupResponse>("User not found");

        if (user.TwoFactorEnabled)
            return ServiceResult.BadRequest<TwoFactorSetupResponse>("Two-factor authentication is already enabled. Disable it first to reconfigure.");

        var (secret, sharedKey, uri) = _totpService.GenerateSetup(user.Email);

        // Store pending secret (not enabled yet until confirmed)
        user.TotpSecretKey = secret;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("2FA setup initiated for user {UserId}", userId);

        return ServiceResult.Ok(new TwoFactorSetupResponse
        {
            SharedKey = sharedKey,
            AuthenticatorUri = uri
        });
    }

    public async Task<ServiceResult<TwoFactorConfirmResponse>> ConfirmTwoFactorAsync(int userId, TwoFactorConfirmRequest request)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return ServiceResult.NotFound<TwoFactorConfirmResponse>("User not found");

        if (user.TwoFactorEnabled)
            return ServiceResult.BadRequest<TwoFactorConfirmResponse>("Two-factor authentication is already enabled");

        if (string.IsNullOrEmpty(user.TotpSecretKey))
            return ServiceResult.BadRequest<TwoFactorConfirmResponse>("Call setup first to generate a TOTP secret");

        if (!_totpService.VerifyCode(user.TotpSecretKey, request.Code))
            return ServiceResult.BadRequest<TwoFactorConfirmResponse>("Invalid verification code. Make sure the code from your authenticator app is correct.");

        var backupCodes = _totpService.GenerateBackupCodes();
        user.TwoFactorEnabled = true;
        user.TwoFactorEnabledAt = DateTime.UtcNow;
        user.TwoFactorBackupCodes = string.Join(',', backupCodes);
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("2FA enabled for user {UserId}", userId);

        return ServiceResult.Ok(new TwoFactorConfirmResponse
        {
            Enabled = true,
            BackupCodes = backupCodes
        });
    }

    public async Task<ServiceResult<AuthResponse>> DisableTwoFactorAsync(int userId, string currentPassword)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return ServiceResult.NotFound<AuthResponse>("User not found");

        if (!user.TwoFactorEnabled)
            return ServiceResult.BadRequest<AuthResponse>("Two-factor authentication is not enabled");

        if (!VerifyPassword(user, currentPassword))
            return ServiceResult.BadRequest<AuthResponse>("Current password is incorrect");

        user.TwoFactorEnabled = false;
        user.TotpSecretKey = null;
        user.TwoFactorBackupCodes = null;
        user.TwoFactorEnabledAt = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("2FA disabled for user {UserId}", userId);

        return ServiceResult.Ok(new AuthResponse { Success = true, Message = "Two-factor authentication has been disabled" });
    }

    // ── Helpers ──

    /// <summary>Creates and persists a refresh token and returns the full login payload.</summary>
    private async Task<LoginResponse> IssueTokensAsync(User user)
    {
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken(user.Id);

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        return new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpiryMinutes),
            User = ToUserInfo(user)
        };
    }

    private static UserInfo ToUserInfo(User user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        FirstName = user.FirstName,
        LastName = user.LastName,
        ProfileImageUrl = user.ProfileImageUrl,
        EmailConfirmed = user.EmailConfirmed,
        TwoFactorEnabled = user.TwoFactorEnabled
    };

    private static bool VerifyPassword(User user, string password) =>
        !string.IsNullOrEmpty(user.PasswordHash) && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);

    private static string? ValidatePasswordStrength(string password)
    {
        if (password.Length < 8)
            return "Password must be at least 8 characters long.";
        if (!Regex.IsMatch(password, @"[A-Z]"))
            return "Password must contain at least one uppercase letter.";
        if (!Regex.IsMatch(password, @"[a-z]"))
            return "Password must contain at least one lowercase letter.";
        if (!Regex.IsMatch(password, @"\d"))
            return "Password must contain at least one digit.";
        if (!Regex.IsMatch(password, @"[^a-zA-Z0-9]"))
            return "Password must contain at least one special character.";
        return null;
    }
}
