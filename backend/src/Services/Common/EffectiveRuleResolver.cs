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
    /// Enum order is the permissiveness order (Disabled &lt; RequiresApproval &lt; Allowed).
    /// The member visibility matrix applies the same rule per kind (<see cref="MemberVisibility.Combine"/>).
    /// </summary>
    public static T Combine<T>(T orgDefault, IEnumerable<T?> groupOverrides) where T : struct, Enum
    {
        var overrides = groupOverrides.Where(o => o.HasValue).Select(o => o!.Value).ToList();
        return overrides.Count == 0 ? orgDefault : overrides.Max();
    }

    /// <summary>
    /// <see cref="Combine{T}"/> for core working hours, where "more permissive" means fewer
    /// core hours: without any group override the organization's window applies (null when the
    /// org has core hours off); a group set to Off wins over every window; otherwise the
    /// shortest window wins, i.e. the latest start and the earliest end of the groups' own
    /// windows (which may come from different groups). An empty result counts as off.
    /// Inheriting groups (null) have no say once another group overrides.
    /// </summary>
    public static CoreWindow? CombineCoreHours(Organization org, IEnumerable<Group> groups)
    {
        var overrides = groups.Where(g => g.CoreHoursMode != null).ToList();
        if (overrides.Count == 0)
            return org.CoreHoursEnabled ? new CoreWindow(org.CoreHoursStart, org.CoreHoursEnd) : null;
        if (overrides.Any(g => g.CoreHoursMode == CoreHoursMode.Off || g.CoreHoursStart == null || g.CoreHoursEnd == null))
            return null;
        var start = overrides.Max(g => g.CoreHoursStart!.Value);
        var end = overrides.Min(g => g.CoreHoursEnd!.Value);
        return start < end ? new CoreWindow(start, end) : null;
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
    /// True when the caller may see the target member's time entries in this org (kind
    /// <see cref="MemberInfo.TrackedTime"/> of <see cref="MemberVisibility"/>: the member themselves,
    /// admins while the org lets them see members' hours, group admin rights, the matrix).
    /// The caller must meet the org's 2FA requirement.
    /// </summary>
    public static async Task<bool> CanViewMemberEntriesAsync(
        TimeTrackingDbContext context, int orgId, int callerUserId, int targetUserId)
    {
        var caller = await context.UserOrganizations.AsNoTracking()
            .Where(uo => uo.OrganizationId == orgId && uo.UserId == callerUserId)
            .Select(uo => new { uo.Organization, uo.User.TwoFactorEnabled })
            .FirstOrDefaultAsync();
        if (caller == null || (caller.Organization.Require2fa && !caller.TwoFactorEnabled)) return false;
        return (await MemberVisibility.ForCallerAsync(context, caller.Organization, callerUserId)).Can(targetUserId, MemberInfo.TrackedTime);
    }
}
