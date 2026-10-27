using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public class HolidayService : IHolidayService
{
    private readonly TimeTrackingDbContext _context;
    private readonly ILogger<HolidayService> _logger;

    public const string GroupNotFoundMessage = "Group not found.";
    public const string HolidayExistsMessage = "A holiday already exists on this date.";
    public const string NotMembersMessage = "Only members of the organization can be left out.";
    public const string NoRegionMessage = "The organization has no holiday region.";

    public HolidayService(TimeTrackingDbContext context, ILogger<HolidayService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ServiceResult<List<HolidayResponse>>> GetHolidaysAsync(string slug, int callerUserId)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<List<HolidayResponse>>("Organization not found");

        var role = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (role == null)
            return ServiceResult.Forbidden<List<HolidayResponse>>();
        var isAdmin = role >= OrganizationRole.Admin;

        var holidays = await _context.Holidays.AsNoTracking()
            .Where(h => h.OrganizationId == org.Id)
            .OrderBy(h => h.Date)
            .Select(h => new { Holiday = h, Excluded = h.Exclusions.Select(x => x.UserId).ToList() })
            .ToListAsync();

        return ServiceResult.Ok(holidays.Select(h => MapToResponse(h.Holiday, isAdmin ? h.Excluded : null)).ToList());
    }

    public async Task<ServiceResult<HolidayResponse>> CreateHolidayAsync(
        string slug, int callerUserId, CreateHolidayRequest request)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<HolidayResponse>("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<HolidayResponse>();

        if (request.GroupId is int groupId && !await GroupExistsAsync(org.Id, groupId))
            return ServiceResult.NotFound<HolidayResponse>(GroupNotFoundMessage);
        if (await _context.Holidays.AnyAsync(h => h.OrganizationId == org.Id && h.GroupId == request.GroupId && h.Date == request.Date))
            return ServiceResult.Conflict<HolidayResponse>(HolidayExistsMessage);

        var holiday = new Holiday
        {
            OrganizationId = org.Id,
            Date = request.Date,
            Name = request.Name,
            IsRecurring = request.IsRecurring,
            IsHalfDay = request.IsHalfDay,
            GroupId = request.GroupId
        };

        _context.Holidays.Add(holiday);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Holiday {HolidayId} created in org {OrgSlug} by user {UserId}", holiday.Id, slug, callerUserId);

        return ServiceResult.Ok(MapToResponse(holiday, []));
    }

    public async Task<ServiceResult<HolidayResponse>> UpdateHolidayAsync(
        string slug, int callerUserId, int holidayId, UpdateHolidayRequest request)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<HolidayResponse>("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<HolidayResponse>();

        var holiday = await _context.Holidays.Include(h => h.Exclusions)
            .FirstOrDefaultAsync(h => h.Id == holidayId && h.OrganizationId == org.Id);
        if (holiday == null)
            return ServiceResult.NotFound<HolidayResponse>("Holiday not found");

        if (request.GroupId is int groupId && !await GroupExistsAsync(org.Id, groupId))
            return ServiceResult.NotFound<HolidayResponse>(GroupNotFoundMessage);
        if (request.Date.HasValue)       holiday.Date = request.Date.Value;
        if (request.Name != null)        holiday.Name = request.Name;
        if (request.IsRecurring.HasValue) holiday.IsRecurring = request.IsRecurring.Value;
        if (request.IsHalfDay.HasValue)  holiday.IsHalfDay = request.IsHalfDay.Value;
        if (request.ForWholeOrganization) holiday.GroupId = null;
        else if (request.GroupId.HasValue) holiday.GroupId = request.GroupId;
        if (await _context.Holidays.AnyAsync(h => h.Id != holiday.Id && h.OrganizationId == org.Id && h.GroupId == holiday.GroupId && h.Date == holiday.Date))
            return ServiceResult.Conflict<HolidayResponse>(HolidayExistsMessage);

        await _context.SaveChangesAsync();
        return ServiceResult.Ok(MapToResponse(holiday, holiday.Exclusions.Select(x => x.UserId).ToList()));
    }

    public async Task<ServiceResult> DeleteHolidayAsync(string slug, int callerUserId, int holidayId)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden();

        var holiday = await _context.Holidays
            .FirstOrDefaultAsync(h => h.Id == holidayId && h.OrganizationId == org.Id);
        if (holiday == null)
            return ServiceResult.NotFound("Holiday not found");

        _context.Holidays.Remove(holiday);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Holiday {HolidayId} deleted from org {OrgSlug} by user {UserId}", holidayId, slug, callerUserId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<HolidayResponse>> SetExclusionsAsync(string slug, int callerUserId, int holidayId, SetExclusionsRequest request)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<HolidayResponse>("Organization not found");
        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<HolidayResponse>();

        var holiday = await _context.Holidays.Include(h => h.Exclusions)
            .FirstOrDefaultAsync(h => h.Id == holidayId && h.OrganizationId == org.Id);
        if (holiday == null)
            return ServiceResult.NotFound<HolidayResponse>("Holiday not found");

        if (await ReplaceExclusionsAsync(_context, org.Id, holiday.Exclusions, request.UserIds, x => x.HolidayId = holiday.Id) is { } error)
            return ServiceResult.BadRequest<HolidayResponse>(error);
        await _context.SaveChangesAsync();
        return ServiceResult.Ok(MapToResponse(holiday, holiday.Exclusions.Select(x => x.UserId).ToList()));
    }

    /// <summary>
    /// Makes <paramref name="current"/> the exclusions of exactly <paramref name="userIds"/> (members of the organization,
    /// active or former); <paramref name="target"/> points a new row at its holiday or Betriebsurlaub.
    /// </summary>
    internal static async Task<string?> ReplaceExclusionsAsync(TimeTrackingDbContext db, int orgId, ICollection<DayOffExclusion> current,
        IReadOnlyCollection<int> userIds, Action<DayOffExclusion> target)
    {
        var wanted = userIds.Distinct().ToList();
        var members = await db.UserOrganizations.IgnoreQueryFilters()
            .CountAsync(uo => uo.OrganizationId == orgId && wanted.Contains(uo.UserId));
        if (members != wanted.Count) return NotMembersMessage;

        foreach (var x in current.Where(x => !wanted.Contains(x.UserId)).ToList())
        {
            current.Remove(x);
            db.DayOffExclusions.Remove(x);
        }
        foreach (var userId in wanted.Where(id => current.All(x => x.UserId != id)))
        {
            var x = new DayOffExclusion { OrganizationId = orgId, UserId = userId };
            target(x);
            current.Add(x);
            db.DayOffExclusions.Add(x);
        }
        return null;
    }

    public async Task<ServiceResult<MemberDaysOffResponse>> GetMemberDaysOffAsync(
        string slug, int callerUserId, int? userId, DateOnly from, DateOnly to)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<MemberDaysOffResponse>("Organization not found");
        if (EntryTimes.ValidateRange(from, to) is { } rangeError)
            return ServiceResult.BadRequest<MemberDaysOffResponse>(rangeError);

        var memberId = userId ?? callerUserId;
        if (memberId != callerUserId)
        {
            // The holidays reveal the Arbeitsort (region): like the work schedule
            var visibility = await MemberVisibility.ForCallerAsync(_context, org, callerUserId);
            if (visibility.Check(memberId, MemberInfo.WorkSchedule) is { } denied)
                return denied == ServiceErrorType.NotFound
                    ? ServiceResult.NotFound<MemberDaysOffResponse>("Member not found in this organization.")
                    : ServiceResult.Forbidden<MemberDaysOffResponse>();
        }
        else if (await _context.GetOrgRoleAsync(callerUserId, org.Id) == null)
            return ServiceResult.Forbidden<MemberDaysOffResponse>();

        var schedules = await _context.WorkSchedules.AsNoTracking()
            .Where(ws => ws.UserId == memberId && ws.OrganizationId == org.Id)
            .ToListAsync();
        var today = OrgClock.For(org).Today();
        var calendar = await HolidayCalendar.LoadAsync(_context, org, [memberId],
            from < today ? from : today, to > today ? to : today);
        calendar.GroupRegionOn(memberId, today, out var conflict);
        return ServiceResult.Ok(new MemberDaysOffResponse
        {
            UserId = memberId,
            Region = calendar.RegionOn(memberId, today, schedules),
            RegionConflict = conflict,
            Days = calendar.Range(memberId, from, to, schedules)
                .OrderBy(d => d.Key)
                .Select(d => new DayOffResponse(d.Key, d.Value.Name, d.Value.IsHalfDay, d.Value.Source.ToString()))
                .ToList()
        });
    }

    public ServiceResult<List<PublicHolidayResponse>> GetPublicHolidays(string region, int year)
    {
        if (year is < 2000 or > 2100)
            return ServiceResult.BadRequest<List<PublicHolidayResponse>>("Choose years between 2000 and 2100, at most 10 years apart.");
        return HolidayPresets.For(region, year) is { } days
            ? ServiceResult.Ok(days.Select(d => new PublicHolidayResponse(d.Date, d.Name)).ToList())
            : ServiceResult.NotFound<List<PublicHolidayResponse>>(HolidayPresets.UnknownRegionMessage);
    }

    public async Task<ServiceResult<int>> RemoveCoveredHolidaysAsync(string slug, int callerUserId)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<int>("Organization not found");
        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<int>();
        if (org.HolidayRegion is not { } region)
            return ServiceResult.BadRequest<int>(NoRegionMessage);

        // Rows an earlier import copied from a preset: org-wide, full day, same date (every year of a recurring one) and name
        var rows = await _context.Holidays.Where(h => h.OrganizationId == org.Id && h.GroupId == null && !h.IsHalfDay).ToListAsync();
        bool Covered(Holiday h, int year) =>
            h.DateIn(year) is { } d && HolidayPresets.For(region, year)!.Any(p => p.Date == d && p.Name == h.Name);
        var thisYear = OrgClock.For(org).Today().Year;
        var covered = rows.Where(h => h.IsRecurring
            ? Enumerable.Range(Math.Min(h.Date.Year, thisYear), Math.Abs(thisYear - h.Date.Year) + 1).All(y => Covered(h, y))
            : Covered(h, h.Date.Year)).ToList();
        _context.Holidays.RemoveRange(covered);
        await _context.SaveChangesAsync();
        _logger.LogInformation("{Count} imported holidays now covered by region {Region} removed from org {OrgSlug}", covered.Count, region, slug);
        return ServiceResult.Ok(covered.Count);
    }

    public async Task<ServiceResult<List<HolidayResponse>>> ImportPresetHolidaysAsync(
        string slug, int callerUserId, string preset, int? year, int? toYear = null)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<List<HolidayResponse>>("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<List<HolidayResponse>>();

        var targetYear = year ?? OrgClock.For(org).Today().Year;
        var lastYear = toYear ?? targetYear;
        if (targetYear < 2000 || lastYear > 2100 || lastYear < targetYear || lastYear - targetYear > 10)
            return ServiceResult.BadRequest<List<HolidayResponse>>("Choose years between 2000 and 2100, at most 10 years apart.");
        var presetHolidays = HolidayPresets.Get(preset, targetYear, lastYear);
        if (presetHolidays == null)
            return ServiceResult.BadRequest<List<HolidayResponse>>(
                $"Unknown preset: {preset}. Available: {string.Join(", ", HolidayPresets.All().Select(p => p.Key))}");

        var existing = await _context.Holidays
            .Where(h => h.OrganizationId == org.Id && h.GroupId == null)
            .ToListAsync();

        var added = new List<HolidayResponse>();
        foreach (var (date, name, isRecurring) in presetHolidays)
        {
            // Already there: on that date, or as a recurring holiday on that month/day (an earlier import)
            if (existing.Any(h => h.DateIn(date.Year) == date)) continue;

            var holiday = new Holiday
            {
                OrganizationId = org.Id,
                Date = date,
                Name = name,
                IsRecurring = isRecurring
            };
            _context.Holidays.Add(holiday);
            existing.Add(holiday);
            added.Add(MapToResponse(holiday, []) with { Id = 0 });
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("{Count} holidays imported from preset {Preset} for org {OrgSlug} by user {UserId}", added.Count, preset, slug, callerUserId);
        return ServiceResult.Ok(added);
    }

    public List<HolidayPresetResponse> GetAvailablePresets() => HolidayPresets.All();

    // ────────────────────────────────────────────────────
    //  Private helpers
    // ────────────────────────────────────────────────────

    private Task<bool> GroupExistsAsync(int orgId, int groupId) =>
        _context.Groups.AnyAsync(g => g.Id == groupId && g.OrganizationId == orgId);

    private static HolidayResponse MapToResponse(Holiday h, List<int>? excluded) => new()
    {
        Id = h.Id,
        OrganizationId = h.OrganizationId,
        Date = h.Date,
        Name = h.Name,
        IsRecurring = h.IsRecurring,
        IsHalfDay = h.IsHalfDay,
        GroupId = h.GroupId,
        ExcludedUserIds = excluded
    };
}
