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

    private static bool CanGroupAdminManage(Group group, int userId) =>
        group.GroupAdminCanManageMembers == true
        && group.Members.Any(m => m.UserId == userId && m.Role == GroupRole.Admin);

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
                IsGroupAdmin: myMembership?.Role == GroupRole.Admin,
                GroupAdminCanManageMembers: g.GroupAdminCanManageMembers == true
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

        // Org admins, or group admins when the group allows them to manage members
        var isOrgAdmin = callerMembership?.Role >= OrganizationRole.Admin;
        if (!isOrgAdmin && !CanGroupAdminManage(group, callerUserId))
            return ServiceResult.Forbidden<GroupMemberResponse>("Only org admins, or group admins with member management rights, can add members.");

        // Target user must be an org member
        var targetMembership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == request.UserId);
        if (targetMembership is null)
            return ServiceResult.BadRequest<GroupMemberResponse>("User is not a member of this organization.");

        if (group.Members.Any(m => m.UserId == request.UserId))
            return ServiceResult.BadRequest<GroupMemberResponse>("User is already in this group.");

        if (!isOrgAdmin && targetMembership.Role >= OrganizationRole.Admin)
            return ServiceResult.Forbidden<GroupMemberResponse>("Only org admins can add admins or the owner to a group.");

        if (!Enum.TryParse<GroupRole>(request.Role, ignoreCase: true, out var role) || !Enum.IsDefined(role))
            role = GroupRole.Member;

        // Granting the group admin role stays with org admins, like UpdateMemberRoleAsync
        if (role == GroupRole.Admin && !isOrgAdmin)
            return ServiceResult.Forbidden<GroupMemberResponse>("Only org admins can add group admins.");

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

        // Pattern, not `?.Role < Admin`: that is false for a null membership and would let non-members through
        if (callerMembership is not { Role: >= OrganizationRole.Admin })
            return ServiceResult.Forbidden<GroupMemberResponse>("Only org admins can change group member roles.");

        var groupMember = await _context.GroupMembers
            .Include(gm => gm.User)
            .Include(gm => gm.Group)
            .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == targetUserId && gm.Group.OrganizationId == org.Id);

        if (groupMember is null) return ServiceResult.NotFound<GroupMemberResponse>("Member not found in group.");

        if (!Enum.TryParse<GroupRole>(request.Role, ignoreCase: true, out var role) || !Enum.IsDefined(role))
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
        var isSelf = callerUserId == targetUserId;
        // Group admins may remove regular members only, and only with member management rights
        var targetIsGroupAdmin = group.Members.Any(m => m.UserId == targetUserId && m.Role == GroupRole.Admin);
        var groupAdminMayRemove = CanGroupAdminManage(group, callerUserId) && !targetIsGroupAdmin;

        if (!isOrgAdmin && !groupAdminMayRemove && !isSelf)
            return ServiceResult.Forbidden("Only org admins, group admins with member management rights, or the member themselves can remove a member.");

        var groupMember = group.Members.FirstOrDefault(m => m.UserId == targetUserId);
        if (groupMember is null) return ServiceResult.NotFound("Member not found in group.");

        _context.GroupMembers.Remove(groupMember);
        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    // ── Permissions ──────────────────────────────────────────────────────────

    private static GroupPermissionsResponse ToPermissionsResponse(Group group) => new(
        group.Id,
        group.Name,
        group.EditPastEntriesMode?.ToString(),
        group.EditPauseMode?.ToString(),
        group.CsvImportMode?.ToString(),
        group.WorkScheduleChangeMode?.ToString(),
        group.InitialOvertimeMode?.ToString(),
        group.AbsenceSelfServiceMode?.ToString(),
        group.VacationVisibility?.ToString(),
        group.SickDayVisibility?.ToString(),
        group.TimeEntryVisibility?.ToString(),
        group.GroupAdminCanApproveAbsences,
        group.GroupAdminCanApproveTimeEdits,
        group.GroupAdminCanApproveScheduleChanges,
        group.GroupAdminCanManageMembers,
        group.GroupAdminCanViewAllTimeEntries,
        group.GroupAdminCanExportData
    );

    public async Task<ServiceResult<GroupPermissionsResponse>> GetPermissionsAsync(string slug, int groupId, int callerUserId)
    {
        var (org, membership) = await ResolveOrgMemberAsync(slug, callerUserId);
        if (org is null) return ServiceResult.NotFound<GroupPermissionsResponse>("Organization not found.");
        if (membership is null) return ServiceResult.Forbidden<GroupPermissionsResponse>("You are not a member of this organization.");

        var group = await _context.Groups
            .FirstOrDefaultAsync(g => g.Id == groupId && g.OrganizationId == org.Id);
        if (group is null) return ServiceResult.NotFound<GroupPermissionsResponse>("Group not found.");

        return ServiceResult.Ok(ToPermissionsResponse(group));
    }

    public async Task<ServiceResult<GroupPermissionsResponse>> UpdatePermissionsAsync(string slug, int groupId, int callerUserId, UpdateGroupPermissionsRequest request)
    {
        var (org, membership) = await ResolveOrgMemberAsync(slug, callerUserId);
        if (org is null) return ServiceResult.NotFound<GroupPermissionsResponse>("Organization not found.");
        if (membership is null || membership.Role < OrganizationRole.Admin)
            return ServiceResult.Forbidden<GroupPermissionsResponse>("Only org admins can update group permissions.");

        var group = await _context.Groups
            .FirstOrDefaultAsync(g => g.Id == groupId && g.OrganizationId == org.Id);
        if (group is null) return ServiceResult.NotFound<GroupPermissionsResponse>("Group not found.");

        // Apply nullable partial updates.
        // A negative value (e.g. -1) clears the override back to "inherit from org".
        if (request.EditPastEntriesMode.HasValue) group.EditPastEntriesMode = NormalizeOverride(request.EditPastEntriesMode);
        if (request.EditPauseMode.HasValue) group.EditPauseMode = NormalizeOverride(request.EditPauseMode);
        if (request.CsvImportMode.HasValue) group.CsvImportMode = NormalizeOverride(request.CsvImportMode);
        if (request.WorkScheduleChangeMode.HasValue) group.WorkScheduleChangeMode = NormalizeOverride(request.WorkScheduleChangeMode);
        if (request.InitialOvertimeMode.HasValue) group.InitialOvertimeMode = NormalizeOverride(request.InitialOvertimeMode);
        if (request.AbsenceSelfServiceMode.HasValue) group.AbsenceSelfServiceMode = NormalizeOverride(request.AbsenceSelfServiceMode);
        if (request.VacationVisibility.HasValue) group.VacationVisibility = NormalizeOverride(request.VacationVisibility);
        if (request.SickDayVisibility.HasValue) group.SickDayVisibility = NormalizeOverride(request.SickDayVisibility);
        if (request.TimeEntryVisibility.HasValue) group.TimeEntryVisibility = NormalizeOverride(request.TimeEntryVisibility);
        if (request.GroupAdminCanApproveAbsences.HasValue) group.GroupAdminCanApproveAbsences = request.GroupAdminCanApproveAbsences;
        if (request.GroupAdminCanApproveTimeEdits.HasValue) group.GroupAdminCanApproveTimeEdits = request.GroupAdminCanApproveTimeEdits;
        if (request.GroupAdminCanApproveScheduleChanges.HasValue) group.GroupAdminCanApproveScheduleChanges = request.GroupAdminCanApproveScheduleChanges;
        if (request.GroupAdminCanManageMembers.HasValue) group.GroupAdminCanManageMembers = request.GroupAdminCanManageMembers;
        if (request.GroupAdminCanViewAllTimeEntries.HasValue) group.GroupAdminCanViewAllTimeEntries = request.GroupAdminCanViewAllTimeEntries;
        if (request.GroupAdminCanExportData.HasValue) group.GroupAdminCanExportData = request.GroupAdminCanExportData;

        group.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Group {GroupId} permissions updated in org {Slug} by user {UserId}", groupId, slug, callerUserId);

        return ServiceResult.Ok(ToPermissionsResponse(group));
    }

    public async Task<ServiceResult<EffectivePermissionsResponse>> GetEffectivePermissionsAsync(string slug, int callerUserId, int? targetUserId = null)
    {
        var (org, membership) = await ResolveOrgMemberAsync(slug, callerUserId);
        if (org is null) return ServiceResult.NotFound<EffectivePermissionsResponse>("Organization not found.");
        if (membership is null) return ServiceResult.Forbidden<EffectivePermissionsResponse>("You are not a member of this organization.");

        // Resolving another member's permissions requires org admin
        var userId = targetUserId ?? callerUserId;
        if (userId != callerUserId && membership.Role < OrganizationRole.Admin)
            return ServiceResult.Forbidden<EffectivePermissionsResponse>("Only org admins can view other members' permissions.");

        var targetMembership = userId == callerUserId
            ? membership
            : await _context.UserOrganizations.AsNoTracking()
                .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId);
        if (targetMembership is null)
            return ServiceResult.NotFound<EffectivePermissionsResponse>("Member not found in this organization.");

        var isOrgAdmin = targetMembership.Role >= OrganizationRole.Admin;

        // Get all groups the target user belongs to
        var userGroups = await _context.Groups
            .Where(g => g.OrganizationId == org.Id && g.Members.Any(m => m.UserId == userId))
            .ToListAsync();

        var isGroupAdmin = userGroups.Any(g =>
            _context.GroupMembers.Any(gm => gm.GroupId == g.Id && gm.UserId == userId && gm.Role == GroupRole.Admin));

        // Resolve permissions: most permissive group override wins
        var editPastEntries = ResolveRuleMode(org.EditPastEntriesMode, userGroups, g => g.EditPastEntriesMode);
        var editPause = ResolveRuleMode(org.EditPauseMode, userGroups, g => g.EditPauseMode);
        var csvImport = ResolveRuleMode(org.CsvImportMode, userGroups, g => g.CsvImportMode);
        var workScheduleChange = ResolveRuleMode(org.WorkScheduleChangeMode, userGroups, g => g.WorkScheduleChangeMode);
        var initialOvertime = ResolveRuleMode(org.InitialOvertimeMode, userGroups, g => g.InitialOvertimeMode);
        var absenceSelfService = ResolveRuleMode(org.AbsenceSelfServiceMode, userGroups, g => g.AbsenceSelfServiceMode);
        var vacationVis = ResolveVisibilityMode(org.VacationVisibility, userGroups, g => g.VacationVisibility);
        var sickDayVis = ResolveVisibilityMode(org.SickDayVisibility, userGroups, g => g.SickDayVisibility);
        var timeEntryVis = ResolveVisibilityMode(
            org.MemberTimeEntryVisibility ? VisibilityMode.AdminOnly : VisibilityMode.Private,
            userGroups, g => g.TimeEntryVisibility);

        // Group Admin capabilities (only apply if user is a group admin in any group)
        bool canApproveAbsences = isOrgAdmin || (isGroupAdmin && userGroups.Any(g => g.GroupAdminCanApproveAbsences == true));
        bool canApproveTimeEdits = isOrgAdmin || (isGroupAdmin && userGroups.Any(g => g.GroupAdminCanApproveTimeEdits == true));
        bool canApproveScheduleChanges = isOrgAdmin || (isGroupAdmin && userGroups.Any(g => g.GroupAdminCanApproveScheduleChanges == true));
        bool canManageGroupMembers = isOrgAdmin || (isGroupAdmin && userGroups.Any(g => g.GroupAdminCanManageMembers == true));
        bool canViewAllTimeEntries = isOrgAdmin || (isGroupAdmin && userGroups.Any(g => g.GroupAdminCanViewAllTimeEntries == true));
        bool canExportData = isOrgAdmin || (isGroupAdmin && userGroups.Any(g => g.GroupAdminCanExportData == true));

        return ServiceResult.Ok(new EffectivePermissionsResponse(
            editPastEntries.ToString(),
            editPause.ToString(),
            csvImport.ToString(),
            workScheduleChange.ToString(),
            initialOvertime.ToString(),
            absenceSelfService.ToString(),
            vacationVis.ToString(),
            sickDayVis.ToString(),
            timeEntryVis.ToString(),
            canApproveAbsences,
            canApproveTimeEdits,
            canApproveScheduleChanges,
            canManageGroupMembers,
            canViewAllTimeEntries,
            canExportData
        ));
    }

    /// <summary>Maps negative enum values (sent by the UI as "Inherit") to null (no override).</summary>
    private static T? NormalizeOverride<T>(T? value) where T : struct, Enum
        => value.HasValue && Convert.ToInt32(value.Value) < 0 ? null : value;

    // Same override rule as enforcement (EffectiveRuleResolver.Combine), so what the UI shows is what the API does
    private static RuleMode ResolveRuleMode(RuleMode orgDefault, List<Group> userGroups, Func<Group, RuleMode?> selector)
        => EffectiveRuleResolver.Combine(orgDefault, userGroups.Select(selector));

    private static VisibilityMode ResolveVisibilityMode(VisibilityMode orgDefault, List<Group> userGroups, Func<Group, VisibilityMode?> selector)
        => EffectiveRuleResolver.Combine(orgDefault, userGroups.Select(selector));
}
