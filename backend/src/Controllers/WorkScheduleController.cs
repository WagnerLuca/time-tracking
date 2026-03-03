using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

/// <summary>
/// Manages work schedules and initial overtime for organization members.
/// Includes self-service endpoints and admin endpoints for managing other members.
/// </summary>
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

    /// <summary>Get the current user's active work schedule.</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpGet("{slug}/work-schedule")]
    [Authorize]
    [ProducesResponseType(typeof(WorkScheduleResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyWorkSchedule(string slug)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetMyWorkScheduleAsync(slug, userId.Value));
    }

    /// <summary>List all work schedules for the current user.</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpGet("{slug}/work-schedules")]
    [Authorize]
    [ProducesResponseType(typeof(List<WorkScheduleResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyWorkSchedules(string slug)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetMyWorkSchedulesAsync(slug, userId.Value));
    }

    /// <summary>Create a new work schedule for the current user.</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpPost("{slug}/work-schedules")]
    [Authorize]
    [ProducesResponseType(typeof(WorkScheduleResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateMyWorkSchedule(
        string slug, [FromBody] CreateWorkScheduleRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToCreatedResponse(await _service.CreateMyWorkScheduleAsync(slug, userId.Value, request));
    }

    /// <summary>Update a work schedule for the current user.</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="id">Work schedule ID.</param>
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

    /// <summary>Delete a work schedule for the current user.</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="id">Work schedule ID.</param>
    [HttpDelete("{slug}/work-schedules/{id}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteMyWorkSchedule(string slug, int id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.DeleteMyWorkScheduleAsync(slug, userId.Value, id));
    }

    /// <summary>Update the current user's initial overtime hours.</summary>
    /// <param name="slug">Organization URL slug.</param>
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

    /// <summary>Get a specific member's active work schedule (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="memberId">Member ID.</param>
    [HttpGet("{slug}/members/{memberId}/work-schedule")]
    [Authorize]
    [ProducesResponseType(typeof(WorkScheduleResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMemberWorkSchedule(string slug, int memberId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetMemberWorkScheduleAsync(slug, userId.Value, memberId));
    }

    /// <summary>List all work schedules for a specific member (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="memberId">Member ID.</param>
    [HttpGet("{slug}/members/{memberId}/work-schedules")]
    [Authorize]
    [ProducesResponseType(typeof(List<WorkScheduleResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMemberWorkSchedules(string slug, int memberId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetMemberWorkSchedulesAsync(slug, userId.Value, memberId));
    }

    /// <summary>Create a work schedule for a specific member (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="memberId">Member ID.</param>
    [HttpPost("{slug}/members/{memberId}/work-schedules")]
    [Authorize]
    [ProducesResponseType(typeof(WorkScheduleResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateMemberWorkSchedule(
        string slug, int memberId, [FromBody] CreateWorkScheduleRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToCreatedResponse(await _service.CreateMemberWorkScheduleAsync(slug, userId.Value, memberId, request));
    }

    /// <summary>Update a work schedule for a specific member (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="memberId">Member ID.</param>
    /// <param name="id">Work schedule ID.</param>
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

    /// <summary>Delete a work schedule for a specific member (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="memberId">Member ID.</param>
    /// <param name="id">Work schedule ID.</param>
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
