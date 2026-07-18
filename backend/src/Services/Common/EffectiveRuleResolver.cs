using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>
/// Resolves the effective <see cref="RuleMode"/> for a user by combining the
/// organization default with the user's group overrides (most permissive wins).
/// Shared by services that enforce rules or gate requests.
/// </summary>
public static class EffectiveRuleResolver
{
    public static async Task<RuleMode> ResolveAsync(
        TimeTrackingDbContext context, Organization org, int userId, RequestType action)
    {
        var orgMode = OrgRuleHelper.GetRuleMode(org, action);

        var userGroups = await context.GroupMembers
            .AsNoTracking()
            .Include(gm => gm.Group)
            .Where(gm => gm.UserId == userId
                      && gm.Group.OrganizationId == org.Id
                      && gm.Group.IsActive)
            .Select(gm => gm.Group)
            .ToListAsync();

        var overrides = userGroups
            .Select(g => OrgRuleHelper.GetGroupRuleMode(g, action))
            .Where(m => m.HasValue)
            .Select(m => m!.Value)
            .ToList();

        if (overrides.Count == 0) return orgMode;

        // Most permissive wins (Allowed > RequiresApproval > Disabled), but never below org default
        var mostPermissive = overrides.Max();
        return mostPermissive > orgMode ? mostPermissive : orgMode;
    }

    /// <summary>
    /// True when the user can approve absence requests in this org:
    /// org Admin/Owner, or Group Admin of a group with the approve-absences capability.
    /// </summary>
    public static async Task<bool> CanApproveAbsencesAsync(
        TimeTrackingDbContext context, int orgId, int userId)
    {
        var role = await context.UserOrganizations
            .AsNoTracking()
            .Where(uo => uo.OrganizationId == orgId && uo.UserId == userId)
            .Select(uo => (OrganizationRole?)uo.Role)
            .FirstOrDefaultAsync();

        if (role >= OrganizationRole.Admin) return true;

        return await context.GroupMembers
            .AsNoTracking()
            .AnyAsync(gm => gm.UserId == userId
                         && gm.Role == GroupRole.Admin
                         && gm.Group.OrganizationId == orgId
                         && gm.Group.IsActive
                         && gm.Group.GroupAdminCanApproveAbsences == true);
    }

    /// <summary>
    /// True when the caller may export/print reports for the given target member:
    /// the member themselves, an org Admin/Owner, or a Group Admin of a shared
    /// group that has the export-data capability enabled.
    /// </summary>
    public static async Task<bool> CanExportDataForMemberAsync(
        TimeTrackingDbContext context, int orgId, int callerUserId, int targetUserId)
    {
        if (callerUserId == targetUserId) return true;

        var callerRole = await context.UserOrganizations
            .AsNoTracking()
            .Where(uo => uo.OrganizationId == orgId && uo.UserId == callerUserId)
            .Select(uo => (OrganizationRole?)uo.Role)
            .FirstOrDefaultAsync();

        if (callerRole >= OrganizationRole.Admin) return true;

        var callerAdminGroupIds = await context.GroupMembers
            .AsNoTracking()
            .Where(gm => gm.UserId == callerUserId
                      && gm.Role == GroupRole.Admin
                      && gm.Group.OrganizationId == orgId
                      && gm.Group.IsActive
                      && gm.Group.GroupAdminCanExportData == true)
            .Select(gm => gm.GroupId)
            .ToListAsync();

        if (callerAdminGroupIds.Count == 0) return false;

        return await context.GroupMembers
            .AsNoTracking()
            .AnyAsync(gm => gm.UserId == targetUserId && callerAdminGroupIds.Contains(gm.GroupId));
    }
}
