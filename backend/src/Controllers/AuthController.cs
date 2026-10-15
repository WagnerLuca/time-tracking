using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

/// <summary>
/// Thin HTTP layer over <see cref="IAuthService"/>: every action maps a
/// ServiceResult to a response via <see cref="OrganizationBaseController"/>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
[EnableRateLimiting("General")]   // per-user baseline; sensitive actions below are stricter
public class AuthController : OrganizationBaseController
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Register a new user
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthStrict")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        => ToResponse(await _authService.RegisterAsync(request));

    /// <summary>
    /// Login with email and password. With 2FA enabled the result carries only <c>requiresTwoFactor</c> + <c>twoFactorToken</c>.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthStrict")]
    [ProducesResponseType(typeof(LoginResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
        => ToResponse(await _authService.LoginAsync(request));

    /// <summary>
    /// Refresh access token using refresh token
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthModerate")]
    [ProducesResponseType(typeof(RefreshTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
        => ToResponse(await _authService.RefreshTokenAsync(request.RefreshToken));

    /// <summary>
    /// Logout by revoking refresh token
    /// </summary>
    [HttpPost("logout")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
    {
        if (GetCurrentUserId() is not int userId) return Unauthorized();
        return ToResponse(await _authService.LogoutAsync(userId, request.RefreshToken));
    }

    /// <summary>
    /// Change password for authenticated user
    /// </summary>
    [HttpPost("change-password")]
    [EnableRateLimiting("AuthStrict")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (GetCurrentUserId() is not int userId) return Unauthorized();
        return ToResponse(await _authService.ChangePasswordAsync(userId, request));
    }

    /// <summary>
    /// Delete the authenticated user's account (requires the password). Personal data is
    /// anonymized; time entries stay with the organization as required for working-time records.
    /// </summary>
    [HttpPost("account/delete")]
    [EnableRateLimiting("AuthStrict")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequest request)
    {
        if (GetCurrentUserId() is not int userId) return Unauthorized();
        return ToResponse(await _authService.DeleteAccountAsync(userId, request.Password));
    }

    /// <summary>Confirm an email address (after registration or an email change) with the token from the mail.</summary>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthModerate")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request)
        => ToResponse(await _authService.VerifyEmailAsync(request.Token));

    /// <summary>Send the email confirmation link again.</summary>
    [HttpPost("verify-email/resend")]
    [EnableRateLimiting("AuthStrict")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendVerification()
    {
        if (GetCurrentUserId() is not int userId) return Unauthorized();
        return ToResponse(await _authService.ResendVerificationAsync(userId));
    }

    /// <summary>Mail a password reset link. Always answers the same, whether or not the account exists.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthStrict")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        => ToResponse(await _authService.ForgotPasswordAsync(request.Email));

    /// <summary>Set a new password with the token from a reset link; ends all sessions.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthStrict")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        => ToResponse(await _authService.ResetPasswordAsync(request));

    /// <summary>
    /// Get current authenticated user info
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserInfo), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentUser()
    {
        if (GetCurrentUserId() is not int userId) return Unauthorized();
        return ToResponse(await _authService.GetCurrentUserAsync(userId));
    }

    /// <summary>Update the current user's profile (name, email).</summary>
    [HttpPut("profile")]
    [EnableRateLimiting("AuthStrict")]
    [ProducesResponseType(typeof(UserInfo), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        if (GetCurrentUserId() is not int userId) return Unauthorized();
        return ToResponse(await _authService.UpdateProfileAsync(userId, request));
    }

    /// <summary>Download everything stored about you (GDPR Art. 15/20) as JSON.</summary>
    [HttpGet("me/export")]
    [EnableRateLimiting("AuthModerate")]
    [ProducesResponseType(typeof(PersonalDataExport), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportPersonalData()
    {
        if (GetCurrentUserId() is not int userId) return Unauthorized();
        var result = await _authService.ExportPersonalDataAsync(userId);
        if (!result.IsSuccess) return ToResponse(result);
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(result.Data,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true });
        return File(json, "application/json", $"time-tracking-data-{DateTime.UtcNow:yyyy-MM-dd}.json");
    }

    // ── Two-Factor Authentication Endpoints ──

    /// <summary>
    /// Verify TOTP code to complete login when 2FA is enabled
    /// </summary>
    [HttpPost("2fa/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthStrict")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifyTwoFactor([FromBody] TwoFactorVerifyRequest request)
        => ToResponse(await _authService.VerifyTwoFactorAsync(request));

    /// <summary>
    /// Begin 2FA setup — generates a TOTP secret and returns the authenticator URI
    /// </summary>
    [HttpPost("2fa/setup")]
    [EnableRateLimiting("AuthStrict")]
    [ProducesResponseType(typeof(TwoFactorSetupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetupTwoFactor()
    {
        if (GetCurrentUserId() is not int userId) return Unauthorized();
        return ToResponse(await _authService.SetupTwoFactorAsync(userId));
    }

    /// <summary>
    /// Confirm 2FA setup by providing a valid TOTP code from the authenticator app
    /// </summary>
    [HttpPost("2fa/confirm")]
    [EnableRateLimiting("AuthStrict")]
    [ProducesResponseType(typeof(TwoFactorConfirmResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmTwoFactor([FromBody] TwoFactorConfirmRequest request)
    {
        if (GetCurrentUserId() is not int userId) return Unauthorized();
        return ToResponse(await _authService.ConfirmTwoFactorAsync(userId, request));
    }

    /// <summary>
    /// Disable 2FA for the authenticated user (requires current password)
    /// </summary>
    [HttpPost("2fa/disable")]
    [EnableRateLimiting("AuthStrict")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DisableTwoFactor([FromBody] DisableTwoFactorRequest request)
    {
        if (GetCurrentUserId() is not int userId) return Unauthorized();
        return ToResponse(await _authService.DisableTwoFactorAsync(userId, request.CurrentPassword, request.Code));
    }
}
