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

    public const string ParentNotFoundMessage = "Parent group not found.";
    public const string ParentOrTopLevelMessage = "Choose either a parent group or the top level, not both.";
    public const string HasSubGroupsMessage = "This group has sub-groups. Choose whether to move them up one level or delete them with it.";
    public const string UnknownCapabilityMessage = "Unknown group admin capability.";
    public const string CoreHoursToleranceMessage = "The core hours tolerance must be between 0 and 120 minutes.";

    /// <summary>The member-management capability (an <see cref="GroupTree.Effective"/> group's value).</summary>
    private static bool ManagesMembers(Group g) => g.GroupAdminCanManageMembers == true;

    /// <summary>Group admins of this group or of an ancestor whose effective settings allow member management.</summary>
    private async Task<bool> CanGroupAdminManageAsync(int orgId, int groupId, int userId) =>
        (await GroupTree.LoadAsync(_context, orgId)).AdministersWith(userId, groupId, ManagesMembers);

    private static GroupMemberResponse ToMemberResponse(GroupMember gm, MemberVisibility? visibility = null, string? via = null) => new(
        gm.UserId,
        gm.User.FirstName,
        gm.User.LastName,
        visibility == null || visibility.Can(gm.UserId, MemberInfo.Email) ? gm.User.Email : null,
        gm.User.ProfileImageUrl,
        gm.Role.ToString(),
        gm.JoinedAt,
        via
    );

    /// <summary>Being listed in a group reveals the membership: only people whose groups the caller may see.</summary>
    private static bool Listed(MemberVisibility visibility, int userId) =>
        visibility.Can(userId, MemberInfo.Member | MemberInfo.Groups);

    private static GroupDetailResponse ToDetailResponse(Group g, IList<GroupMember> members, MemberVisibility? visibility = null,
        IEnumerable<GroupMemberResponse>? inherited = null) => new(
        g.Id,
        g.OrganizationId,
        g.Name,
        g.Description,
        g.Color,
        g.CreatedAt,
        members.Where(m => visibility == null || Listed(visibility, m.UserId)).Select(m => ToMemberResponse(m, visibility))
            .Concat(inherited ?? []).ToList(),
        g.ParentGroupId
    );

    // ── Queries ─────────────────────────────────────────────────────────────

    public async Task<ServiceResult<List<GroupResponse>>> GetGroupsAsync(string slug, int callerUserId)
    {
        var (org, membership) = await ResolveOrgMemberAsync(slug, callerUserId);
        if (org is null) return ServiceResult.NotFound<List<GroupResponse>>("Organization not found.");
        if (membership is null) return ServiceResult.Forbidden<List<GroupResponse>>("You are not a member of this organization.");

        var visibility = await MemberVisibility.ForCallerAsync(_context, org, callerUserId);
        var tree = visibility.Tree;
        var isOrgAdmin = membership.Role >= OrganizationRole.Admin;

        var result = tree.Groups.OrderBy(g => g.Name).Select(g =>
        {
            var myMembership = g.Members.FirstOrDefault(m => m.UserId == callerUserId);
            return new GroupResponse(
                g.Id,
                g.OrganizationId,
                g.Name,
                g.Description,
                g.Color,
                g.CreatedAt,
                g.Members.Count(m => Listed(visibility, m.UserId)),
                IsGroupAdmin: myMembership?.Role == GroupRole.Admin,
                GroupAdminCanManageMembers: ManagesMembers(tree.Effective(g.Id)),
                ParentGroupId: tree.ParentOf(g.Id),
                CanManageMembers: isOrgAdmin || tree.AdministersWith(callerUserId, g.Id, ManagesMembers)
            );
        }).ToList();

        return ServiceResult.Ok(result);
    }

    public async Task<ServiceResult<GroupDetailResponse>> GetGroupAsync(string slug, int groupId, int callerUserId)
    {
        var (org, membership) = await ResolveOrgMemberAsync(slug, callerUserId);
        if (org is null) return ServiceResult.NotFound<GroupDetailResponse>("Organization not found.");
        if (membership is null) return ServiceResult.Forbidden<GroupDetailResponse>("You are not a member of this organization.");

        var visibility = await MemberVisibility.ForCallerAsync(_context, org, callerUserId);
        var group = visibility.Tree.Find(groupId);
        if (group is null) return ServiceResult.NotFound<GroupDetailResponse>("Group not found.");

        // Direct members, then the members of the sub-groups (inherited membership, once each)
        var subtree = visibility.Tree.Subtree(groupId).ToList();
        var rows = await _context.GroupMembers.AsNoTracking()
            .Include(gm => gm.User)
            .Where(gm => subtree.Contains(gm.GroupId))
            .ToListAsync();
        var direct = rows.Where(r => r.GroupId == groupId).ToList();
        var seen = direct.Select(r => r.UserId).ToHashSet();
        var inherited = subtree.Skip(1)
            .SelectMany(id => rows.Where(r => r.GroupId == id).OrderBy(r => r.User.LastName))
            .Where(r => Listed(visibility, r.UserId) && seen.Add(r.UserId))
            .Select(r => ToMemberResponse(r, visibility, visibility.Tree.Find(r.GroupId)!.Name) with { Role = nameof(GroupRole.Member) })
            .ToList();
        return ServiceResult.Ok(ToDetailResponse(group, direct, visibility, inherited));
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    /// <summary>Org admins create any group; a group admin may create sub-groups below a group they lead with member management.</summary>
    public async Task<ServiceResult<GroupDetailResponse>> CreateGroupAsync(string slug, int callerUserId, CreateGroupRequest request)
    {
        var (org, membership) = await ResolveOrgMemberAsync(slug, callerUserId);
        if (org is null) return ServiceResult.NotFound<GroupDetailResponse>("Organization not found.");
        var isOrgAdmin = membership is { Role: >= OrganizationRole.Admin };
        if (membership is null || (!isOrgAdmin && request.ParentGroupId is null))
            return ServiceResult.Forbidden<GroupDetailResponse>("Only admins can create groups.");

        if (request.ParentGroupId is { } parentId)
        {
            var tree = await GroupTree.LoadAsync(_context, org.Id);
            if (tree.Find(parentId) is null)
                return ServiceResult.BadRequest<GroupDetailResponse>(ParentNotFoundMessage);
            if (!isOrgAdmin && !tree.AdministersWith(callerUserId, parentId, ManagesMembers))
                return ServiceResult.Forbidden<GroupDetailResponse>("Only admins can create groups.");
            if (tree.CheckPlacement(null, parentId) is { } placementError)
                return ServiceResult.BadRequest<GroupDetailResponse>(placementError);
        }

        var group = new Group
        {
            OrganizationId = org.Id,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Color = request.Color?.Trim(),
            ParentGroupId = request.ParentGroupId,
        };

        _context.Groups.Add(group);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Group {GroupId} '{Name}' created in org {Slug} (parent {ParentId}) by user {UserId}",
            group.Id, group.Name, slug, group.ParentGroupId, callerUserId);

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

        if (request.MoveToTopLevel && request.ParentGroupId != null)
            return ServiceResult.BadRequest<GroupDetailResponse>(ParentOrTopLevelMessage);
        if (request.ParentGroupId != group.ParentGroupId && request.ParentGroupId is { } parentId)
        {
            var tree = await GroupTree.LoadAsync(_context, org.Id);
            if (tree.Find(parentId) is null)
                return ServiceResult.BadRequest<GroupDetailResponse>(ParentNotFoundMessage);
            if (tree.CheckPlacement(groupId, parentId) is { } placementError)
                return ServiceResult.BadRequest<GroupDetailResponse>(placementError);
        }

        group.Name = request.Name.Trim();
        group.Description = request.Description?.Trim();
        group.Color = request.Color?.Trim();
        if (request.MoveToTopLevel) group.ParentGroupId = null;
        else if (request.ParentGroupId != null) group.ParentGroupId = request.ParentGroupId;
        group.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return ServiceResult.Ok(ToDetailResponse(group, group.Members.ToList()));
    }

    /// <summary>
    /// Deactivates the group. Its sub-groups are never left orphaned: with
    /// <see cref="SubGroupAction.MoveUp"/> they move to the group's parent, with
    /// <see cref="SubGroupAction.Delete"/> the whole subtree goes; without a choice a group with
    /// sub-groups is not deleted.
    /// </summary>
    public async Task<ServiceResult> DeleteGroupAsync(string slug, int groupId, int callerUserId, SubGroupAction? subGroups = null)
    {
        var (org, membership) = await ResolveOrgMemberAsync(slug, callerUserId);
        if (org is null) return ServiceResult.NotFound("Organization not found.");
        if (membership is null || membership.Role < OrganizationRole.Admin)
            return ServiceResult.Forbidden("Only admins can delete groups.");

        var groups = await _context.Groups.Where(g => g.OrganizationId == org.Id && g.IsActive).ToListAsync();
        var group = groups.FirstOrDefault(g => g.Id == groupId);
        if (group is null) return ServiceResult.NotFound("Group not found.");

        var tree = new GroupTree(groups);
        var children = tree.ChildrenOf(groupId).ToList();
        if (children.Count > 0 && subGroups is null)
            return ServiceResult.BadRequest(HasSubGroupsMessage);

        var now = DateTime.UtcNow;
        var removed = subGroups == SubGroupAction.Delete ? tree.Subtree(groupId).ToHashSet() : [groupId];
        var newParent = tree.ParentOf(groupId);
        foreach (var g in groups)
        {
            if (removed.Contains(g.Id)) g.IsActive = false;
            else if (children.Contains(g.Id)) g.ParentGroupId = newParent;
            else continue;
            g.UpdatedAt = now;
        }
        await _context.SaveChangesAsync();

        _logger.LogInformation("Group {GroupId} deleted in org {Slug} by user {UserId} (sub-groups: {SubGroups})", groupId, slug, callerUserId, subGroups);

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
        if (!isOrgAdmin && !await CanGroupAdminManageAsync(org.Id, groupId, callerUserId))
            return ServiceResult.Forbidden<GroupMemberResponse>("Only org admins, or group admins with member management rights, can add members.");

        // Target user must be an org member
        var targetMembership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == request.UserId);
        if (targetMembership is null
            || (!isOrgAdmin && !(await MemberVisibility.ForCallerAsync(_context, org, callerUserId)).Can(request.UserId, MemberInfo.Member)))
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
        var groupAdminMayRemove = !targetIsGroupAdmin && await CanGroupAdminManageAsync(org.Id, groupId, callerUserId);

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
        MemberVisibility.Rows(group),
        group.GroupAdminCanApproveAbsences,
        group.GroupAdminCanApproveTimeEdits,
        group.GroupAdminCanApproveScheduleChanges,
        group.GroupAdminCanManageMembers,
        group.GroupAdminCanViewAllTimeEntries,
        group.GroupAdminCanExportData,
        group.CoreHoursMode?.ToString(),
        group.CoreHoursStart?.ToString("HH:mm"),
        group.CoreHoursEnd?.ToString("HH:mm"),
        group.ParentGroupId,
        group.CoreHoursToleranceMinutes
    );

    /// <summary>Resets a capability flag to "inherit", by its request/response property name.</summary>
    private static readonly Dictionary<string, Action<Group>> CapabilityResets = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(Group.GroupAdminCanApproveAbsences)] = g => g.GroupAdminCanApproveAbsences = null,
        [nameof(Group.GroupAdminCanApproveTimeEdits)] = g => g.GroupAdminCanApproveTimeEdits = null,
        [nameof(Group.GroupAdminCanApproveScheduleChanges)] = g => g.GroupAdminCanApproveScheduleChanges = null,
        [nameof(Group.GroupAdminCanManageMembers)] = g => g.GroupAdminCanManageMembers = null,
        [nameof(Group.GroupAdminCanViewAllTimeEntries)] = g => g.GroupAdminCanViewAllTimeEntries = null,
        [nameof(Group.GroupAdminCanExportData)] = g => g.GroupAdminCanExportData = null,
    };

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

    public async Task<ServiceResult<List<GroupPermissionsResponse>>> GetAllPermissionsAsync(string slug, int callerUserId)
    {
        var (org, membership) = await ResolveOrgMemberAsync(slug, callerUserId);
        if (org is null) return ServiceResult.NotFound<List<GroupPermissionsResponse>>("Organization not found.");
        if (membership is null) return ServiceResult.Forbidden<List<GroupPermissionsResponse>>("You are not a member of this organization.");

        var groups = await _context.Groups.AsNoTracking()
            .Where(g => g.OrganizationId == org.Id)
            .OrderBy(g => g.Name)
            .ToListAsync();
        return ServiceResult.Ok(groups.Select(ToPermissionsResponse).ToList());
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
        if (request.Visibility != null && MemberVisibility.Apply(group, request.Visibility) is { } visibilityError)
            return ServiceResult.BadRequest<GroupPermissionsResponse>(visibilityError);
        if (request.GroupAdminCanApproveAbsences.HasValue) group.GroupAdminCanApproveAbsences = request.GroupAdminCanApproveAbsences;
        if (request.GroupAdminCanApproveTimeEdits.HasValue) group.GroupAdminCanApproveTimeEdits = request.GroupAdminCanApproveTimeEdits;
        if (request.GroupAdminCanApproveScheduleChanges.HasValue) group.GroupAdminCanApproveScheduleChanges = request.GroupAdminCanApproveScheduleChanges;
        if (request.GroupAdminCanManageMembers.HasValue) group.GroupAdminCanManageMembers = request.GroupAdminCanManageMembers;
        if (request.GroupAdminCanViewAllTimeEntries.HasValue) group.GroupAdminCanViewAllTimeEntries = request.GroupAdminCanViewAllTimeEntries;
        if (request.GroupAdminCanExportData.HasValue) group.GroupAdminCanExportData = request.GroupAdminCanExportData;
        foreach (var name in request.InheritCapabilities ?? [])
        {
            if (!CapabilityResets.TryGetValue(name, out var reset))
                return ServiceResult.BadRequest<GroupPermissionsResponse>(UnknownCapabilityMessage);
            reset(group);
        }
        var coreBefore = (group.CoreHoursMode, group.CoreHoursStart, group.CoreHoursEnd, group.CoreHoursToleranceMinutes);
        if (request.CoreHoursMode.HasValue)
        {
            group.CoreHoursMode = NormalizeOverride(request.CoreHoursMode);
            TimeOnly? start = null, end = null;
            if (group.CoreHoursMode == CoreHoursMode.Own)
            {
                // End before start = past midnight (night shift); only an empty window is invalid
                if (!TimeOnly.TryParseExact(request.CoreHoursStart, "HH:mm", out var s) || !TimeOnly.TryParseExact(request.CoreHoursEnd, "HH:mm", out var e) || s == e)
                    return ServiceResult.BadRequest<GroupPermissionsResponse>(OrganizationService.CoreHoursOrderMessage);
                (start, end) = (s, e);
            }
            group.CoreHoursStart = start;
            group.CoreHoursEnd = end;
        }
        if (request.CoreHoursToleranceMinutes is { } tolerance)
        {
            if (tolerance > 120) return ServiceResult.BadRequest<GroupPermissionsResponse>(CoreHoursToleranceMessage);
            group.CoreHoursToleranceMinutes = tolerance < 0 ? null : tolerance;   // negative = inherit
        }
        if (request.CoreHoursBackfillDays is < 1 or > CoreHours.MaxBackfillDays)
            return ServiceResult.BadRequest<GroupPermissionsResponse>(OrganizationService.CoreHoursBackfillMessage);
        if (coreBefore != (group.CoreHoursMode, group.CoreHoursStart, group.CoreHoursEnd, group.CoreHoursToleranceMinutes) || request.CoreHoursBackfillDays != null)
            OrganizationService.CoreHoursChanged(org, group.CoreHoursMode == CoreHoursMode.Own ? request.CoreHoursBackfillDays : null);

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

        // The target's groups with their effective (inherited along the parent chain) settings
        var tree = await GroupTree.LoadAsync(_context, org.Id);
        var userGroups = tree.EffectiveGroupsOf(userId).ToList();

        // Resolve permissions: most permissive group override wins
        var editPastEntries = ResolveRuleMode(org.EditPastEntriesMode, userGroups, g => g.EditPastEntriesMode);
        var editPause = ResolveRuleMode(org.EditPauseMode, userGroups, g => g.EditPauseMode);
        var csvImport = ResolveRuleMode(org.CsvImportMode, userGroups, g => g.CsvImportMode);
        var workScheduleChange = ResolveRuleMode(org.WorkScheduleChangeMode, userGroups, g => g.WorkScheduleChangeMode);
        var initialOvertime = ResolveRuleMode(org.InitialOvertimeMode, userGroups, g => g.InitialOvertimeMode);
        var absenceSelfService = ResolveRuleMode(org.AbsenceSelfServiceMode, userGroups, g => g.AbsenceSelfServiceMode);
        var (visibleInGroups, visibleOutside) = MemberVisibility.EffectiveFor(org, userGroups);

        // Group Admin capabilities: the effective capabilities of the groups the user administers
        // (they apply to those groups' members and all their sub-groups)
        bool Can(Func<Group, bool> capability) => isOrgAdmin || tree.AdministeredBy(userId).Any(id => capability(tree.Effective(id)));
        bool canApproveAbsences = Can(g => g.GroupAdminCanApproveAbsences == true);
        bool canApproveTimeEdits = Can(g => g.GroupAdminCanApproveTimeEdits == true);
        bool canApproveScheduleChanges = Can(g => g.GroupAdminCanApproveScheduleChanges == true);
        bool canManageGroupMembers = Can(ManagesMembers);
        bool canViewAllTimeEntries = Can(g => g.GroupAdminCanViewAllTimeEntries == true);
        bool canExportData = Can(g => g.GroupAdminCanExportData == true);

        return ServiceResult.Ok(new EffectivePermissionsResponse(
            editPastEntries.ToString(),
            editPause.ToString(),
            csvImport.ToString(),
            workScheduleChange.ToString(),
            initialOvertime.ToString(),
            absenceSelfService.ToString(),
            MemberVisibility.Rows(visibleInGroups, visibleOutside),
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
}
