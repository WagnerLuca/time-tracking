using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace TimeTracking.Api.Services;

public class AuthService : IAuthService
{
    private static readonly TimeSpan EmailTokenLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromHours(1);
    // One mail per purpose and user in this window, so the endpoints cannot be used to spam a mailbox
    private static readonly TimeSpan MailCooldown = TimeSpan.FromMinutes(2);
    private const int MaxTwoFactorAttempts = 5;
    private static readonly TimeSpan TwoFactorLockout = TimeSpan.FromMinutes(15);

    // Verified against when the email is unknown, so response time does not reveal which accounts exist
    private static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword("timing-equalizer");

    private readonly TimeTrackingDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly ITotpService _totpService;
    private readonly IEmailSender _email;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<AuthService> _logger;
    private readonly JwtSettings _jwtSettings;
    private readonly IMemoryCache _cache;

    public AuthService(
        TimeTrackingDbContext context,
        ITokenService tokenService,
        ITotpService totpService,
        IEmailSender email,
        EmailSettings emailSettings,
        ILogger<AuthService> logger,
        IOptions<JwtSettings> jwtSettings,
        IMemoryCache cache)
    {
        _cache = cache;
        _context = context;
        _tokenService = tokenService;
        _totpService = totpService;
        _email = email;
        _emailSettings = emailSettings;
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
            Email = request.Email.Trim(),
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

        _logger.LogInformation("User {UserId} registered", user.Id);
        await SendEmailVerificationAsync(user);

        return ServiceResult.Ok(await IssueTokensAsync(user));
    }

    public async Task<ServiceResult<object>> LoginAsync(LoginRequest request)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());

        // Always run one BCrypt verification and check the password before revealing anything
        // about the account (existence, deactivation)
        var passwordOk = BCrypt.Net.BCrypt.Verify(request.Password, user?.PasswordHash ?? DummyPasswordHash)
                         && user?.PasswordHash != null;

        if (user == null || !passwordOk)
            return ServiceResult.Unauthorized<object>("Invalid email or password");

        if (!user.IsActive)
            return ServiceResult.Unauthorized<object>("This account has been deactivated");

        user.LastLoginAt = DateTime.UtcNow;

        // 2FA enabled — hand out a short-lived token to submit with the TOTP code
        if (user.TwoFactorEnabled && !string.IsNullOrEmpty(user.TotpSecretKey))
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("2FA verification required for user {UserId}", user.Id);
            return ServiceResult.Ok<object>(new TwoFactorRequiredResponse
            {
                TwoFactorToken = _totpService.GenerateTwoFactorToken(user.Id)
            });
        }

        _logger.LogInformation("User {UserId} logged in", user.Id);

        return ServiceResult.Ok<object>(await IssueTokensAsync(user));
    }

    public async Task<ServiceResult<RefreshTokenResponse>> RefreshTokenAsync(string refreshToken)
    {
        var current = await _tokenService.ValidateRefreshTokenAsync(refreshToken);
        if (current == null)
            return ServiceResult.Unauthorized<RefreshTokenResponse>("Invalid or expired refresh token");

        var user = current.User;
        var (next, nextToken) = _tokenService.GenerateRefreshToken(user.Id);

        // Rotate: the old token is revoked in the same save. IsRevoked is a concurrency token,
        // so if two refreshes race with the same token only one of them succeeds.
        current.IsRevoked = true;
        current.RevokedAt = DateTime.UtcNow;
        current.ReplacedByToken = next.Token;
        _context.RefreshTokens.Add(next);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Unauthorized<RefreshTokenResponse>("Invalid or expired refresh token");
        }

        return ServiceResult.Ok(new RefreshTokenResponse
        {
            AccessToken = _tokenService.GenerateAccessToken(user),
            RefreshToken = nextToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpiryMinutes)
        });
    }

    public async Task<ServiceResult<AuthResponse>> LogoutAsync(int userId, string refreshToken)
    {
        // Prevent authenticated users from revoking tokens that belong to another account.
        var hash = TokenService.HashToken(refreshToken);
        var tokenOwnerId = await _context.RefreshTokens
            .AsNoTracking()
            .Where(rt => rt.Token == hash && !rt.IsRevoked)
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
        await RevokeAllSessionsAsync(user);
        await _context.SaveChangesAsync();
        _cache.Remove(Filters.ActiveUserFilter.CacheKey(userId));

        _logger.LogInformation("Password changed for user {UserId}", userId);

        return ServiceResult.Ok(new AuthResponse { Success = true, Message = "Password changed successfully. Please log in again." });
    }

    /// <summary>
    /// Deletes the account by anonymizing it: personal data (name, email, password, 2FA) is
    /// wiped and the login disabled, but time entries stay with the anonymized user, because
    /// working-time records must be retained for the organization. Owners must hand over or
    /// delete their organizations first so no organization is left without an owner.
    /// </summary>
    public async Task<ServiceResult<AuthResponse>> DeleteAccountAsync(int userId, string password)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return ServiceResult.NotFound<AuthResponse>("User not found");

        if (!VerifyPassword(user, password))
            return ServiceResult.BadRequest<AuthResponse>("Password is incorrect");

        var ownedOrgs = await _context.UserOrganizations
            .Where(uo => uo.UserId == userId && uo.Role == OrganizationRole.Owner)
            .Select(uo => uo.Organization.Name)
            .ToListAsync();
        if (ownedOrgs.Count > 0)
            return ServiceResult.BadRequest<AuthResponse>(
                $"You own {string.Join(", ", ownedOrgs)}. Transfer ownership or delete the organization first.");

        user.Email = $"deleted-{user.Id}@deleted.invalid";
        user.FirstName = "Deleted";
        user.LastName = "User";
        user.PasswordHash = null;
        user.ProfileImageUrl = null;
        user.EmailConfirmed = false;
        user.TwoFactorEnabled = false;
        user.TotpSecretKey = null;
        user.TwoFactorBackupCodes = null;
        user.TwoFactorEnabledAt = null;
        user.TotpLastStep = null;
        user.IsActive = false;
        user.LastLoginAt = null;
        user.UpdatedAt = DateTime.UtcNow;

        foreach (var membership in await _context.UserOrganizations.Where(uo => uo.UserId == userId).ToListAsync())
        {
            membership.IsActive = false;
            membership.BadgeId = null;
        }
        // Free-text the person wrote may name them; the request itself stays for the audit trail
        foreach (var request in await _context.OrgRequests.Where(r => r.UserId == userId).ToListAsync())
            request.Message = null;
        _context.Notifications.RemoveRange(_context.Notifications.Where(n => n.UserId == userId));
        _context.GroupMembers.RemoveRange(_context.GroupMembers.Where(gm => gm.UserId == userId));
        _context.UserTokens.RemoveRange(_context.UserTokens.Where(t => t.UserId == userId));
        await RevokeAllSessionsAsync(user);
        await _context.SaveChangesAsync();
        _cache.Remove(Filters.ActiveUserFilter.CacheKey(userId));

        _logger.LogInformation("User {UserId} deleted (anonymized) their account", userId);
        return ServiceResult.Ok(new AuthResponse { Success = true, Message = "Account deleted successfully" });
    }

    public async Task<ServiceResult<UserInfo>> GetCurrentUserAsync(int userId)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        return user == null
            ? ServiceResult.Unauthorized<UserInfo>("User not found.")
            : ServiceResult.Ok(await ToUserInfoAsync(user));
    }

    public async Task<ServiceResult<UserInfo>> UpdateProfileAsync(int userId, UpdateProfileRequest request)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return ServiceResult.NotFound<UserInfo>("User not found");

        // Changing the login email needs the password and a confirmation from the new address
        if (!string.IsNullOrWhiteSpace(request.Email) &&
            !request.Email.Trim().Equals(user.Email, StringComparison.OrdinalIgnoreCase))
        {
            if (request.CurrentPassword == null || !VerifyPassword(user, request.CurrentPassword))
                return ServiceResult.BadRequest<UserInfo>("Enter your current password to change your email address.");

            var newEmail = request.Email.Trim();
            var emailTaken = await _context.Users
                .AnyAsync(u => u.Email.ToLower() == newEmail.ToLower() && u.Id != userId);
            if (emailTaken)
                return ServiceResult.BadRequest<UserInfo>("Email is already in use by another account.");
            if (await RecentlyMailedAsync(user.Id, UserTokenPurpose.EmailChange))
                return ServiceResult.TooManyRequests<UserInfo>("A confirmation email was just sent. Please wait a few minutes before changing the address again.");

            await SendEmailChangeConfirmationAsync(user, newEmail);
        }

        if (!string.IsNullOrWhiteSpace(request.FirstName))
            user.FirstName = request.FirstName.Trim();

        if (!string.IsNullOrWhiteSpace(request.LastName))
            user.LastName = request.LastName.Trim();

        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} updated their profile", userId);

        return ServiceResult.Ok(await ToUserInfoAsync(user));
    }

    public async Task<ServiceResult<PersonalDataExport>> ExportPersonalDataAsync(int userId)
    {
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return ServiceResult.NotFound<PersonalDataExport>("User not found");

        return ServiceResult.Ok(new PersonalDataExport
        {
            Profile = await ToUserInfoAsync(user),
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            Memberships = await _context.UserOrganizations.IgnoreQueryFilters().AsNoTracking()
                .Where(uo => uo.UserId == userId)
                .Select(uo => new PersonalMembership(uo.Organization.Name, uo.Role.ToString(), uo.JoinedAt, uo.IsActive,
                    uo.VacationDaysPerYear, uo.InitialOvertimeHours, uo.BadgeId))
                .ToListAsync(),
            TimeEntries = await _context.TimeEntries.IgnoreQueryFilters().AsNoTracking()
                .Where(t => t.UserId == userId)
                .OrderBy(t => t.StartTime)
                .Select(t => new PersonalTimeEntry(t.Organization != null ? t.Organization.Name : null,
                    t.StartTime, t.EndTime, t.PauseDurationMinutes, t.Description, t.IsFailure))
                .ToListAsync(),
            Absences = await _context.AbsenceDays.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.UserId == userId)
                .OrderBy(a => a.Date)
                .Select(a => new PersonalAbsence(a.Organization.Name, a.Date, a.Type.ToString(), a.IsHalfDay, a.Note))
                .ToListAsync(),
            Requests = await _context.OrgRequests.IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.UserId == userId)
                .OrderBy(r => r.CreatedAt)
                .Select(r => new PersonalRequest(r.Organization.Name, r.Type.ToString(), r.Status.ToString(),
                    r.Message, r.RequestData, r.CreatedAt, r.RespondedAt))
                .ToListAsync(),
            Notifications = await _context.Notifications.AsNoTracking()
                .Where(n => n.UserId == userId)
                .OrderBy(n => n.CreatedAt)
                .Select(n => new PersonalNotification(n.Title, n.Message, n.CreatedAt, n.IsRead))
                .ToListAsync()
        });
    }

    // ── Email verification, email change, password reset ──

    public async Task<ServiceResult<AuthResponse>> VerifyEmailAsync(string token)
    {
        var userToken = await FindUsableTokenAsync(token, UserTokenPurpose.EmailVerification, UserTokenPurpose.EmailChange);
        if (userToken == null)
            return ServiceResult.BadRequest<AuthResponse>("This link is invalid or has expired.");

        var user = userToken.User;
        if (userToken.Purpose == UserTokenPurpose.EmailChange)
        {
            var taken = await _context.Users.AnyAsync(u => u.Email.ToLower() == userToken.NewEmail!.ToLower() && u.Id != user.Id);
            if (taken)
                return ServiceResult.BadRequest<AuthResponse>("Email is already in use by another account.");
            user.Email = userToken.NewEmail!;
        }

        user.EmailConfirmed = true;
        user.UpdatedAt = DateTime.UtcNow;
        userToken.UsedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Email confirmed for user {UserId} ({Purpose})", user.Id, userToken.Purpose);
        return ServiceResult.Ok(new AuthResponse { Success = true, Message = "Email address confirmed." });
    }

    public async Task<ServiceResult<AuthResponse>> ResendVerificationAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return ServiceResult.NotFound<AuthResponse>("User not found");
        if (user.EmailConfirmed)
            return ServiceResult.BadRequest<AuthResponse>("Your email address is already confirmed.");
        if (await RecentlyMailedAsync(user.Id, UserTokenPurpose.EmailVerification))
            return ServiceResult.TooManyRequests<AuthResponse>("A confirmation email was just sent. Please wait a few minutes before asking again.");

        await SendEmailVerificationAsync(user);
        await _context.SaveChangesAsync();
        return ServiceResult.Ok(new AuthResponse { Success = true, Message = "Confirmation email sent." });
    }

    public async Task<ServiceResult<AuthResponse>> ForgotPasswordAsync(string email)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.Trim().ToLower() && u.IsActive);
        if (user != null && !await RecentlyMailedAsync(user.Id, UserTokenPurpose.PasswordReset))
        {
            var token = CreateUserToken(user, UserTokenPurpose.PasswordReset, ResetTokenLifetime);
            await _context.SaveChangesAsync();
            await _email.SendAsync(user.Email, "Reset your Time Tracking password",
                $"Hello {user.FirstName},\n\nuse this link within the next hour to choose a new password:\n" +
                $"{_emailSettings.FrontendBaseUrl}/reset-password?token={Uri.EscapeDataString(token)}\n\n" +
                "If you did not ask for this, you can ignore this email.");
        }

        // Same answer whether or not the address exists (no account enumeration)
        return ServiceResult.Ok(new AuthResponse { Success = true, Message = "If an account exists for this address, a reset link is on its way." });
    }

    public async Task<ServiceResult<AuthResponse>> ResetPasswordAsync(ResetPasswordRequest request)
    {
        var userToken = await FindUsableTokenAsync(request.Token, UserTokenPurpose.PasswordReset);
        if (userToken == null || !userToken.User.IsActive)
            return ServiceResult.BadRequest<AuthResponse>("This link is invalid or has expired.");

        var passwordError = ValidatePasswordStrength(request.NewPassword);
        if (passwordError != null)
            return ServiceResult.BadRequest<AuthResponse>(passwordError);

        var user = userToken.User;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.EmailConfirmed = true;   // they proved control of the mailbox
        user.UpdatedAt = DateTime.UtcNow;
        userToken.UsedAt = DateTime.UtcNow;
        await RevokeAllSessionsAsync(user);
        await _context.SaveChangesAsync();
        _cache.Remove(Filters.ActiveUserFilter.CacheKey(user.Id));

        _logger.LogInformation("Password reset for user {UserId}", user.Id);
        return ServiceResult.Ok(new AuthResponse { Success = true, Message = "Your password has been changed. You can sign in now." });
    }

    // ── Two-Factor Authentication ──

    public async Task<ServiceResult<LoginResponse>> VerifyTwoFactorAsync(TwoFactorVerifyRequest request)
    {
        var token = _totpService.ValidateTwoFactorToken(request.TwoFactorToken);
        if (token == null)
            return ServiceResult.Unauthorized<LoginResponse>("Invalid or expired two-factor token");

        var user = await _context.Users.FindAsync(token.Value.UserId);
        if (user == null || !user.IsActive || !user.TwoFactorEnabled)
            return ServiceResult.Unauthorized<LoginResponse>("Invalid two-factor request");

        // A login token works once: after a successful 2FA login, older tokens are spent
        if (user.LastTwoFactorLoginAt is { } last && token.Value.IssuedAt <= last)
            return ServiceResult.Unauthorized<LoginResponse>("Invalid or expired two-factor token");

        if (user.TwoFactorLockedUntil > DateTime.UtcNow)
            return ServiceResult.TooManyRequests<LoginResponse>("Too many wrong codes. Try again in 15 minutes.");

        if (!CheckSecondFactor(user, request.Code))
        {
            if (++user.TwoFactorFailedAttempts >= MaxTwoFactorAttempts)
            {
                user.TwoFactorLockedUntil = DateTime.UtcNow + TwoFactorLockout;
                user.TwoFactorFailedAttempts = 0;
                _logger.LogWarning("2FA locked for user {UserId} after repeated wrong codes", user.Id);
            }
            await _context.SaveChangesAsync();
            return ServiceResult.Unauthorized<LoginResponse>("Invalid verification code");
        }

        user.TwoFactorFailedAttempts = 0;
        user.TwoFactorLockedUntil = null;
        user.LastTwoFactorLoginAt = DateTime.UtcNow;

        _logger.LogInformation("2FA verification successful for user {UserId}", user.Id);

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
        user.TotpLastStep = null;
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

        if (!_totpService.VerifyCode(user.TotpSecretKey, request.Code, null, out var step))
            return ServiceResult.BadRequest<TwoFactorConfirmResponse>("Invalid verification code. Make sure the code from your authenticator app is correct.");

        // Backup codes are shown once and stored only as BCrypt hashes
        var backupCodes = _totpService.GenerateBackupCodes();
        user.TwoFactorEnabled = true;
        user.TwoFactorEnabledAt = DateTime.UtcNow;
        user.TwoFactorBackupCodes = string.Join(',', backupCodes.Select(c => BCrypt.Net.BCrypt.HashPassword(c)));
        user.TotpLastStep = step;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("2FA enabled for user {UserId}", userId);

        return ServiceResult.Ok(new TwoFactorConfirmResponse
        {
            Enabled = true,
            BackupCodes = backupCodes
        });
    }

    public async Task<ServiceResult<AuthResponse>> DisableTwoFactorAsync(int userId, string currentPassword, string code)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return ServiceResult.NotFound<AuthResponse>("User not found");

        if (!user.TwoFactorEnabled)
            return ServiceResult.BadRequest<AuthResponse>("Two-factor authentication is not enabled");

        if (!VerifyPassword(user, currentPassword))
            return ServiceResult.BadRequest<AuthResponse>("Current password is incorrect");

        // A stolen session plus password is not enough: the second factor itself must agree
        if (user.TwoFactorLockedUntil > DateTime.UtcNow || !CheckSecondFactor(user, code))
            return ServiceResult.BadRequest<AuthResponse>("Invalid verification code");

        // Turning 2FA off would lock the user out of organizations that require it
        var requiringOrgs = await _context.UserOrganizations
            .Where(uo => uo.UserId == userId && uo.Organization.Require2fa)
            .Select(uo => uo.Organization.Name)
            .ToListAsync();
        if (requiringOrgs.Count > 0)
            return ServiceResult.BadRequest<AuthResponse>(
                $"{string.Join(", ", requiringOrgs)} require two-factor authentication. Leave them first, or ask an admin to turn the requirement off.");

        user.TwoFactorEnabled = false;
        user.TotpSecretKey = null;
        user.TwoFactorBackupCodes = null;
        user.TwoFactorEnabledAt = null;
        user.TotpLastStep = null;
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
        var (refreshToken, plaintext) = _tokenService.GenerateRefreshToken(user.Id);

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        return new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = plaintext,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpiryMinutes),
            User = await ToUserInfoAsync(user)
        };
    }

    /// <summary>Ends every session: refresh tokens revoked, access tokens invalid via the security stamp.</summary>
    private async Task RevokeAllSessionsAsync(User user)
    {
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        var tokens = await _context.RefreshTokens.Where(rt => rt.UserId == user.Id && !rt.IsRevoked).ToListAsync();
        foreach (var token in tokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = DateTime.UtcNow;
        }
    }

    /// <summary>TOTP first (each time step only once), then the one-time backup codes.</summary>
    private bool CheckSecondFactor(User user, string code)
    {
        if (_totpService.VerifyCode(user.TotpSecretKey!, code, user.TotpLastStep, out var step))
        {
            user.TotpLastStep = step;
            return true;
        }
        return ConsumeBackupCode(user, code);
    }

    /// <summary>One-time backup code check against the BCrypt hashes (legacy plaintext codes still work once).</summary>
    private static bool ConsumeBackupCode(User user, string code)
    {
        if (string.IsNullOrEmpty(user.TwoFactorBackupCodes)) return false;

        var normalized = code.Trim().ToLowerInvariant();
        var stored = user.TwoFactorBackupCodes.Split(',').ToList();
        var match = stored.FindIndex(s => s.StartsWith("$2")
            ? BCrypt.Net.BCrypt.Verify(normalized, s)
            : CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(s), System.Text.Encoding.UTF8.GetBytes(normalized)));
        if (match < 0) return false;

        stored.RemoveAt(match);
        user.TwoFactorBackupCodes = string.Join(',', stored);
        return true;
    }

    private string CreateUserToken(User user, UserTokenPurpose purpose, TimeSpan lifetime, string? newEmail = null)
    {
        // A fresh link invalidates earlier unused links of the same kind
        foreach (var old in _context.UserTokens.Where(t => t.UserId == user.Id && t.Purpose == purpose && t.UsedAt == null))
            old.UsedAt = DateTime.UtcNow;

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _context.UserTokens.Add(new UserToken
        {
            UserId = user.Id,
            Purpose = purpose,
            TokenHash = TokenService.HashToken(token),
            NewEmail = newEmail,
            ExpiresAt = DateTime.UtcNow.Add(lifetime)
        });
        return token;
    }

    private Task<bool> RecentlyMailedAsync(int userId, UserTokenPurpose purpose)
    {
        var since = DateTime.UtcNow - MailCooldown;
        return _context.UserTokens.AnyAsync(t => t.UserId == userId && t.Purpose == purpose && t.CreatedAt > since);
    }

    private async Task<UserToken?> FindUsableTokenAsync(string token, params UserTokenPurpose[] purposes)
    {
        var hash = TokenService.HashToken(token.Trim());
        var userToken = await _context.UserTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == hash);
        return userToken != null && purposes.Contains(userToken.Purpose) && userToken.UsedAt == null && userToken.ExpiresAt > DateTime.UtcNow
            ? userToken
            : null;
    }

    private async Task SendEmailVerificationAsync(User user)
    {
        var token = CreateUserToken(user, UserTokenPurpose.EmailVerification, EmailTokenLifetime);
        await _context.SaveChangesAsync();
        await _email.SendAsync(user.Email, "Confirm your email address",
            $"Hello {user.FirstName},\n\nplease confirm your email address for Time Tracking:\n" +
            $"{_emailSettings.FrontendBaseUrl}/verify-email?token={Uri.EscapeDataString(token)}");
    }

    private async Task SendEmailChangeConfirmationAsync(User user, string newEmail)
    {
        var token = CreateUserToken(user, UserTokenPurpose.EmailChange, EmailTokenLifetime, newEmail);
        await _context.SaveChangesAsync();
        await _email.SendAsync(newEmail, "Confirm your new email address",
            $"Hello {user.FirstName},\n\nconfirm this address to use it for signing in to Time Tracking:\n" +
            $"{_emailSettings.FrontendBaseUrl}/verify-email?token={Uri.EscapeDataString(token)}\n\n" +
            $"Until then you keep signing in with {user.Email}.");
    }

    private async Task<UserInfo> ToUserInfoAsync(User user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        FirstName = user.FirstName,
        LastName = user.LastName,
        ProfileImageUrl = user.ProfileImageUrl,
        EmailConfirmed = user.EmailConfirmed,
        TwoFactorEnabled = user.TwoFactorEnabled,
        PendingEmail = await _context.UserTokens
            .Where(t => t.UserId == user.Id && t.Purpose == UserTokenPurpose.EmailChange && t.UsedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => t.NewEmail)
            .FirstOrDefaultAsync()
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
