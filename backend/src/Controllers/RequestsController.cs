using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimeTracking.Api.Filters;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

/// <summary>
/// Manages organization requests (join, edit entries) and their notifications.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/organizations")]
[Authorize]
[EnableRateLimiting("General")]
public class RequestsController : OrganizationBaseController
{
    private readonly IRequestService _service;
    private readonly ILogger<RequestsController> _logger;

    public RequestsController(IRequestService service, ILogger<RequestsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>Create a new request for the organization (e.g. join, edit entry). No membership required for join requests.</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpPost("{slug}/requests")]
    [ProducesResponseType(typeof(OrgRequestResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateRequest(string slug, [FromBody] CreateOrgRequestRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToCreatedResponse(await _service.CreateRequestAsync(slug, userId.Value, request));
    }

    /// <summary>List requests for an organization with optional filters (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="type">Filter by request type.</param>
    /// <param name="status">Filter by request status.</param>
    /// <param name="limit">Max items per page (default 50, max 200).</param>
    /// <param name="offset">Number of items to skip.</param>
    [HttpGet("{slug}/requests")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(PaginatedResponse<OrgRequestResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRequests(
        string slug, [FromQuery] RequestType? type, [FromQuery] RequestStatus? status,
        [FromQuery] int limit = 50, [FromQuery] int offset = 0)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetRequestsAsync(slug, userId.Value, type, status, limit, offset));
    }

    /// <summary>Accept or decline a pending request (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="id">Request ID.</param>
    [HttpPut("{slug}/requests/{id}")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(OrgRequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RespondToRequest(
        string slug, int id, [FromBody] RespondToOrgRequestRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.RespondToRequestAsync(slug, userId.Value, id, request));
    }

    /// <summary>List the current user's requests across all organizations.</summary>
    /// <param name="type">Filter by request type.</param>
    /// <param name="limit">Max items per page (default 50, max 200).</param>
    /// <param name="offset">Number of items to skip.</param>
    [HttpGet("my-requests")]
    [ProducesResponseType(typeof(PaginatedResponse<OrgRequestResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyRequests(
        [FromQuery] RequestType? type,
        [FromQuery] int limit = 50, [FromQuery] int offset = 0)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetMyRequestsAsync(userId.Value, type, limit, offset));
    }

    /// <summary>Get pending request notifications for admins.</summary>
    [HttpGet("notifications")]
    [ProducesResponseType(typeof(AdminNotificationResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAdminNotifications()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetAdminNotificationsAsync(userId.Value));
    }

    /// <summary>Get responded request notifications for the current user.</summary>
    [HttpGet("user-notifications")]
    [ProducesResponseType(typeof(UserNotificationResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserNotifications()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetUserNotificationsAsync(userId.Value));
    }

    /// <summary>Mark request notifications as seen.</summary>
    /// <param name="requestIds">Optional list of specific request IDs to mark. If null, marks all.</param>
    [HttpPost("user-notifications/mark-seen")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkNotificationsSeen([FromBody] List<int>? requestIds = null)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.MarkNotificationsSeenAsync(userId.Value, requestIds));
    }
}
