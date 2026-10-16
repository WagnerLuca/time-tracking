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
/// Manages organizations, memberships, settings, and time overviews.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
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
        => ToResponse(await _service.GetOrganizationsAsync(limit, offset, GetCurrentUserId()));

    /// <summary>Get organization details by slug. Members see full details including member list; non-members see a limited view.</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(OrganizationDetailResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrganization(string slug)
    {
        var userId = GetCurrentUserId();
        return ToResponse(await _service.GetOrganizationAsync(slug, userId));
    }

    /// <summary>List organizations the current user belongs to.</summary>
    [HttpGet("mine")]
    [ProducesResponseType(typeof(List<UserOrganizationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyOrganizations()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetUserOrganizationsAsync(userId.Value));
    }

    /// <summary>Create a new organization.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrganizationResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateOrganization([FromBody] CreateOrganizationRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToCreatedResponse(await _service.CreateOrganizationAsync(userId.Value, request));
    }

    /// <summary>Update an existing organization (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpPut("{slug}")]
    [RequireOrgRole(OrganizationRole.Admin)]
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
    [RequireOrgRole(OrganizationRole.Owner)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteOrganization(string slug)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.DeleteOrganizationAsync(slug, userId.Value));
    }

    /// <summary>Update a member's role in the organization (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="memberId">Member ID to update.</param>
    [HttpPut("{slug}/members/{memberId}")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(OrganizationMemberResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMemberRole(string slug, int memberId, [FromBody] UpdateMemberRoleRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.UpdateMemberRoleAsync(slug, userId.Value, memberId, request));
    }

    /// <summary>Remove a member from the organization (self-leave or admin).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="memberId">Member ID to remove.</param>
    [HttpDelete("{slug}/members/{memberId}")]
    [RequireOrgRole]
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
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(OrganizationSettingsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSettings(string slug, [FromBody] UpdateOrganizationSettingsRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.UpdateSettingsAsync(slug, userId.Value, request));
    }

    /// <summary>
    /// The dashboard's team overview: one card per member the caller leads (org admins: everyone, group admins:
    /// their groups' members) with balance, presence, failure entries and today's core hours violation —
    /// each only where the member visibility matrix shows it. 403 for callers who lead nobody.
    /// </summary>
    [HttpGet("{slug}/team-overview")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(TeamOverviewResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTeamOverview(string slug)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetTeamOverviewAsync(slug, userId.Value));
    }

    /// <summary>
    /// The Organigramm: the organization's groups (with parent ids and whether the caller may manage them) and every
    /// person the caller may see, with direct memberships (leads marked), presence and the visible kinds of information —
    /// per the member visibility matrix, hidden people are left out. Open to every member.
    /// </summary>
    [HttpGet("{slug}/org-chart")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(OrgChartResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrgChart(string slug)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetOrgChartAsync(slug, userId.Value));
    }

    /// <summary>Get time tracking overview for visible members (admin or group admin).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="from">Start of date range filter.</param>
    /// <param name="to">End of date range filter.</param>
    [HttpGet("{slug}/time-overview")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(List<MemberTimeOverviewResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTimeOverview(
        string slug, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetTimeOverviewAsync(slug, userId.Value, from, to));
    }

    /// <summary>Get time entries for a specific member (admin or group admin).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="memberId">Member ID whose entries to retrieve.</param>
    /// <param name="from">Start of date range filter.</param>
    /// <param name="to">End of date range filter.</param>
    /// <param name="limit">Max items per page (default 50, max 200).</param>
    /// <param name="offset">Number of items to skip.</param>
    [HttpGet("{slug}/member-entries/{memberId}")]
    [RequireOrgRole]
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
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(MemberInitialOvertimeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetMemberInitialOvertime(
        string slug, int memberId, [FromBody] SetInitialOvertimeRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.SetMemberInitialOvertimeAsync(slug, userId.Value, memberId, request));
    }

    /// <summary>Set a member's vacation days allowance per year (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="memberId">Member ID to update.</param>
    [HttpPut("{slug}/members/{memberId}/vacation-days")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(MemberVacationDaysResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetMemberVacationDays(
        string slug, int memberId, [FromBody] SetVacationDaysRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.SetMemberVacationDaysAsync(slug, userId.Value, memberId, request));
    }

    /// <summary>Set or clear a member's personnel number for the payroll export (admin only; unique per organization).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="memberId">Member ID to update.</param>
    [HttpPut("{slug}/members/{memberId}/personnel-number")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(MemberPersonnelNumberResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetMemberPersonnelNumber(
        string slug, int memberId, [FromBody] SetPersonnelNumberRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.SetMemberPersonnelNumberAsync(slug, userId.Value, memberId, request));
    }

    /// <summary>
    /// Grant or revoke the accounting role (Buchhaltung): the Owner for anyone, admins for members only
    /// (not for themselves or other admins).
    /// </summary>
    [HttpPut("{slug}/members/{memberId}/accountant")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(MemberAccountantResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetMemberAccountant(string slug, int memberId, [FromBody] SetAccountantRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.SetMemberAccountantAsync(slug, userId.Value, memberId, request));
    }
}
