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
/// Manages absence days (sick days, vacation, etc.) for organization members.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/organizations")]
[Authorize]
[EnableRateLimiting("General")]
public class AbsenceDayController : OrganizationBaseController
{
    private readonly IAbsenceDayService _service;
    private readonly ILogger<AbsenceDayController> _logger;

    public AbsenceDayController(IAbsenceDayService service, ILogger<AbsenceDayController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>List absences for the organization with optional filters (members only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="userId">Filter by user ID.</param>
    /// <param name="from">Start of date range.</param>
    /// <param name="to">End of date range.</param>
    /// <param name="limit">Max items per page (default 50, max 200).</param>
    /// <param name="offset">Number of items to skip.</param>
    /// <param name="groupId">Only the members of this group and of its sub-groups (whose groups the caller may see).</param>
    [HttpGet("{slug}/absences")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(PaginatedResponse<AbsenceDayResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAbsences(
        string slug, [FromQuery] int? userId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] int limit = 50, [FromQuery] int offset = 0, [FromQuery] int? groupId = null)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();
        return ToResponse(await _service.GetAbsencesAsync(slug, callerId.Value, userId, from, to, limit, offset, groupId));
    }

    /// <summary>Create an absence day for the current user (members only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpPost("{slug}/absences")]
    [RequireOrgRole]
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
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(AbsenceDayResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AdminCreateAbsence(string slug, [FromBody] AdminCreateAbsenceDayRequest request)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();
        return ToResponse(await _service.AdminCreateAbsenceAsync(slug, callerId.Value, request));
    }

    /// <summary>Create own absences for every weekday of a range, all or nothing (members only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpPost("{slug}/absences/range")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(List<AbsenceDayResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAbsenceRange(string slug, [FromBody] CreateAbsenceRangeRequest request)
    {
        if (GetCurrentUserId() is not int userId) return Unauthorized();
        return ToResponse(await _service.CreateAbsenceRangeAsync(slug, userId, request));
    }

    /// <summary>Create absences for a member for every weekday of a range, all or nothing (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpPost("{slug}/absences/admin/range")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(List<AbsenceDayResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AdminCreateAbsenceRange(string slug, [FromBody] CreateAbsenceRangeRequest request)
    {
        if (GetCurrentUserId() is not int callerId) return Unauthorized();
        return ToResponse(await _service.AdminCreateAbsenceRangeAsync(slug, callerId, request));
    }

    /// <summary>Delete an absence day (members only; service enforces ownership rules).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="id">Absence day ID.</param>
    [HttpDelete("{slug}/absences/{id}")]
    [RequireOrgRole]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAbsence(string slug, int id)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();
        return ToResponse(await _service.DeleteAbsenceAsync(slug, callerId.Value, id));
    }

    /// <summary>Delete several absence days (a span) together, all or nothing — same rules as a single delete.</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpPost("{slug}/absences/delete")]
    [RequireOrgRole]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAbsences(string slug, [FromBody] DeleteAbsencesRequest request)
    {
        if (GetCurrentUserId() is not int callerId) return Unauthorized();
        return ToResponse(await _service.DeleteAbsencesAsync(slug, callerId, request.Ids));
    }

    // ────────────────────────────────────────────────────
    //  Custom absence types
    // ────────────────────────────────────────────────────

    /// <summary>List the organization's custom absence types (members only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpGet("{slug}/absence-types")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(List<AbsenceTypeDefinitionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAbsenceTypes(string slug)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();
        return ToResponse(await _service.GetAbsenceTypesAsync(slug, callerId.Value));
    }

    /// <summary>Create a custom absence type (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpPost("{slug}/absence-types")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(AbsenceTypeDefinitionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateAbsenceType(string slug, [FromBody] UpsertAbsenceTypeDefinitionRequest request)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();
        return ToResponse(await _service.CreateAbsenceTypeAsync(slug, callerId.Value, request));
    }

    /// <summary>Update a custom absence type (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="typeId">Absence type ID.</param>
    [HttpPut("{slug}/absence-types/{typeId}")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(AbsenceTypeDefinitionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAbsenceType(string slug, int typeId, [FromBody] UpsertAbsenceTypeDefinitionRequest request)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();
        return ToResponse(await _service.UpdateAbsenceTypeAsync(slug, callerId.Value, typeId, request));
    }

    /// <summary>Deactivate a custom absence type (admin only). Existing absences keep the type.</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="typeId">Absence type ID.</param>
    [HttpDelete("{slug}/absence-types/{typeId}")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAbsenceType(string slug, int typeId)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();
        return ToResponse(await _service.DeleteAbsenceTypeAsync(slug, callerId.Value, typeId));
    }
}
