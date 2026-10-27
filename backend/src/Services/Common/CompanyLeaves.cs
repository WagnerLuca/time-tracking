using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>
/// Betriebsurlaub as vacation rows (<see cref="CompanyLeave"/>): <see cref="StageAsync"/> makes the generated
/// <see cref="AbsenceDay"/> rows of the given leaves match their definition from a day on, in the change tracker (the
/// caller saves, so the closed-month guard and the balance invalidation see them like any other absence).
/// <para>
/// A member gets a row on a day of the leave when they belonged to it that day (org-wide: a member of the organization;
/// a group's: in the group or a sub-group, by <see cref="GroupMembershipPeriod"/>), were not excluded, it is one of their
/// working days and not a full holiday (a half holiday gets the row too; it costs half a day), and they have no other
/// absence that day (a sick day replaces a generated row, see <c>AbsenceDayService</c>). Too few vacation days left do not
/// matter: the balance goes negative. Members who left without a known date (before <see cref="UserOrganization.LeftAt"/>
/// existed) are left alone.
/// </para>
/// </summary>
public static class CompanyLeaves
{
    public const int MaxDays = 92;
    public const string RangeMessage = "A Betriebsurlaub lasts from 1 to 92 days.";
    public const string ManagedMessage = "This vacation day belongs to a Betriebsurlaub; an admin can leave you out of it.";

    /// <summary>
    /// Stages the rows of <paramref name="leaves"/> (tracked with their <see cref="CompanyLeave.Exclusions"/>, possibly new
    /// or just edited; deleted ones lose all rows) from <paramref name="from"/> on, for <paramref name="userIds"/> or every member.
    /// </summary>
    public static async Task StageAsync(TimeTrackingDbContext db, Organization org, IReadOnlyCollection<CompanyLeave> leaves,
        DateOnly from, IReadOnlyCollection<int>? userIds = null, DateOnly? until = null, int? createdBy = null,
        CancellationToken ct = default)
    {
        if (leaves.Count == 0) return;
        var clock = OrgClock.For(org);
        var leaveIds = leaves.Where(l => l.Id > 0).Select(l => l.Id).ToList();
        var deleted = leaves.Where(l => db.Entry(l).State == EntityState.Deleted).ToHashSet();
        var live = leaves.Where(l => !deleted.Contains(l)).ToList();

        // Rows of these leaves that no longer fit (a deleted leave, a shortened range) go wherever they are
        if (leaveIds.Count > 0)
        {
            var stale = await db.AbsenceDays
                .Where(a => a.OrganizationId == org.Id && a.CompanyLeaveId != null && leaveIds.Contains(a.CompanyLeaveId!.Value))
                .Where(a => a.Date >= from && (until == null || a.Date <= until))
                .ToListAsync(ct);
            foreach (var row in stale)
            {
                var leave = leaves.First(l => l.Id == row.CompanyLeaveId);
                if (deleted.Contains(leave) || row.Date < leave.From || row.Date > leave.To) db.AbsenceDays.Remove(row);
            }
        }
        if (live.Count == 0) return;

        var rangeFrom = live.Min(l => l.From) > from ? live.Min(l => l.From) : from;
        var rangeTo = live.Max(l => l.To);
        if (until < rangeTo) rangeTo = until.Value;
        if (rangeFrom > rangeTo) return;

        var memberships = await db.UserOrganizations.IgnoreQueryFilters().AsNoTracking()
            .Where(uo => uo.OrganizationId == org.Id && (userIds == null || userIds.Contains(uo.UserId)))
            .Select(uo => new { uo.UserId, uo.JoinedAt, uo.IsActive, uo.LeftAt })
            .ToListAsync(ct);
        // Former members without a leaving date keep whatever they have
        memberships = memberships.Where(m => m.IsActive || m.LeftAt != null).ToList();
        var ids = memberships.Select(m => m.UserId).ToList();
        if (ids.Count == 0) return;

        var schedules = (await db.WorkSchedules.AsNoTracking()
                .Where(ws => ws.OrganizationId == org.Id && ids.Contains(ws.UserId)).ToListAsync(ct))
            .GroupBy(ws => ws.UserId).ToDictionary(g => g.Key, g => g.ToList());
        var calendar = await HolidayCalendar.LoadAsync(db, org, ids, rangeFrom, rangeTo, ct);
        var absences = (await db.AbsenceDays
                .Where(a => a.OrganizationId == org.Id && ids.Contains(a.UserId) && a.Date >= rangeFrom && a.Date <= rangeTo)
                .ToListAsync(ct))
            .Concat(db.ChangeTracker.Entries<AbsenceDay>().Where(e => e.State == EntityState.Added).Select(e => e.Entity))
            .Where(a => db.Entry(a).State != EntityState.Deleted)
            .ToLookup(a => (a.UserId, a.Date));
        var groupIds = live.Where(l => l.GroupId != null).Select(l => l.GroupId!.Value).ToList();
        var tree = groupIds.Count == 0 ? null : await GroupDaysAsync(db, org.Id, ids, clock, ct);

        foreach (var leave in live)
        {
            var first = leave.From > rangeFrom ? leave.From : rangeFrom;
            var last = leave.To < rangeTo ? leave.To : rangeTo;
            foreach (var m in memberships)
            {
                var memberSchedules = schedules.GetValueOrDefault(m.UserId) ?? [];
                var joined = clock.LocalDate(m.JoinedAt);
                DateOnly? left = m.LeftAt is { } l ? clock.LocalDate(l) : null;
                var isExcluded = leave.Exclusions.Any(x => x.UserId == m.UserId);
                for (var d = first; d <= last; d = d.AddDays(1))
                {
                    var wanted = !isExcluded && d >= joined && (left == null || d < left)
                        && (leave.GroupId is not int g || tree!.BelongedTo(m.UserId, g, d))
                        && WorkingDays.IsWorkingDay(memberSchedules, d)
                        && calendar.On(m.UserId, d, memberSchedules) is not { IsHalfDay: false };
                    var day = absences[(m.UserId, d)].ToList();
                    var own = day.FirstOrDefault(a => a.CompanyLeave == leave || (leave.Id > 0 && a.CompanyLeaveId == leave.Id));
                    var other = day.Any(a => a != own);
                    if (wanted && own == null && !other)
                    {
                        db.AbsenceDays.Add(new AbsenceDay
                        {
                            UserId = m.UserId, OrganizationId = org.Id, Date = d, Type = AbsenceType.Vacation,
                            Note = leave.Name, CreatedByUserId = createdBy, CompanyLeave = leave
                        });
                    }
                    else if (own != null && (!wanted || other))
                        db.AbsenceDays.Remove(own);
                }
            }
        }
    }

    /// <summary>
    /// Makes room for a member's own absences: removes the generated Betriebsurlaub rows on <paramref name="dates"/> (a sick
    /// day replaces the vacation day it falls on, which goes back to the budget). Staged, the caller saves.
    /// </summary>
    public static async Task ReplaceGeneratedAsync(TimeTrackingDbContext db, int orgId, int userId, IReadOnlyCollection<DateOnly> dates)
    {
        var generated = await db.AbsenceDays
            .Where(a => a.OrganizationId == orgId && a.UserId == userId && a.CompanyLeaveId != null && dates.Contains(a.Date))
            .ToListAsync();
        db.AbsenceDays.RemoveRange(generated);
    }

    /// <summary>What a save changed that can move generated rows: per organization, the members (null = all) and the days.</summary>
    internal sealed class Triggers
    {
        public readonly Dictionary<int, (HashSet<int>? Users, DateOnly? From, DateOnly? Until)> Orgs = [];

        /// <summary>Records <paramref name="userId"/> (null = every member) from tomorrow on, or on one day.</summary>
        public void Add(int orgId, int? userId, DateOnly? day = null)
        {
            if (!Orgs.TryGetValue(orgId, out var t))
            {
                Orgs[orgId] = (userId == null ? null : [userId.Value], day, day);
                return;
            }
            var users = userId == null || t.Users == null ? null : t.Users.Append(userId.Value).ToHashSet();
            // Days only stay days while nothing from tomorrow on is recorded for the organization
            var (from, until) = day == null || t.From == null ? ((DateOnly?)null, (DateOnly?)null)
                : (day < t.From ? day : t.From, day > t.Until ? day : t.Until);
            Orgs[orgId] = (users, from, until);
        }
    }

    /// <summary>
    /// Before a save: the changes that move generated rows — memberships (org, groups), schedules and holidays from
    /// tomorrow on (the past keeps its rows), and a deleted absence on its day (a Betriebsurlaub day comes back after a sick
    /// day is removed). Generated rows themselves trigger nothing. Null when there is nothing to do.
    /// </summary>
    internal static Triggers? Collect(TimeTrackingDbContext db)
    {
        Triggers? t = null;
        foreach (var e in db.ChangeTracker.Entries())
        {
            if (e.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            switch (e.Entity)
            {
                case GroupMembershipPeriod p:
                    (t ??= new()).Add(p.OrganizationId != 0 ? p.OrganizationId : p.Group?.OrganizationId ?? 0, p.UserId);
                    break;
                case UserOrganization uo when e.State == EntityState.Added || e.Property(nameof(UserOrganization.IsActive)).IsModified:
                    (t ??= new()).Add(uo.OrganizationId, uo.UserId);
                    break;
                case WorkSchedule ws:
                    (t ??= new()).Add(ws.OrganizationId, ws.UserId);
                    break;
                case Holiday h:
                    (t ??= new()).Add(h.OrganizationId, null);
                    break;
                case Organization o when e.State == EntityState.Modified && e.Property(nameof(Organization.HolidayRegion)).IsModified:
                    (t ??= new()).Add(o.Id, null);
                    break;
                case Group g when e.State == EntityState.Modified && (e.Property(nameof(Group.HolidayRegion)).IsModified
                        || e.Property(nameof(Group.ParentGroupId)).IsModified):
                    (t ??= new()).Add(g.OrganizationId, null);
                    break;
                case AbsenceDay a when e.State == EntityState.Deleted && a.CompanyLeaveId == null && a.CompanyLeave == null:
                    (t ??= new()).Add(a.OrganizationId, a.UserId, a.Date);
                    break;
            }
        }
        return t;
    }

    /// <summary>After the save: brings the rows of the affected organizations' leaves up to date (a second save).</summary>
    internal static async Task ApplyAsync(TimeTrackingDbContext db, Triggers triggers, CancellationToken ct)
    {
        var changed = false;
        foreach (var (orgId, (users, day, until)) in triggers.Orgs)
        {
            if (orgId == 0) continue;
            var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == orgId, ct);
            if (org == null) continue;
            var from = day ?? OrgClock.For(org).Today().AddDays(1);
            var leaves = await db.CompanyLeaves.Include(l => l.Exclusions)
                .Where(l => l.OrganizationId == orgId && l.To >= from && (until == null || l.From <= until))
                .ToListAsync(ct);
            if (leaves.Count == 0) continue;
            await StageAsync(db, org, leaves, from, users, until, ct: ct);
            changed = true;
        }
        if (changed) await db.SaveChangesAsync(ct);
    }

    /// <summary>The members' group memberships by day (periods + the group tree, deleted groups included).</summary>
    internal sealed class GroupDays(Dictionary<int, int?> parents, ILookup<int, (int GroupId, DateOnly From, DateOnly? Until)> periods)
    {
        public bool BelongedTo(int userId, int groupId, DateOnly date)
        {
            foreach (var (memberGroup, from, until) in periods[userId])
            {
                if (date < from || (until is { } u && date >= u)) continue;
                int? id = memberGroup;
                for (var depth = 0; id is int g && depth < 10; depth++)
                {
                    if (g == groupId) return true;
                    id = parents.GetValueOrDefault(g);
                }
            }
            return false;
        }
    }

    private static async Task<GroupDays> GroupDaysAsync(TimeTrackingDbContext db, int orgId, List<int> ids, OrgClock clock, CancellationToken ct)
    {
        var parents = await db.Groups.IgnoreQueryFilters().AsNoTracking().Where(g => g.OrganizationId == orgId)
            .ToDictionaryAsync(g => g.Id, g => g.ParentGroupId, ct);
        var periods = await db.GroupMembershipPeriods.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.OrganizationId == orgId && ids.Contains(p.UserId))
            .Select(p => new { p.UserId, p.GroupId, p.From, p.Until })
            .ToListAsync(ct);
        return new GroupDays(parents, periods.ToLookup(p => p.UserId,
            p => (p.GroupId, clock.LocalDate(p.From), p.Until is { } u ? clock.LocalDate(u) : (DateOnly?)null)));
    }
}
