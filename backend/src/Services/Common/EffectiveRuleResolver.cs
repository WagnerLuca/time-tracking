using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>
/// Resolves the effective <see cref="RuleMode"/> for a user by combining the
/// organization default with the user's group overrides (see <see cref="Combine{T}"/>).
/// Shared by services that enforce rules or gate requests.
/// </summary>
public static class EffectiveRuleResolver
{
    /// <summary>
    /// The one override rule: a group override replaces the organization default in either
    /// direction (a group may be stricter or looser than the org); with several overriding
    /// groups the most permissive one wins; without any override the org default applies.
    /// Enum order is the permissiveness order (Disabled &lt; RequiresApproval &lt; Allowed,
    /// Private &lt; AdminOnly &lt; AllMembers).
    /// </summary>
    public static T Combine<T>(T orgDefault, IEnumerable<T?> groupOverrides) where T : struct, Enum
    {
        var overrides = groupOverrides.Where(o => o.HasValue).Select(o => o!.Value).ToList();
        return overrides.Count == 0 ? orgDefault : overrides.Max();
    }

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

        return Combine(orgMode, userGroups.Select(g => OrgRuleHelper.GetGroupRuleMode(g, action)));
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

        if (callerRole == null) return false;

        // "Admins may see members' time entries" is the organization's own privacy setting;
        // exports, PDFs and balances are just other ways of seeing the same hours
        if (callerRole >= OrganizationRole.Admin)
            return await context.Organizations.AsNoTracking()
                .Where(o => o.Id == orgId).Select(o => o.MemberTimeEntryVisibility).FirstOrDefaultAsync();

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
