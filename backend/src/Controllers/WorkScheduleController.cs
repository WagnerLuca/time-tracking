using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

[ApiController]
[Route("api/organizations")]
public class WorkScheduleController : OrganizationBaseController
{
    private readonly IWorkScheduleService _service;

    public WorkScheduleController(IWorkScheduleService service)
    {
        _service = service;
    }

    // ── Self endpoints ──

    [HttpGet("{slug}/work-schedule")]
    [Authorize]
    [ProducesResponseType(typeof(WorkScheduleResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyWorkSchedule(string slug)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetMyWorkScheduleAsync(slug, userId.Value));
    }

    [HttpGet("{slug}/work-schedules")]
    [Authorize]
    [ProducesResponseType(typeof(List<WorkScheduleResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyWorkSchedules(string slug)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetMyWorkSchedulesAsync(slug, userId.Value));
    }

    [HttpPost("{slug}/work-schedules")]
    [Authorize]
    [ProducesResponseType(typeof(WorkScheduleResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateMyWorkSchedule(
        string slug, [FromBody] CreateWorkScheduleRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.CreateMyWorkScheduleAsync(slug, userId.Value, request));
    }

    [HttpPut("{slug}/work-schedules/{id}")]
    [Authorize]
    [ProducesResponseType(typeof(WorkScheduleResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMyWorkSchedule(
        string slug, int id, [FromBody] UpdateWorkScheduleRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.UpdateMyWorkScheduleAsync(slug, userId.Value, id, request));
    }

    [HttpDelete("{slug}/work-schedules/{id}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteMyWorkSchedule(string slug, int id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.DeleteMyWorkScheduleAsync(slug, userId.Value, id));
    }

    [HttpPut("{slug}/initial-overtime")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMyInitialOvertime(
        string slug, [FromBody] SetInitialOvertimeRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.UpdateMyInitialOvertimeAsync(slug, userId.Value, request));
    }

    // ── Admin endpoints ──

    [HttpGet("{slug}/members/{memberId}/work-schedule")]
    [Authorize]
    [ProducesResponseType(typeof(WorkScheduleResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMemberWorkSchedule(string slug, int memberId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetMemberWorkScheduleAsync(slug, userId.Value, memberId));
    }

    [HttpGet("{slug}/members/{memberId}/work-schedules")]
    [Authorize]
    [ProducesResponseType(typeof(List<WorkScheduleResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMemberWorkSchedules(string slug, int memberId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetMemberWorkSchedulesAsync(slug, userId.Value, memberId));
    }

    [HttpPost("{slug}/members/{memberId}/work-schedules")]
    [Authorize]
    [ProducesResponseType(typeof(WorkScheduleResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateMemberWorkSchedule(
        string slug, int memberId, [FromBody] CreateWorkScheduleRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.CreateMemberWorkScheduleAsync(slug, userId.Value, memberId, request));
    }

    [HttpPut("{slug}/members/{memberId}/work-schedules/{id}")]
    [Authorize]
    [ProducesResponseType(typeof(WorkScheduleResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMemberWorkSchedule(
        string slug, int memberId, int id, [FromBody] UpdateWorkScheduleRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.UpdateMemberWorkScheduleAsync(slug, userId.Value, memberId, id, request));
    }

    [HttpDelete("{slug}/members/{memberId}/work-schedules/{id}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteMemberWorkSchedule(string slug, int memberId, int id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.DeleteMemberWorkScheduleAsync(slug, userId.Value, memberId, id));
    }
}
