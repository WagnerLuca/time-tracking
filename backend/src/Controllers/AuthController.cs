using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Register a new user
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var (success, message, response) = await _authService.RegisterAsync(request);

        if (!success)
        {
            return BadRequest(new AuthResponse { Success = false, Message = message });
        }

        return Ok(response);
    }

    /// <summary>
    /// Login with email and password
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var (success, message, response) = await _authService.LoginAsync(request);

        if (!success)
        {
            return Unauthorized(new AuthResponse { Success = false, Message = message });
        }

        return Ok(response);
    }

    /// <summary>
    /// Refresh access token using refresh token
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(RefreshTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        var (success, message, response) = await _authService.RefreshTokenAsync(request.RefreshToken);

        if (!success)
        {
            return Unauthorized(new AuthResponse { Success = false, Message = message });
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
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            return Unauthorized(new AuthResponse { Success = false, Message = "Invalid user" });
        }

        var (success, message) = await _authService.ChangePasswordAsync(userId, request);

        if (!success)
        {
            return BadRequest(new AuthResponse { Success = false, Message = message });
        }

        return Ok(new AuthResponse { Success = true, Message = message });
    }

    /// <summary>
    /// Get current authenticated user info
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserInfo), StatusCodes.Status200OK)]
    public IActionResult GetCurrentUser()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        var emailClaim = User.FindFirst(ClaimTypes.Email);
        var firstNameClaim = User.FindFirst("firstName");
        var lastNameClaim = User.FindFirst("lastName");

        if (userIdClaim == null || emailClaim == null)
        {
            return Unauthorized(new AuthResponse { Success = false, Message = "Invalid token" });
        }

        var userInfo = new UserInfo
        {
            Id = int.Parse(userIdClaim.Value),
            Email = emailClaim.Value,
            FirstName = firstNameClaim?.Value ?? "",
            LastName = lastNameClaim?.Value ?? "",
            EmailConfirmed = false // You could add this to claims if needed
        };

        return Ok(userInfo);
    }
}
