using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
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
    [EnableRateLimiting("AuthStrict")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var (success, message, response) = await _authService.LoginAsync(request);

        if (!success)
        {
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
    [Authorize]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
    {
        var (success, message) = await _authService.RevokeRefreshTokenAsync(request.RefreshToken);

        return Ok(new AuthResponse { Success = success, Message = message });
    }

    /// <summary>
    /// Change password for authenticated user
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
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
    [Authorize]
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
    [Authorize]
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
            EmailConfirmed = user.EmailConfirmed
        };

        return Ok(userInfo);
    }
}
