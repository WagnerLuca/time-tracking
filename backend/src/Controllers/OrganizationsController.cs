using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

/// <summary>
/// Manages organizations, memberships, settings, and time overviews.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[EnableRateLimiting("General")]
public class OrganizationsController : OrganizationBaseController
{
    private readonly IOrganizationService _service;
    private readonly ILogger<OrganizationsController> _logger;

    public OrganizationsController(IOrganizationService service, ILogger<OrganizationsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>List all organizations.</summary>
    /// <param name="limit">Max items per page (default 50, max 200).</param>
    /// <param name="offset">Number of items to skip.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<OrganizationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrganizations(
        [FromQuery] int limit = 50, [FromQuery] int offset = 0)
        => ToResponse(await _service.GetOrganizationsAsync(limit, offset));

    /// <summary>Get organization details by slug.</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(OrganizationDetailResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrganization(string slug)
        => ToResponse(await _service.GetOrganizationAsync(slug));

    /// <summary>List organizations a specific user belongs to.</summary>
    /// <param name="userId">Target user ID.</param>
    [HttpGet("user/{userId}")]
    [ProducesResponseType(typeof(List<UserOrganizationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserOrganizations(int userId)
        => ToResponse(await _service.GetUserOrganizationsAsync(userId));

    /// <summary>Create a new organization.</summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(OrganizationResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateOrganization([FromBody] CreateOrganizationRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToCreatedResponse(await _service.CreateOrganizationAsync(userId.Value, request));
    }

    /// <summary>Update an existing organization.</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpPut("{slug}")]
    [Authorize]
    [ProducesResponseType(typeof(OrganizationResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateOrganization(string slug, [FromBody] UpdateOrganizationRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.UpdateOrganizationAsync(slug, userId.Value, request));
    }

    /// <summary>Delete an organization (owner only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpDelete("{slug}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteOrganization(string slug)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.DeleteOrganizationAsync(slug, userId.Value));
    }

    /// <summary>Add a member to the organization.</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpPost("{slug}/members")]
    [Authorize]
    [ProducesResponseType(typeof(OrganizationMemberResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddMember(string slug, [FromBody] AddMemberRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToCreatedResponse(await _service.AddMemberAsync(slug, userId.Value, request));
    }

    /// <summary>Update a member's role in the organization.</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="memberId">Member ID to update.</param>
    [HttpPut("{slug}/members/{memberId}")]
    [Authorize]
    [ProducesResponseType(typeof(OrganizationMemberResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMemberRole(string slug, int memberId, [FromBody] UpdateMemberRoleRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.UpdateMemberRoleAsync(slug, userId.Value, memberId, request));
    }

    /// <summary>Remove a member from the organization.</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="memberId">Member ID to remove.</param>
    [HttpDelete("{slug}/members/{memberId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveMember(string slug, int memberId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.RemoveMemberAsync(slug, userId.Value, memberId));
    }

    /// <summary>Update organization settings (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpPut("{slug}/settings")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSettings(string slug, [FromBody] UpdateOrganizationSettingsRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.UpdateSettingsAsync(slug, userId.Value, request));
    }

    /// <summary>Get time tracking overview for all members (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="from">Start of date range filter.</param>
    /// <param name="to">End of date range filter.</param>
    [HttpGet("{slug}/time-overview")]
    [Authorize]
    [ProducesResponseType(typeof(List<MemberTimeOverviewResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTimeOverview(
        string slug, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetTimeOverviewAsync(slug, userId.Value, from, to));
    }

    /// <summary>Get time entries for a specific member (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="memberId">Member ID whose entries to retrieve.</param>
    /// <param name="from">Start of date range filter.</param>
    /// <param name="to">End of date range filter.</param>
    /// <param name="limit">Max items per page (default 50, max 200).</param>
    /// <param name="offset">Number of items to skip.</param>
    [HttpGet("{slug}/member-entries/{memberId}")]
    [Authorize]
    [ProducesResponseType(typeof(PaginatedResponse<TimeEntryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMemberEntries(
        string slug, int memberId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int limit = 50, [FromQuery] int offset = 0)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetMemberEntriesAsync(slug, userId.Value, memberId, from, to, limit, offset));
    }

    /// <summary>Set a member's initial overtime hours (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="memberId">Member ID to update.</param>
    [HttpPut("{slug}/members/{memberId}/initial-overtime")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetMemberInitialOvertime(
        string slug, int memberId, [FromBody] SetInitialOvertimeRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.SetMemberInitialOvertimeAsync(slug, userId.Value, memberId, request));
    }
}
