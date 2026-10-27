using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

public enum DayOffSource { Public, Company }

/// <summary>A holiday on one member's day: a public one of their region or a company holiday (org-wide or of a group).</summary>
public sealed record DayOff(string Name, bool IsHalfDay, DayOffSource Source);

/// <summary>
/// The one answer to "is this a holiday for this member?" (<c>docs/DAYS_OFF_DESIGN.md</c>), used by every reader:
/// balances and reports, core hours, vacation counting, the calendars.
/// <list type="bullet">
/// <item>Public holidays are calculated from the region valid that day: the member's work schedule
/// (<see cref="WorkSchedule.HolidayRegion"/>), else the groups they belonged to that day (<see cref="Group.HolidayRegion"/>,
/// inherited down sub-groups; two different ones count as none), else <see cref="Organization.HolidayRegion"/>.</item>
/// <item>Company holidays are <see cref="Holiday"/> rows: org-wide ones for everyone, a group's for the days a member belonged
/// to it or a sub-group (<see cref="GroupMembershipPeriod"/>) — never by today's membership, so a move never rewrites the
/// past. A member an admin excluded (<see cref="DayOffExclusion"/>) does not get it.</item>
/// <item>Several on one day count once: among company holidays the first that applies (a one-off before a recurring one,
/// so a one-off half day overrides a recurring full one; then a group's before the organization's), and a public holiday
/// wins over a half company one.</item>
/// </list>
/// One query for an organization without group holidays or group regions, two otherwise.
/// </summary>
public sealed class HolidayCalendar
{
    private readonly string? _orgRegion;
    private readonly List<(Holiday Holiday, HashSet<int> Excluded)> _holidays;
    private readonly Dictionary<int, (int? Parent, string? Region)> _groups;
    private readonly ILookup<int, (int GroupId, DateOnly From, DateOnly? Until)> _periods;
    private readonly Dictionary<(string Region, int Year), Dictionary<DateOnly, string>> _public = [];
    private readonly Dictionary<int, Dictionary<DateOnly, List<(Holiday Holiday, HashSet<int> Excluded)>>> _company = [];

    private HolidayCalendar(string? orgRegion, List<(Holiday, HashSet<int>)> holidays,
        Dictionary<int, (int?, string?)> groups, ILookup<int, (int, DateOnly, DateOnly?)> periods)
    {
        (_orgRegion, _holidays, _groups, _periods) = (orgRegion, holidays, groups, periods);
    }

    /// <summary>Loads the calendar of <paramref name="userIds"/> for the days <paramref name="from"/>..<paramref name="to"/>.</summary>
    public static Task<HolidayCalendar> LoadAsync(TimeTrackingDbContext db, Organization org,
        IReadOnlyCollection<int> userIds, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        LoadAsync(db, org.Id, org.HolidayRegion, org.GroupHolidayRegions, OrgClock.For(org), userIds, from, to, ct);

    public static async Task<HolidayCalendar> LoadAsync(TimeTrackingDbContext db, int orgId, string? orgRegion, bool groupRegions,
        OrgClock clock, IReadOnlyCollection<int> userIds, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        var rows = await db.Holidays.AsNoTracking()
            .Where(h => h.OrganizationId == orgId && (h.IsRecurring || (h.Date >= from && h.Date <= to)))
            .OrderBy(h => h.IsRecurring).ThenBy(h => h.GroupId == null).ThenBy(h => h.Id)
            .Select(h => new { Holiday = h, Excluded = h.Exclusions.Where(x => ids.Contains(x.UserId)).Select(x => x.UserId).ToList() })
            .ToListAsync(ct);
        var holidays = rows.Select(r => (r.Holiday, r.Excluded.ToHashSet())).ToList();

        var groups = new Dictionary<int, (int?, string?)>();
        var periods = new List<(int UserId, int GroupId, DateOnly From, DateOnly? Until)>();
        if (groupRegions || holidays.Any(h => h.Holiday.GroupId != null))
        {
            var fromUtc = clock.StartOfDayUtc(from);
            var toUtc = clock.StartOfDayUtc(to.AddDays(1));
            // Every group of the organization (deleted ones too: a past membership still counts), with the members'
            // periods that overlap the range
            var loaded = await db.Groups.IgnoreQueryFilters().AsNoTracking()
                .Where(g => g.OrganizationId == orgId)
                .Select(g => new
                {
                    g.Id, g.ParentGroupId, g.HolidayRegion,
                    Periods = db.GroupMembershipPeriods.IgnoreQueryFilters()
                        .Where(p => p.GroupId == g.Id && ids.Contains(p.UserId) && p.From < toUtc && (p.Until == null || p.Until > fromUtc))
                        .Select(p => new { p.UserId, p.From, p.Until }).ToList()
                })
                .ToListAsync(ct);
            foreach (var g in loaded)
            {
                groups[g.Id] = (g.ParentGroupId, g.HolidayRegion);
                // The day someone joined counts, the day they left does not
                periods.AddRange(g.Periods.Select(p => (p.UserId, g.Id, clock.LocalDate(p.From),
                    p.Until is { } u ? clock.LocalDate(u) : (DateOnly?)null)));
            }
        }
        return new HolidayCalendar(orgRegion, holidays, groups,
            periods.ToLookup(p => p.UserId, p => (p.GroupId, p.From, p.Until)));
    }

    /// <summary>The member's holiday on <paramref name="date"/>, or null. <paramref name="schedules"/>: the member's work schedules.</summary>
    public DayOff? On(int userId, DateOnly date, IReadOnlyList<WorkSchedule> schedules)
    {
        DayOff? best = null;
        if (!_company.TryGetValue(date.Year, out var byDate)) byDate = IndexCompany(date.Year);
        if (byDate.TryGetValue(date, out var company))
            // Among company holidays the first that applies decides (as loaded: one-offs before recurring ones, a group's
            // before the organization's): a one-off half day overrides a recurring full one that year
            foreach (var (holiday, excluded) in company)
            {
                if (excluded.Contains(userId)) continue;
                if (holiday.GroupId is int groupId && !BelongedTo(userId, groupId, date)) continue;
                best = new DayOff(holiday.Name, holiday.IsHalfDay, DayOffSource.Company);
                break;
            }
        if (best is { IsHalfDay: false }) return best;

        if (RegionOn(userId, date, schedules) is { } region && Public(region, date.Year).TryGetValue(date, out var name))
            best = Better(best, new DayOff(name, false, DayOffSource.Public));
        return best;
    }

    /// <summary>The member's holidays from <paramref name="from"/> to <paramref name="to"/>, by day.</summary>
    public Dictionary<DateOnly, DayOff> Range(int userId, DateOnly from, DateOnly to, IReadOnlyList<WorkSchedule> schedules)
    {
        var result = new Dictionary<DateOnly, DayOff>();
        for (var d = from; d <= to; d = d.AddDays(1))
            if (On(userId, d, schedules) is { } off) result[d] = off;
        return result;
    }

    /// <summary>
    /// The public holiday region on <paramref name="date"/>: the schedule's, else the groups' (when they agree), else the
    /// organization's.
    /// </summary>
    public string? RegionOn(int userId, DateOnly date, IReadOnlyList<WorkSchedule> schedules) =>
        ScheduleOn(schedules, date)?.HolidayRegion ?? GroupRegionOn(userId, date, out _) ?? _orgRegion;

    /// <summary>The region the member's groups give on <paramref name="date"/>; <paramref name="conflict"/> when they disagree.</summary>
    public string? GroupRegionOn(int userId, DateOnly date, out bool conflict)
    {
        conflict = false;
        string? region = null;
        foreach (var (groupId, from, until) in _periods[userId])
        {
            if (date < from || (until is { } u && date >= u)) continue;
            if (EffectiveRegion(groupId) is not { } r) continue;
            if (region != null && region != r) { conflict = true; return null; }
            region = r;
        }
        return region;
    }

    private string? EffectiveRegion(int groupId)
    {
        int? id = groupId;
        for (var depth = 0; id is int g && depth < 10; depth++)
        {
            if (!_groups.TryGetValue(g, out var group)) return null;
            if (group.Region != null) return group.Region;
            id = group.Parent;
        }
        return null;
    }

    /// <summary>The member was in <paramref name="groupId"/> or one of its sub-groups on that day.</summary>
    private bool BelongedTo(int userId, int groupId, DateOnly date)
    {
        foreach (var (memberGroup, from, until) in _periods[userId])
        {
            if (date < from || (until is { } u && date >= u)) continue;
            int? id = memberGroup;
            for (var depth = 0; id is int g && depth < 10; depth++)
            {
                if (g == groupId) return true;
                id = _groups.TryGetValue(g, out var group) ? group.Parent : null;
            }
        }
        return false;
    }

    private static DayOff Better(DayOff? current, DayOff candidate) =>
        current == null || (current.IsHalfDay && !candidate.IsHalfDay) ? candidate : current;

    private Dictionary<DateOnly, List<(Holiday, HashSet<int>)>> IndexCompany(int year)
    {
        var byDate = new Dictionary<DateOnly, List<(Holiday, HashSet<int>)>>();
        foreach (var entry in _holidays)
            if (entry.Holiday.DateIn(year) is { } d)
            {
                if (!byDate.TryGetValue(d, out var list)) byDate[d] = list = [];
                list.Add(entry);
            }
        return _company[year] = byDate;
    }

    private Dictionary<DateOnly, string> Public(string region, int year)
    {
        if (_public.TryGetValue((region, year), out var days)) return days;
        days = new Dictionary<DateOnly, string>();
        foreach (var (date, name, _) in HolidayPresets.For(region, year) ?? [])
            days.TryAdd(date, name);
        return _public[(region, year)] = days;
    }

    private static WorkSchedule? ScheduleOn(IReadOnlyList<WorkSchedule> schedules, DateOnly date)
    {
        WorkSchedule? schedule = null;
        foreach (var ws in schedules)
            if (ws.ValidFrom <= date && (ws.ValidTo == null || ws.ValidTo >= date)
                && (schedule == null || ws.ValidFrom > schedule.ValidFrom))
                schedule = ws;
        return schedule;
    }
}
