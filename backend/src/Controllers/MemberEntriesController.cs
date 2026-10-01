using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimeTracking.Api.Filters;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

/// <summary>
/// Admin corrections of members' time entries and the Working Time Act compliance list.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/organizations/{slug}/members/{memberId}")]
[Authorize]
[EnableRateLimiting("General")]
[RequireOrgRole]
public class MemberEntriesController(MemberEntryService service) : OrganizationBaseController
{
    /// <summary>Create a finished entry for a member (org admin, or group admin with time-edit approval).</summary>
    [HttpPost("entries")]
    [ProducesResponseType(typeof(TimeEntryResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateEntry(string slug, int memberId, [FromBody] CreateMemberEntryRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToCreatedResponse(await service.CreateAsync(slug, userId.Value, memberId, request));
    }

    /// <summary>Correct a member's entry regardless of the member's own edit rule; setting the end time resolves a failure entry.</summary>
    [HttpPut("entries/{entryId}")]
    [ProducesResponseType(typeof(TimeEntryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateEntry(string slug, int memberId, int entryId, [FromBody] UpdateMemberEntryRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await service.UpdateAsync(slug, userId.Value, memberId, entryId, request));
    }

    /// <summary>Delete a member's entry.</summary>
    [HttpDelete("entries/{entryId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteEntry(string slug, int memberId, int entryId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await service.DeleteAsync(slug, userId.Value, memberId, entryId));
    }

    /// <summary>
    /// Days breaking the Working Time Act (&gt; 10 h net, &lt; 11 h rest) in [from, to] (default: current month).
    /// Same access as the member's reports (self, admins while member hours are visible, group admins with export rights).
    /// </summary>
    [HttpGet("compliance")]
    [ProducesResponseType(typeof(List<ComplianceDayResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCompliance(string slug, int memberId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await service.GetComplianceAsync(slug, userId.Value, memberId, from, to));
    }
}
