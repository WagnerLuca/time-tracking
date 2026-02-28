using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrganizationsController : OrganizationBaseController
{
    private readonly IOrganizationService _service;

    public OrganizationsController(IOrganizationService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<OrganizationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrganizations()
        => ToResponse(await _service.GetOrganizationsAsync());

    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(OrganizationDetailResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrganization(string slug)
        => ToResponse(await _service.GetOrganizationAsync(slug));

    [HttpGet("user/{userId}")]
    [ProducesResponseType(typeof(List<UserOrganizationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserOrganizations(int userId)
        => ToResponse(await _service.GetUserOrganizationsAsync(userId));

    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(OrganizationResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateOrganization([FromBody] CreateOrganizationRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToCreatedResponse(await _service.CreateOrganizationAsync(userId.Value, request));
    }

    [HttpPut("{slug}")]
    [Authorize]
    [ProducesResponseType(typeof(OrganizationResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateOrganization(string slug, [FromBody] UpdateOrganizationRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.UpdateOrganizationAsync(slug, userId.Value, request));
    }

    [HttpDelete("{slug}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteOrganization(string slug)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.DeleteOrganizationAsync(slug, userId.Value));
    }

    [HttpPost("{slug}/members")]
    [Authorize]
    [ProducesResponseType(typeof(OrganizationMemberResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddMember(string slug, [FromBody] AddMemberRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToCreatedResponse(await _service.AddMemberAsync(slug, userId.Value, request));
    }

    [HttpPut("{slug}/members/{memberId}")]
    [Authorize]
    [ProducesResponseType(typeof(OrganizationMemberResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMemberRole(string slug, int memberId, [FromBody] UpdateMemberRoleRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.UpdateMemberRoleAsync(slug, userId.Value, memberId, request));
    }

    [HttpDelete("{slug}/members/{memberId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveMember(string slug, int memberId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.RemoveMemberAsync(slug, userId.Value, memberId));
    }

    [HttpPut("{slug}/settings")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSettings(string slug, [FromBody] UpdateOrganizationSettingsRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.UpdateSettingsAsync(slug, userId.Value, request));
    }

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

    [HttpGet("{slug}/member-entries/{memberId}")]
    [Authorize]
    [ProducesResponseType(typeof(List<TimeEntryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMemberEntries(
        string slug, int memberId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetMemberEntriesAsync(slug, userId.Value, memberId, from, to));
    }

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
