using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

[ApiController]
[Route("api/organizations")]
public class HolidayController : OrganizationBaseController
{
    private readonly IHolidayService _service;

    public HolidayController(IHolidayService service)
    {
        _service = service;
    }

    [HttpGet("{slug}/holidays")]
    [Authorize]
    [ProducesResponseType(typeof(List<HolidayResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHolidays(string slug)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetHolidaysAsync(slug, userId.Value));
    }

    [HttpPost("{slug}/holidays")]
    [Authorize]
    [ProducesResponseType(typeof(HolidayResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateHoliday(string slug, [FromBody] CreateHolidayRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.CreateHolidayAsync(slug, userId.Value, request));
    }

    [HttpPut("{slug}/holidays/{id}")]
    [Authorize]
    [ProducesResponseType(typeof(HolidayResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateHoliday(string slug, int id, [FromBody] UpdateHolidayRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.UpdateHolidayAsync(slug, userId.Value, id, request));
    }

    [HttpDelete("{slug}/holidays/{id}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteHoliday(string slug, int id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.DeleteHolidayAsync(slug, userId.Value, id));
    }

    [HttpPost("{slug}/holidays/import-preset")]
    [Authorize]
    [ProducesResponseType(typeof(List<HolidayResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ImportPresetHolidays(
        string slug, [FromQuery] string preset = "de", [FromQuery] int? year = null)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.ImportPresetHolidaysAsync(slug, userId.Value, preset, year));
    }

    [HttpGet("/api/organizations/holiday-presets")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetAvailablePresets()
    {
        return Ok(_service.GetAvailablePresets());
    }
}
