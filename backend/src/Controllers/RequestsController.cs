using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

[ApiController]
[Route("api/organizations")]
public class RequestsController : OrganizationBaseController
{
    private readonly IRequestService _service;

    public RequestsController(IRequestService service)
    {
        _service = service;
    }

    [HttpPost("{slug}/requests")]
    [Authorize]
    [ProducesResponseType(typeof(OrgRequestResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateRequest(string slug, [FromBody] CreateOrgRequestRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToCreatedResponse(await _service.CreateRequestAsync(slug, userId.Value, request));
    }

    [HttpGet("{slug}/requests")]
    [Authorize]
    [ProducesResponseType(typeof(List<OrgRequestResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRequests(
        string slug, [FromQuery] RequestType? type, [FromQuery] RequestStatus? status)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetRequestsAsync(slug, userId.Value, type, status));
    }

    [HttpPut("{slug}/requests/{id}")]
    [Authorize]
    [ProducesResponseType(typeof(OrgRequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RespondToRequest(
        string slug, int id, [FromBody] RespondToOrgRequestRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.RespondToRequestAsync(slug, userId.Value, id, request));
    }

    [HttpGet("my-requests")]
    [Authorize]
    [ProducesResponseType(typeof(List<OrgRequestResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyRequests([FromQuery] RequestType? type)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetMyRequestsAsync(userId.Value, type));
    }

    [HttpGet("notifications")]
    [Authorize]
    [ProducesResponseType(typeof(AdminNotificationResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAdminNotifications()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetAdminNotificationsAsync(userId.Value));
    }

    [HttpGet("user-notifications")]
    [Authorize]
    [ProducesResponseType(typeof(UserNotificationResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserNotifications()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetUserNotificationsAsync(userId.Value));
    }

    [HttpPost("user-notifications/mark-seen")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkNotificationsSeen([FromBody] List<int>? requestIds = null)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.MarkNotificationsSeenAsync(userId.Value, requestIds));
    }
}
