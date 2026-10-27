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
/// Work schedules (issue #82: current, at most one future one, read-only history) and initial overtime: self-service and a
/// member's (admins). Past periods are never edited or deleted; see <see cref="WorkSchedulePlan"/>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/organizations")]
[Authorize]
[EnableRateLimiting("General")]
public class WorkScheduleController : OrganizationBaseController
{
    private readonly IWorkScheduleService _service;

    public WorkScheduleController(IWorkScheduleService service) => _service = service;

    // ── Self endpoints ──

    /// <summary>The current user's current work schedule (an empty one with id 0 without).</summary>
    [HttpGet("{slug}/work-schedule")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(WorkScheduleResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyWorkSchedule(string slug) =>
        GetCurrentUserId() is int userId ? ToResponse(await _service.GetMyWorkScheduleAsync(slug, userId)) : Unauthorized();

    /// <summary>Every period of the current user's work schedule (read-only; the calendars' targets).</summary>
    [HttpGet("{slug}/work-schedules")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(List<WorkScheduleResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyWorkSchedules(string slug) =>
        GetCurrentUserId() is int userId ? ToResponse(await _service.GetMyWorkSchedulesAsync(slug, userId)) : Unauthorized();

    /// <summary>The current user's schedule as current / future / history (issue #82).</summary>
    [HttpGet("{slug}/work-schedule/plan")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(WorkSchedulePlanResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyPlan(string slug) =>
        GetCurrentUserId() is int userId ? ToResponse(await _service.GetMyPlanAsync(slug, userId)) : Unauthorized();

    /// <summary>
    /// A new schedule from a day (default today; members today or later, admins any open day): the period before it ends the
    /// day before; an existing future one is replaced only with <c>replaceFuture</c> (else 409 <c>schedule.futureExists</c>).
    /// Only while the effective rule allows schedule changes (RequiresApproval: send a WorkScheduleChange request).
    /// </summary>
    [HttpPost("{slug}/work-schedule/change")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(WorkSchedulePlanResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeMyWorkSchedule(string slug, [FromBody] ChangeWorkScheduleRequest request) =>
        GetCurrentUserId() is int userId ? ToResponse(await _service.ChangeMyWorkScheduleAsync(slug, userId, request)) : Unauthorized();

    /// <summary>Cancels the current user's future schedule (the current one runs on); only while the rule allows changes.</summary>
    [HttpDelete("{slug}/work-schedule/future")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(WorkSchedulePlanResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelMyFuture(string slug) =>
        GetCurrentUserId() is int userId ? ToResponse(await _service.CancelMyFutureAsync(slug, userId)) : Unauthorized();

    /// <summary>Update the current user's initial overtime hours.</summary>
    [HttpPut("{slug}/initial-overtime")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(MemberInitialOvertimeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMyInitialOvertime(string slug, [FromBody] SetInitialOvertimeRequest request) =>
        GetCurrentUserId() is int userId ? ToResponse(await _service.UpdateMyInitialOvertimeAsync(slug, userId, request)) : Unauthorized();

    // ── A member's (reading: admins, or per the member visibility matrix; writing: admins) ──

    /// <summary>A member's current work schedule (admins, or per the member visibility matrix).</summary>
    [HttpGet("{slug}/members/{memberId}/work-schedule")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(WorkScheduleResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMemberWorkSchedule(string slug, int memberId) =>
        GetCurrentUserId() is int userId ? ToResponse(await _service.GetMemberWorkScheduleAsync(slug, userId, memberId)) : Unauthorized();

    /// <summary>Every period of a member's work schedule (read-only; admins, or per the member visibility matrix).</summary>
    [HttpGet("{slug}/members/{memberId}/work-schedules")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(List<WorkScheduleResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMemberWorkSchedules(string slug, int memberId) =>
        GetCurrentUserId() is int userId ? ToResponse(await _service.GetMemberWorkSchedulesAsync(slug, userId, memberId)) : Unauthorized();

    /// <summary>A member's schedule as current / future / history (same access as reading it).</summary>
    [HttpGet("{slug}/members/{memberId}/work-schedule/plan")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(WorkSchedulePlanResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMemberPlan(string slug, int memberId) =>
        GetCurrentUserId() is int userId ? ToResponse(await _service.GetMemberPlanAsync(slug, userId, memberId)) : Unauthorized();

    /// <summary>An admin's "change from a day" for a member: any open day (409 <c>schedule.startInClosedMonth</c> in a closed month).</summary>
    [HttpPost("{slug}/members/{memberId}/work-schedule/change")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(WorkSchedulePlanResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeMemberWorkSchedule(string slug, int memberId, [FromBody] ChangeWorkScheduleRequest request) =>
        GetCurrentUserId() is int userId ? ToResponse(await _service.ChangeMemberWorkScheduleAsync(slug, userId, memberId, request)) : Unauthorized();

    /// <summary>
    /// An admin's correction of a member's current schedule (typos): targets and optionally its first day, retroactively —
    /// never into a closed month (409 <c>schedule.correctionClosed</c>).
    /// </summary>
    [HttpPut("{slug}/members/{memberId}/work-schedule/current")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(WorkSchedulePlanResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CorrectMemberWorkSchedule(string slug, int memberId, [FromBody] CorrectWorkScheduleRequest request) =>
        GetCurrentUserId() is int userId ? ToResponse(await _service.CorrectMemberWorkScheduleAsync(slug, userId, memberId, request)) : Unauthorized();

    /// <summary>An admin cancels a member's future schedule.</summary>
    [HttpDelete("{slug}/members/{memberId}/work-schedule/future")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(WorkSchedulePlanResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelMemberFuture(string slug, int memberId) =>
        GetCurrentUserId() is int userId ? ToResponse(await _service.CancelMemberFutureAsync(slug, userId, memberId)) : Unauthorized();
}
