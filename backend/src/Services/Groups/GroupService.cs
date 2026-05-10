using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public class GroupService : IGroupService
{
    private readonly TimeTrackingDbContext _context;
    private readonly ILogger<GroupService> _logger;

    public GroupService(TimeTrackingDbContext context, ILogger<GroupService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task<(Organization? org, UserOrganization? membership)> ResolveOrgMemberAsync(string slug, int userId)
    {
        var org = await _context.Organizations.FirstOrDefaultAsync(o => o.Slug == slug);
        if (org is null) return (null, null);
        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId);
        return (org, membership);
    }

    private static GroupMemberResponse ToMemberResponse(GroupMember gm) => new(
        gm.UserId,
        gm.User.FirstName,
        gm.User.LastName,
        gm.User.Email,
        gm.User.ProfileImageUrl,
        gm.Role.ToString(),
        gm.JoinedAt
    );

    private static GroupDetailResponse ToDetailResponse(Group g, IList<GroupMember> members) => new(
        g.Id,
        g.OrganizationId,
        g.Name,
        g.Description,
        g.Color,
        g.CreatedAt,
        members.Select(ToMemberResponse).ToList()
    );

    // ── Queries ─────────────────────────────────────────────────────────────

    public async Task<ServiceResult<List<GroupResponse>>> GetGroupsAsync(string slug, int callerUserId)
    {
        var (org, membership) = await ResolveOrgMemberAsync(slug, callerUserId);
        if (org is null) return ServiceResult.NotFound<List<GroupResponse>>("Organization not found.");
        if (membership is null) return ServiceResult.Forbidden<List<GroupResponse>>("You are not a member of this organization.");

        var isOrgAdmin = membership.Role >= OrganizationRole.Admin;

        var groups = await _context.Groups
            .Where(g => g.OrganizationId == org.Id)
            .Include(g => g.Members)
            .OrderBy(g => g.Name)
            .ToListAsync();

        var result = groups.Select(g =>
        {
            var myMembership = g.Members.FirstOrDefault(m => m.UserId == callerUserId);
            return new GroupResponse(
                g.Id,
                g.OrganizationId,
                g.Name,
                g.Description,
                g.Color,
                g.CreatedAt,
                g.Members.Count,
                IsGroupAdmin: myMembership?.Role == GroupRole.Admin
            );
        }).ToList();

        return ServiceResult.Ok(result);
    }

    public async Task<ServiceResult<GroupDetailResponse>> GetGroupAsync(string slug, int groupId, int callerUserId)
    {
        var (org, membership) = await ResolveOrgMemberAsync(slug, callerUserId);
        if (org is null) return ServiceResult.NotFound<GroupDetailResponse>("Organization not found.");
        if (membership is null) return ServiceResult.Forbidden<GroupDetailResponse>("You are not a member of this organization.");

        var group = await _context.Groups
            .Include(g => g.Members)
            .ThenInclude(gm => gm.User)
            .FirstOrDefaultAsync(g => g.Id == groupId && g.OrganizationId == org.Id);

        if (group is null) return ServiceResult.NotFound<GroupDetailResponse>("Group not found.");

        return ServiceResult.Ok(ToDetailResponse(group, group.Members.ToList()));
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    public async Task<ServiceResult<GroupDetailResponse>> CreateGroupAsync(string slug, int callerUserId, CreateGroupRequest request)
    {
        var (org, membership) = await ResolveOrgMemberAsync(slug, callerUserId);
        if (org is null) return ServiceResult.NotFound<GroupDetailResponse>("Organization not found.");
        if (membership is null || membership.Role < OrganizationRole.Admin)
            return ServiceResult.Forbidden<GroupDetailResponse>("Only admins can create groups.");

        var group = new Group
        {
            OrganizationId = org.Id,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Color = request.Color?.Trim(),
        };

        _context.Groups.Add(group);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Group {GroupId} '{Name}' created in org {Slug} by user {UserId}",
            group.Id, group.Name, slug, callerUserId);

        return ServiceResult.Ok(ToDetailResponse(group, new List<GroupMember>()));
    }

    public async Task<ServiceResult<GroupDetailResponse>> UpdateGroupAsync(string slug, int groupId, int callerUserId, UpdateGroupRequest request)
    {
        var (org, membership) = await ResolveOrgMemberAsync(slug, callerUserId);
        if (org is null) return ServiceResult.NotFound<GroupDetailResponse>("Organization not found.");
        if (membership is null || membership.Role < OrganizationRole.Admin)
            return ServiceResult.Forbidden<GroupDetailResponse>("Only admins can update groups.");

        var group = await _context.Groups
            .Include(g => g.Members)
            .ThenInclude(gm => gm.User)
            .FirstOrDefaultAsync(g => g.Id == groupId && g.OrganizationId == org.Id);

        if (group is null) return ServiceResult.NotFound<GroupDetailResponse>("Group not found.");

        group.Name = request.Name.Trim();
        group.Description = request.Description?.Trim();
        group.Color = request.Color?.Trim();
        group.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return ServiceResult.Ok(ToDetailResponse(group, group.Members.ToList()));
    }

    public async Task<ServiceResult> DeleteGroupAsync(string slug, int groupId, int callerUserId)
    {
        var (org, membership) = await ResolveOrgMemberAsync(slug, callerUserId);
        if (org is null) return ServiceResult.NotFound("Organization not found.");
        if (membership is null || membership.Role < OrganizationRole.Admin)
            return ServiceResult.Forbidden("Only admins can delete groups.");

        var group = await _context.Groups
            .FirstOrDefaultAsync(g => g.Id == groupId && g.OrganizationId == org.Id);

        if (group is null) return ServiceResult.NotFound("Group not found.");

        group.IsActive = false;
        group.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Group {GroupId} deleted in org {Slug} by user {UserId}", groupId, slug, callerUserId);

        return ServiceResult.Ok();
    }

    // ── Member management ────────────────────────────────────────────────────

    public async Task<ServiceResult<GroupMemberResponse>> AddMemberAsync(string slug, int groupId, int callerUserId, AddGroupMemberRequest request)
    {
        var (org, callerMembership) = await ResolveOrgMemberAsync(slug, callerUserId);
        if (org is null) return ServiceResult.NotFound<GroupMemberResponse>("Organization not found.");

        var group = await _context.Groups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == groupId && g.OrganizationId == org.Id);

        if (group is null) return ServiceResult.NotFound<GroupMemberResponse>("Group not found.");

        // Only org admins or group admins can add members
        var isOrgAdmin = callerMembership?.Role >= OrganizationRole.Admin;
        var isGroupAdmin = group.Members.Any(m => m.UserId == callerUserId && m.Role == GroupRole.Admin);
        if (!isOrgAdmin && !isGroupAdmin)
            return ServiceResult.Forbidden<GroupMemberResponse>("Only org admins or group admins can add members.");

        // Target user must be an org member
        var targetMembership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == request.UserId);
        if (targetMembership is null)
            return ServiceResult.BadRequest<GroupMemberResponse>("User is not a member of this organization.");

        if (group.Members.Any(m => m.UserId == request.UserId))
            return ServiceResult.BadRequest<GroupMemberResponse>("User is already in this group.");

        if (!Enum.TryParse<GroupRole>(request.Role, ignoreCase: true, out var role))
            role = GroupRole.Member;

        var groupMember = new GroupMember
        {
            GroupId = groupId,
            UserId = request.UserId,
            Role = role,
        };

        _context.GroupMembers.Add(groupMember);
        await _context.SaveChangesAsync();

        // Reload with user for response
        await _context.Entry(groupMember).Reference(gm => gm.User).LoadAsync();

        return ServiceResult.Ok(ToMemberResponse(groupMember));
    }

    public async Task<ServiceResult<GroupMemberResponse>> UpdateMemberRoleAsync(string slug, int groupId, int targetUserId, int callerUserId, UpdateGroupMemberRoleRequest request)
    {
        var (org, callerMembership) = await ResolveOrgMemberAsync(slug, callerUserId);
        if (org is null) return ServiceResult.NotFound<GroupMemberResponse>("Organization not found.");

        if (callerMembership?.Role < OrganizationRole.Admin)
            return ServiceResult.Forbidden<GroupMemberResponse>("Only org admins can change group member roles.");

        var groupMember = await _context.GroupMembers
            .Include(gm => gm.User)
            .Include(gm => gm.Group)
            .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == targetUserId && gm.Group.OrganizationId == org.Id);

        if (groupMember is null) return ServiceResult.NotFound<GroupMemberResponse>("Member not found in group.");

        if (!Enum.TryParse<GroupRole>(request.Role, ignoreCase: true, out var role))
            return ServiceResult.BadRequest<GroupMemberResponse>("Invalid role. Use 'Admin' or 'Member'.");

        groupMember.Role = role;
        await _context.SaveChangesAsync();

        return ServiceResult.Ok(ToMemberResponse(groupMember));
    }

    public async Task<ServiceResult> RemoveMemberAsync(string slug, int groupId, int targetUserId, int callerUserId)
    {
        var (org, callerMembership) = await ResolveOrgMemberAsync(slug, callerUserId);
        if (org is null) return ServiceResult.NotFound("Organization not found.");

        var group = await _context.Groups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == groupId && g.OrganizationId == org.Id);

        if (group is null) return ServiceResult.NotFound("Group not found.");

        var isOrgAdmin = callerMembership?.Role >= OrganizationRole.Admin;
        var isGroupAdmin = group.Members.Any(m => m.UserId == callerUserId && m.Role == GroupRole.Admin);
        var isSelf = callerUserId == targetUserId;

        if (!isOrgAdmin && !isGroupAdmin && !isSelf)
            return ServiceResult.Forbidden("Only org admins, group admins, or the member themselves can remove a member.");

        var groupMember = group.Members.FirstOrDefault(m => m.UserId == targetUserId);
        if (groupMember is null) return ServiceResult.NotFound("Member not found in group.");

        _context.GroupMembers.Remove(groupMember);
        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }
}
