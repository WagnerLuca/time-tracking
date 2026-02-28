using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TimeTrackingController : OrganizationBaseController
{
    private readonly ITimeTrackingService _service;

    public TimeTrackingController(ITimeTrackingService service)
    {
        _service = service;
    }

    [HttpPost("start")]
    [ProducesResponseType(typeof(TimeEntryResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Start([FromBody] StartTimeEntryRequest? request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToCreatedResponse(await _service.StartAsync(userId.Value, request));
    }

    [HttpPost("stop")]
    [ProducesResponseType(typeof(TimeEntryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Stop([FromBody] StopTimeEntryRequest? request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.StopAsync(userId.Value, request));
    }

    [HttpGet("current")]
    [ProducesResponseType(typeof(TimeEntryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrent()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetCurrentAsync(userId.Value));
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<TimeEntryResponse>), StatusCodes.Status200OK)]
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

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(TimeEntryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTimeEntryRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.UpdateAsync(userId.Value, id, request));
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.DeleteAsync(userId.Value, id));
    }
}
