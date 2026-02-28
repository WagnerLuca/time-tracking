using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

/// <summary>
/// Manages absence days (sick days, vacation, etc.) for organization members.
/// </summary>
[ApiController]
[Route("api/organizations")]
public class AbsenceDayController : OrganizationBaseController
{
    private readonly IAbsenceDayService _service;

    public AbsenceDayController(IAbsenceDayService service)
    {
        _service = service;
    }

    /// <summary>List absences for the organization with optional filters.</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="userId">Filter by user ID.</param>
    /// <param name="from">Start of date range.</param>
    /// <param name="to">End of date range.</param>
    [HttpGet("{slug}/absences")]
    [Authorize]
    [ProducesResponseType(typeof(List<AbsenceDayResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAbsences(
        string slug, [FromQuery] int? userId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();
        return ToResponse(await _service.GetAbsencesAsync(slug, callerId.Value, userId, from, to));
    }

    /// <summary>Create an absence day for the current user.</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpPost("{slug}/absences")]
    [Authorize]
    [ProducesResponseType(typeof(AbsenceDayResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateAbsence(string slug, [FromBody] CreateAbsenceDayRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.CreateAbsenceAsync(slug, userId.Value, request));
    }

    /// <summary>Create an absence day for any member (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpPost("{slug}/absences/admin")]
    [Authorize]
    [ProducesResponseType(typeof(AbsenceDayResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AdminCreateAbsence(string slug, [FromBody] AdminCreateAbsenceDayRequest request)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();
        return ToResponse(await _service.AdminCreateAbsenceAsync(slug, callerId.Value, request));
    }

    /// <summary>Delete an absence day.</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="id">Absence day ID.</param>
    [HttpDelete("{slug}/absences/{id}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAbsence(string slug, int id)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();
        return ToResponse(await _service.DeleteAbsenceAsync(slug, callerId.Value, id));
    }
}
