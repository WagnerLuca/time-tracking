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
    /// Windows past midnight (end before start, night shifts) are compared on the timeline of the
    /// day they start on, so 22:00–02:00 and 23:00–06:00 give 23:00–02:00, while a day window and
    /// a night window do not overlap (→ off).
    /// ponytail: a night window is not also shifted back a day, so 22:00–06:00 with 05:00–10:00 is
    /// off rather than 05:00–06:00; shift by −24 h too if such mixes show up.
    /// Inheriting groups (null) have no say once another group overrides. Pass effective groups
    /// (<see cref="GroupTree.EffectiveGroupsOf"/>) so sub-groups inherit their parent's window.
    /// </summary>
    public static CoreWindow? CombineCoreHours(Organization org, IEnumerable<Group> groups)
    {
        var overrides = groups.Where(g => g.CoreHoursMode != null).ToList();
        if (overrides.Count == 0)
            return org.CoreHoursEnabled ? new CoreWindow(org.CoreHoursStart, org.CoreHoursEnd) : null;
        if (overrides.Any(g => g.CoreHoursMode == CoreHoursMode.Off || g.CoreHoursStart == null || g.CoreHoursEnd == null))
            return null;
        var spans = overrides.Select(g => new CoreWindow(g.CoreHoursStart!.Value, g.CoreHoursEnd!.Value).Minutes).ToList();
        var start = spans.Max(s => s.Start);
        var end = spans.Min(s => s.End);
        return start < end ? new CoreWindow(TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(start % 1440)),
            TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(end % 1440))) : null;
    }

    /// <summary>
    /// The core hours tolerance under <see cref="Combine{T}"/>'s rule: a group's own tolerance
    /// (<see cref="Group.CoreHoursToleranceMinutes"/>, inherited down sub-groups by
    /// <see cref="GroupTree.Effective"/>) replaces the organization's in either direction; with
    /// several the most permissive, i.e. the largest, wins; without any the organization's.
    /// Independent of which group the window comes from.
    /// </summary>
    public static int CombineCoreTolerance(Organization org, IEnumerable<Group> groups) =>
        groups.Max(g => g.CoreHoursToleranceMinutes) ?? org.CoreHoursToleranceMinutes;

    /// <summary>The user's effective rule for the action: the org default combined with the effective
    /// values (sub-groups inherit along <see cref="GroupTree"/>) of the user's groups.</summary>
    public static async Task<RuleMode> ResolveAsync(
        TimeTrackingDbContext context, Organization org, int userId, RequestType action)
    {
        var tree = await GroupTree.LoadAsync(context, org.Id);
        return Combine(OrgRuleHelper.GetRuleMode(org, action),
            tree.EffectiveGroupsOf(userId).Select(g => OrgRuleHelper.GetGroupRuleMode(g, action)));
    }

    /// <summary>
    /// True when the user can approve absence requests in this org:
    /// org Admin/Owner, or Group Admin of a group whose effective capabilities approve absences.
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

        var tree = await GroupTree.LoadAsync(context, orgId);
        return tree.AdministeredBy(userId).Any(id => tree.Effective(id).GroupAdminCanApproveAbsences == true);
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
