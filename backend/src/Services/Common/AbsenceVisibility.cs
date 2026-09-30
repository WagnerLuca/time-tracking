using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>
/// Who may see whose vacation / sick days. A member's effective visibility is their groups'
/// overrides combined (<see cref="EffectiveRuleResolver.Combine{T}"/>: a group override replaces
/// the org default in either direction, the most permissive group wins) or else the org default:
/// <list type="bullet">
/// <item><c>AllMembers</c> — every org member can see it</item>
/// <item><c>AdminOnly</c> — org admins, plus group admins of a group the member belongs to</item>
/// <item><c>Private</c> — org admins only</item>
/// </list>
/// Members always see their own entries; org admins/owners see everything.
/// </summary>
public static class AbsenceVisibility
{
    public sealed record Visible(HashSet<int> Vacation, HashSet<int> SickDay)
    {
        /// <summary>"Other"/custom absences reveal as much as both kinds, so they need both.</summary>
        public HashSet<int> Other => Vacation.Intersect(SickDay).ToHashSet();
    }

    public static async Task<Visible> ForCallerAsync(TimeTrackingDbContext context, Organization org, int callerUserId)
    {
        var memberIds = await context.UserOrganizations.AsNoTracking()
            .Where(uo => uo.OrganizationId == org.Id)
            .Select(uo => new { uo.UserId, uo.Role })
            .ToListAsync();

        if (memberIds.Any(m => m.UserId == callerUserId && m.Role >= OrganizationRole.Admin))
        {
            var all = memberIds.Select(m => m.UserId).ToHashSet();
            return new Visible(all, all.ToHashSet());
        }

        var groupMembers = await context.GroupMembers.AsNoTracking()
            .Where(gm => gm.Group.OrganizationId == org.Id && gm.Group.IsActive)
            .Select(gm => new { gm.UserId, gm.GroupId, gm.Role, gm.Group.VacationVisibility, gm.Group.SickDayVisibility })
            .ToListAsync();

        var callerAdminGroupIds = groupMembers
            .Where(gm => gm.UserId == callerUserId && gm.Role == GroupRole.Admin)
            .Select(gm => gm.GroupId)
            .ToHashSet();
        var managedByCaller = groupMembers
            .Where(gm => callerAdminGroupIds.Contains(gm.GroupId))
            .Select(gm => gm.UserId)
            .ToHashSet();

        var vacation = new HashSet<int> { callerUserId };
        var sick = new HashSet<int> { callerUserId };
        foreach (var userId in memberIds.Select(m => m.UserId))
        {
            var groups = groupMembers.Where(gm => gm.UserId == userId).ToList();
            var vacationMode = EffectiveRuleResolver.Combine(org.VacationVisibility, groups.Select(g => g.VacationVisibility));
            var sickMode = EffectiveRuleResolver.Combine(org.SickDayVisibility, groups.Select(g => g.SickDayVisibility));

            if (IsVisible(vacationMode, managedByCaller.Contains(userId))) vacation.Add(userId);
            if (IsVisible(sickMode, managedByCaller.Contains(userId))) sick.Add(userId);
        }

        return new Visible(vacation, sick);
    }

    private static bool IsVisible(VisibilityMode mode, bool callerIsTheirGroupAdmin) =>
        mode >= VisibilityMode.AllMembers || (mode >= VisibilityMode.AdminOnly && callerIsTheirGroupAdmin);
}
