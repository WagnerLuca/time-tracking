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
/// Manages organization holidays and holiday preset imports.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/organizations")]
[Authorize]
[EnableRateLimiting("General")]
public class HolidayController : OrganizationBaseController
{
    private readonly IHolidayService _service;
    private readonly ILogger<HolidayController> _logger;

    public HolidayController(IHolidayService service, ILogger<HolidayController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>List all holidays for an organization (members only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpGet("{slug}/holidays")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(List<HolidayResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHolidays(string slug)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetHolidaysAsync(slug, userId.Value));
    }

    /// <summary>Create a new holiday for the organization (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpPost("{slug}/holidays")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(HolidayResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateHoliday(string slug, [FromBody] CreateHolidayRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.CreateHolidayAsync(slug, userId.Value, request));
    }

    /// <summary>Update an existing holiday (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="id">Holiday ID.</param>
    [HttpPut("{slug}/holidays/{id}")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(HolidayResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateHoliday(string slug, int id, [FromBody] UpdateHolidayRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.UpdateHolidayAsync(slug, userId.Value, id, request));
    }

    /// <summary>Delete a holiday (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="id">Holiday ID.</param>
    [HttpDelete("{slug}/holidays/{id}")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteHoliday(string slug, int id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.DeleteHolidayAsync(slug, userId.Value, id));
    }

    /// <summary>Import preset holidays (e.g. German public holidays) for a given year (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="preset">Preset code, e.g. "de" (default).</param>
    /// <param name="year">Year to import for. Defaults to current year.</param>
    [HttpPost("{slug}/holidays/import-preset")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(List<HolidayResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ImportPresetHolidays(
        string slug, [FromQuery] string preset = "de", [FromQuery] int? year = null)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.ImportPresetHolidaysAsync(slug, userId.Value, preset, year));
    }

    /// <summary>List available holiday preset codes.</summary>
    [HttpGet("holiday-presets")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetAvailablePresets()
    {
        return Ok(_service.GetAvailablePresets());
    }
}
