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
/// Manages groups within an organization.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/organizations")]
[Authorize]
[EnableRateLimiting("General")]
public class GroupController : OrganizationBaseController
{
    private readonly IGroupService _service;

    public GroupController(IGroupService service)
    {
        _service = service;
    }

    /// <summary>List all groups in an organization.</summary>
    [HttpGet("{slug}/groups")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(List<GroupResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGroups(string slug)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetGroupsAsync(slug, userId.Value));
    }

    /// <summary>Get group details including members.</summary>
    [HttpGet("{slug}/groups/{groupId}")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(GroupDetailResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGroup(string slug, int groupId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetGroupAsync(slug, groupId, userId.Value));
    }

    /// <summary>Create a new group (admin only).</summary>
    [HttpPost("{slug}/groups")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(GroupDetailResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateGroup(string slug, [FromBody] CreateGroupRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.CreateGroupAsync(slug, userId.Value, request));
    }

    /// <summary>Update a group (admin only).</summary>
    [HttpPut("{slug}/groups/{groupId}")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(GroupDetailResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateGroup(string slug, int groupId, [FromBody] UpdateGroupRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.UpdateGroupAsync(slug, groupId, userId.Value, request));
    }

    /// <summary>Delete a group (admin only).</summary>
    [HttpDelete("{slug}/groups/{groupId}")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteGroup(string slug, int groupId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.DeleteGroupAsync(slug, groupId, userId.Value));
    }

    /// <summary>Add a member to a group.</summary>
    [HttpPost("{slug}/groups/{groupId}/members")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(GroupMemberResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddMember(string slug, int groupId, [FromBody] AddGroupMemberRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.AddMemberAsync(slug, groupId, userId.Value, request));
    }

    /// <summary>Update a group member's role (org admin only).</summary>
    [HttpPatch("{slug}/groups/{groupId}/members/{targetUserId}")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(GroupMemberResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMemberRole(string slug, int groupId, int targetUserId, [FromBody] UpdateGroupMemberRoleRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.UpdateMemberRoleAsync(slug, groupId, targetUserId, userId.Value, request));
    }

    /// <summary>Remove a member from a group.</summary>
    [HttpDelete("{slug}/groups/{groupId}/members/{targetUserId}")]
    [RequireOrgRole]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveMember(string slug, int groupId, int targetUserId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.RemoveMemberAsync(slug, groupId, targetUserId, userId.Value));
    }

    /// <summary>Permission overrides of all groups of the organization (one request instead of one per group).</summary>
    [HttpGet("{slug}/groups/permissions")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(List<GroupPermissionsResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllPermissions(string slug)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetAllPermissionsAsync(slug, userId.Value));
    }

    /// <summary>Get group permission overrides.</summary>
    [HttpGet("{slug}/groups/{groupId}/permissions")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(GroupPermissionsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPermissions(string slug, int groupId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetPermissionsAsync(slug, groupId, userId.Value));
    }

    /// <summary>Update group permission overrides (admin only).</summary>
    [HttpPut("{slug}/groups/{groupId}/permissions")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(GroupPermissionsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePermissions(string slug, int groupId, [FromBody] UpdateGroupPermissionsRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.UpdatePermissionsAsync(slug, groupId, userId.Value, request));
    }

    /// <summary>Get effective permissions for the current user (resolves org + group overrides).</summary>
    [HttpGet("{slug}/effective-permissions")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(EffectivePermissionsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEffectivePermissions(string slug)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetEffectivePermissionsAsync(slug, userId.Value));
    }

    /// <summary>Get effective permissions for a specific member (admin, or the member themselves).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="memberId">Target member user ID.</param>
    [HttpGet("{slug}/members/{memberId}/effective-permissions")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(EffectivePermissionsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMemberEffectivePermissions(string slug, int memberId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetEffectivePermissionsAsync(slug, userId.Value, memberId));
    }
}
