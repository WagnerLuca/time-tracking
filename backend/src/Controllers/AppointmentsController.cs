using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimeTracking.Api.Filters;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

/// <summary>Planned appointments ("Termine", issue #83) for the team calendar: information only, exempt from core-hours checks.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/organizations/{slug}/appointments")]
[Authorize]
[EnableRateLimiting("General")]
[RequireOrgRole]
public class AppointmentsController(AppointmentService service) : OrganizationBaseController
{
    /// <summary>The visible appointments in [from, to] (note null where hidden), optionally of one member or a group and its sub-groups.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<AppointmentResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(string slug, [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] int? userId = null, [FromQuery] int? groupId = null)
    {
        if (GetCurrentUserId() is not int callerId) return Unauthorized();
        return ToResponse(await service.ListAsync(slug, callerId, from, to, userId, groupId));
    }

    /// <summary>Add an appointment for yourself, or (org admin / group admin approving absences) for a member.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(string slug, [FromBody] SaveAppointmentRequest request)
    {
        if (GetCurrentUserId() is not int callerId) return Unauthorized();
        return ToCreatedResponse(await service.CreateAsync(slug, callerId, request));
    }

    /// <summary>Change an appointment (date, times, note; the owner stays).</summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(string slug, int id, [FromBody] SaveAppointmentRequest request)
    {
        if (GetCurrentUserId() is not int callerId) return Unauthorized();
        return ToResponse(await service.UpdateAsync(slug, callerId, id, request));
    }

    /// <summary>Delete an appointment.</summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(string slug, int id)
    {
        if (GetCurrentUserId() is not int callerId) return Unauthorized();
        return ToResponse(await service.DeleteAsync(slug, callerId, id));
    }
}
