using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>
/// Keeps <see cref="GroupMembershipPeriod"/> in step with <see cref="GroupMember"/> (and <see cref="UserOrganization.LeftAt"/>
/// with <see cref="UserOrganization.IsActive"/>): run by
/// <c>TimeTrackingDbContext.SaveChangesAsync</c> for every save, it opens a period for every added membership and closes the
/// open one of every removed membership and of every member of a deactivated group, in the same save. So every path that
/// changes memberships (group pages, sub-group deletion, leaving the organization, account deletion) is covered without
/// code of its own.
/// </summary>
internal static class MembershipPeriods
{
    public static async Task StageAsync(TimeTrackingDbContext db, CancellationToken ct)
    {
        List<GroupMember>? added = null;
        List<(int GroupId, int UserId)>? removed = null;
        List<int>? deactivated = null;
        foreach (var e in db.ChangeTracker.Entries())
        {
            switch (e.Entity)
            {
                case GroupMember m when e.State == EntityState.Added:
                    (added ??= []).Add(m);
                    break;
                case GroupMember m when e.State == EntityState.Deleted:
                    (removed ??= []).Add(((int)e.Property(nameof(GroupMember.GroupId)).OriginalValue!,
                        (int)e.Property(nameof(GroupMember.UserId)).OriginalValue!));
                    break;
                case Group g when e.State == EntityState.Modified && e.Property(nameof(Group.IsActive)).IsModified && !g.IsActive:
                    (deactivated ??= []).Add(g.Id);
                    break;
                case UserOrganization uo when e.State == EntityState.Modified && e.Property(nameof(UserOrganization.IsActive)).IsModified:
                    uo.LeftAt = uo.IsActive ? null : DateTime.UtcNow;
                    break;
            }
        }
        if (added == null && removed == null && deactivated == null) return;

        var now = DateTime.UtcNow;
        if (removed != null || deactivated != null)
        {
            var groupIds = (removed?.Select(r => r.GroupId) ?? []).Concat(deactivated ?? []).Distinct().ToList();
            var open = await db.GroupMembershipPeriods.IgnoreQueryFilters()
                .Where(p => groupIds.Contains(p.GroupId) && p.Until == null)
                .ToListAsync(ct);
            foreach (var p in open)
                if (deactivated?.Contains(p.GroupId) == true || removed!.Contains((p.GroupId, p.UserId)))
                    p.Until = now;
        }

        if (added == null) return;
        // The group of each membership: tracked (a new group too — its temporary key is the membership's GroupId), else looked up
        var tracked = db.ChangeTracker.Entries<Group>().Select(e => e.Entity).DistinctBy(g => g.Id).ToDictionary(g => g.Id);
        var lookup = added.Where(m => (m.Group ?? tracked.GetValueOrDefault(m.GroupId)) == null).Select(m => m.GroupId).Distinct().ToList();
        var orgOf = lookup.Count == 0 ? [] : await db.Groups.IgnoreQueryFilters().AsNoTracking()
            .Where(g => lookup.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.OrganizationId, ct);
        foreach (var m in added)
        {
            var group = m.Group ?? tracked.GetValueOrDefault(m.GroupId);
            var period = new GroupMembershipPeriod
            {
                UserId = m.UserId,
                OrganizationId = group?.OrganizationId ?? orgOf.GetValueOrDefault(m.GroupId),
                From = m.JoinedAt == default ? now : m.JoinedAt
            };
            // New rows of the same save (seeders add organization, users, groups and memberships at once) are linked by
            // navigation, so their keys are fixed up on save
            if (group != null) period.Group = group; else period.GroupId = m.GroupId;
            if (period.OrganizationId == 0 && group?.Organization != null) period.Organization = group.Organization;
            if (m.User != null) period.User = m.User;
            db.GroupMembershipPeriods.Add(period);
        }
    }
}
