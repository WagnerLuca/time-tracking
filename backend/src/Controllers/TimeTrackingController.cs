using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

/// <summary>
/// Manages personal time entries: start, stop, history, update and delete.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
[EnableRateLimiting("General")]
public class TimeTrackingController : OrganizationBaseController
{
    private readonly ITimeTrackingService _service;
    private readonly ILogger<TimeTrackingController> _logger;

    public TimeTrackingController(ITimeTrackingService service, ILogger<TimeTrackingController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>Start a new time entry (clock in).</summary>
    [HttpPost("start")]
    [ProducesResponseType(typeof(TimeEntryResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Start([FromBody] StartTimeEntryRequest? request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToCreatedResponse(await _service.StartAsync(userId.Value, request));
    }

    /// <summary>Stop the currently running time entry (clock out).</summary>
    [HttpPost("stop")]
    [ProducesResponseType(typeof(TimeEntryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Stop([FromBody] StopTimeEntryRequest? request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.StopAsync(userId.Value, request));
    }

    /// <summary>Get the currently running time entry, if any.</summary>
    [HttpGet("current")]
    [ProducesResponseType(typeof(TimeEntryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrent()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetCurrentAsync(userId.Value));
    }

    /// <summary>My failure entries: timers stopped automatically at the business day end that still need a corrected end time.</summary>
    [HttpGet("failures")]
    [ProducesResponseType(typeof(List<TimeEntryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFailures()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetFailuresAsync(userId.Value));
    }

    /// <summary>Get past time entries with optional filters and pagination.</summary>
    /// <param name="organizationId">Filter by organization ID.</param>
    /// <param name="from">Start of date range.</param>
    /// <param name="to">End of date range.</param>
    /// <param name="limit">Max items per page (default 50, max 200).</param>
    /// <param name="offset">Number of items to skip.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<TimeEntryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(
        [FromQuery] int? organizationId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetHistoryAsync(userId.Value, organizationId, from, to, limit, offset));
    }

    /// <summary>Update an existing time entry.</summary>
    /// <param name="id">Time entry ID.</param>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(TimeEntryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTimeEntryRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.UpdateAsync(userId.Value, id, request));
    }

    /// <summary>Delete a time entry.</summary>
    /// <param name="id">Time entry ID.</param>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.DeleteAsync(userId.Value, id));
    }

    /// <summary>
    /// Add a finished entry by hand (e.g. a forgotten day). Follows the organization's "edit past
    /// entries" rule: Allowed creates it (<c>entry</c>), RequiresApproval creates an AddManualEntry
    /// request (<c>request</c>) that creates it on approval, Disabled is 403. Personal entries are always allowed.
    /// </summary>
    [HttpPost("manual")]
    [ProducesResponseType(typeof(ManualEntryResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateManual([FromBody] CreateManualEntryRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToCreatedResponse(await _service.CreateManualAsync(userId.Value, request));
    }

    /// <summary>Edit history of a time entry (owner, or admins who may see the owner's entries).</summary>
    /// <param name="id">Time entry ID (also works for deleted entries).</param>
    [HttpGet("{id}/history")]
    [ProducesResponseType(typeof(List<TimeEntryChangeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEntryHistory(int id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetEntryHistoryAsync(userId.Value, id));
    }
}
