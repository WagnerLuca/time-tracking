using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;
    private readonly TimeTrackingDbContext _context;

    public AuthController(IAuthService authService, ILogger<AuthController> logger, TimeTrackingDbContext context)
    {
        _authService = authService;
        _logger = logger;
        _context = context;
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
    {
        var (success, message, response) = await _authService.RegisterAsync(request);

        if (!success)
        {
            return Problem(
                type: "https://httpstatuses.io/400",
                title: "Registration Failed",
                statusCode: StatusCodes.Status400BadRequest,
                detail: message);
        }

        return Ok(response);
    }

    /// <summary>
    /// Login with email and password
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthStrict")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var (success, message, response) = await _authService.LoginAsync(request);

        if (!success)
        {
            // Two-factor authentication required — return 200 with 2FA token
            if (message == "TwoFactorRequired" && response != null)
            {
                return Ok(new TwoFactorRequiredResponse
                {
                    TwoFactorToken = response.RefreshToken
                });
            }

            return Problem(
                type: "https://httpstatuses.io/401",
                title: "Authentication Failed",
                statusCode: StatusCodes.Status401Unauthorized,
                detail: message);
        }

        return Ok(response);
    }

    /// <summary>
    /// Refresh access token using refresh token
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthModerate")]
    [ProducesResponseType(typeof(RefreshTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        var (success, message, response) = await _authService.RefreshTokenAsync(request.RefreshToken);

        if (!success)
        {
            return Problem(
                type: "https://httpstatuses.io/401",
                title: "Token Refresh Failed",
                statusCode: StatusCodes.Status401Unauthorized,
                detail: message);
        }

        return Ok(response);
    }

    /// <summary>
    /// Logout by revoking refresh token
    /// </summary>
    [HttpPost("logout")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            return Problem(
                type: "https://httpstatuses.io/401",
                title: "Unauthorized",
                statusCode: StatusCodes.Status401Unauthorized,
                detail: "Invalid user token.");
        }

        // Prevent authenticated users from revoking tokens that belong to another account.
        var tokenOwnerId = await _context.RefreshTokens
            .AsNoTracking()
            .Where(rt => rt.Token == request.RefreshToken && !rt.IsRevoked)
            .Select(rt => (int?)rt.UserId)
            .FirstOrDefaultAsync();

        if (tokenOwnerId.HasValue && tokenOwnerId.Value != userId)
        {
            return Problem(
                type: "https://httpstatuses.io/403",
                title: "Forbidden",
                statusCode: StatusCodes.Status403Forbidden,
                detail: "You can only revoke your own refresh tokens.");
        }

        var (success, message) = await _authService.RevokeRefreshTokenAsync(request.RefreshToken);

        return Ok(new AuthResponse { Success = success, Message = message });
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
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            return Problem(
                type: "https://httpstatuses.io/401",
                title: "Unauthorized",
                statusCode: StatusCodes.Status401Unauthorized,
                detail: "Invalid user token.");
        }

        var (success, message) = await _authService.ChangePasswordAsync(userId, request);

        if (!success)
        {
            return Problem(
                type: "https://httpstatuses.io/400",
                title: "Password Change Failed",
                statusCode: StatusCodes.Status400BadRequest,
                detail: message);
        }

        return Ok(new AuthResponse { Success = true, Message = message });
    }

    /// <summary>
    /// Delete the authenticated user's account and all associated data
    /// </summary>
    [HttpDelete("account")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteAccount()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            return Problem(
                type: "https://httpstatuses.io/401",
                title: "Unauthorized",
                statusCode: StatusCodes.Status401Unauthorized,
                detail: "Invalid user token.");
        }

        var (success, message) = await _authService.DeleteAccountAsync(userId);

        if (!success)
        {
            return Problem(
                type: "https://httpstatuses.io/400",
                title: "Account Deletion Failed",
                statusCode: StatusCodes.Status400BadRequest,
                detail: message);
        }

        return Ok(new AuthResponse { Success = true, Message = message });
    }

    /// <summary>
    /// Get current authenticated user info
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserInfo), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);

        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            return Problem(
                type: "https://httpstatuses.io/401",
                title: "Unauthorized",
                statusCode: StatusCodes.Status401Unauthorized,
                detail: "Invalid token.");
        }

        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return Problem(
                type: "https://httpstatuses.io/401",
                title: "Unauthorized",
                statusCode: StatusCodes.Status401Unauthorized,
                detail: "User not found.");
        }

        var userInfo = new UserInfo
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            ProfileImageUrl = user.ProfileImageUrl,
            EmailConfirmed = user.EmailConfirmed,
            TwoFactorEnabled = user.TwoFactorEnabled
        };

        return Ok(userInfo);
    }

    /// <summary>Update the current user's profile (name, email).</summary>
    [HttpPut("profile")]
    [ProducesResponseType(typeof(UserInfo), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            return Problem(
                type: "https://httpstatuses.io/401",
                title: "Unauthorized",
                statusCode: StatusCodes.Status401Unauthorized,
                detail: "Invalid token.");
        }

        var (success, message, user) = await _authService.UpdateProfileAsync(userId, request);
        if (!success)
        {
            if (message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                return Problem(
                    type: "https://httpstatuses.io/404",
                    title: "Not Found",
                    statusCode: StatusCodes.Status404NotFound,
                    detail: message);

            return Problem(
                type: "https://httpstatuses.io/400",
                title: "Bad Request",
                statusCode: StatusCodes.Status400BadRequest,
                detail: message);
        }

        return Ok(user);
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
    {
        var (success, message, response) = await _authService.VerifyTwoFactorAsync(request);

        if (!success)
        {
            return Problem(
                type: "https://httpstatuses.io/401",
                title: "Two-Factor Verification Failed",
                statusCode: StatusCodes.Status401Unauthorized,
                detail: message);
        }

        return Ok(response);
    }

    /// <summary>
    /// Begin 2FA setup — generates a TOTP secret and returns the authenticator URI
    /// </summary>
    [HttpPost("2fa/setup")]
    [ProducesResponseType(typeof(TwoFactorSetupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetupTwoFactor()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            return Unauthorized();

        var (success, message, response) = await _authService.SetupTwoFactorAsync(userId);
        if (!success)
        {
            return Problem(
                type: "https://httpstatuses.io/400",
                title: "Two-Factor Setup Failed",
                statusCode: StatusCodes.Status400BadRequest,
                detail: message);
        }

        return Ok(response);
    }

    /// <summary>
    /// Confirm 2FA setup by providing a valid TOTP code from the authenticator app
    /// </summary>
    [HttpPost("2fa/confirm")]
    [ProducesResponseType(typeof(TwoFactorConfirmResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmTwoFactor([FromBody] TwoFactorConfirmRequest request)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            return Unauthorized();

        var (success, message, response) = await _authService.ConfirmTwoFactorAsync(userId, request);
        if (!success)
        {
            return Problem(
                type: "https://httpstatuses.io/400",
                title: "Two-Factor Confirmation Failed",
                statusCode: StatusCodes.Status400BadRequest,
                detail: message);
        }

        return Ok(response);
    }

    /// <summary>
    /// Disable 2FA for the authenticated user (requires current password)
    /// </summary>
    [HttpPost("2fa/disable")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DisableTwoFactor([FromBody] ChangePasswordRequest request)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            return Unauthorized();

        var (success, message) = await _authService.DisableTwoFactorAsync(userId, request.CurrentPassword);
        if (!success)
        {
            return Problem(
                type: "https://httpstatuses.io/400",
                title: "Failed to Disable 2FA",
                statusCode: StatusCodes.Status400BadRequest,
                detail: message);
        }

        return Ok(new AuthResponse { Success = true, Message = message });
    }
}
