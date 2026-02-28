using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

[ApiController]
[Route("api/organizations")]
public class AbsenceDayController : OrganizationBaseController
{
    private readonly IAbsenceDayService _service;

    public AbsenceDayController(IAbsenceDayService service)
    {
        _service = service;
    }

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

    [HttpPost("{slug}/absences")]
    [Authorize]
    [ProducesResponseType(typeof(AbsenceDayResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateAbsence(string slug, [FromBody] CreateAbsenceDayRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.CreateAbsenceAsync(slug, userId.Value, request));
    }

    [HttpPost("{slug}/absences/admin")]
    [Authorize]
    [ProducesResponseType(typeof(AbsenceDayResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AdminCreateAbsence(string slug, [FromBody] AdminCreateAbsenceDayRequest request)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();
        return ToResponse(await _service.AdminCreateAbsenceAsync(slug, callerId.Value, request));
    }

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
