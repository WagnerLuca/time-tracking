using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public class HolidayService : IHolidayService
{
    private readonly TimeTrackingDbContext _context;
    private readonly ILogger<HolidayService> _logger;

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

        var isMember = await _context.UserOrganizations
            .AnyAsync(uo => uo.OrganizationId == org.Id && uo.UserId == callerUserId);
        if (!isMember)
            return ServiceResult.Forbidden<List<HolidayResponse>>();

        var holidays = await _context.Holidays
            .Where(h => h.OrganizationId == org.Id)
            .OrderBy(h => h.Date)
            .Select(h => new HolidayResponse
            {
                Id = h.Id,
                OrganizationId = h.OrganizationId,
                Date = h.Date,
                Name = h.Name,
                IsRecurring = h.IsRecurring,
                IsHalfDay = h.IsHalfDay
            })
            .ToListAsync();

        return ServiceResult.Ok(holidays);
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

        var exists = await _context.Holidays
            .AnyAsync(h => h.OrganizationId == org.Id && h.Date == request.Date);
        if (exists)
            return ServiceResult.Conflict<HolidayResponse>("A holiday already exists on this date.");

        var holiday = new Holiday
        {
            OrganizationId = org.Id,
            Date = request.Date,
            Name = request.Name,
            IsRecurring = request.IsRecurring,
            IsHalfDay = request.IsHalfDay
        };

        _context.Holidays.Add(holiday);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Holiday {HolidayId} created in org {OrgSlug} by user {UserId}", holiday.Id, slug, callerUserId);

        return ServiceResult.Ok(MapToResponse(holiday));
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

        var holiday = await _context.Holidays
            .FirstOrDefaultAsync(h => h.Id == holidayId && h.OrganizationId == org.Id);
        if (holiday == null)
            return ServiceResult.NotFound<HolidayResponse>("Holiday not found");

        if (request.Date.HasValue)       holiday.Date = request.Date.Value;
        if (request.Name != null)        holiday.Name = request.Name;
        if (request.IsRecurring.HasValue) holiday.IsRecurring = request.IsRecurring.Value;
        if (request.IsHalfDay.HasValue)  holiday.IsHalfDay = request.IsHalfDay.Value;

        await _context.SaveChangesAsync();
        return ServiceResult.Ok(MapToResponse(holiday));
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

    public async Task<ServiceResult<List<HolidayResponse>>> ImportPresetHolidaysAsync(
        string slug, int callerUserId, string preset, int? year, int? toYear = null)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<List<HolidayResponse>>("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<List<HolidayResponse>>();

        var targetYear = year ?? DateTime.UtcNow.Year;
        var lastYear = toYear ?? targetYear;
        if (targetYear < 2000 || lastYear > 2100 || lastYear < targetYear || lastYear - targetYear > 10)
            return ServiceResult.BadRequest<List<HolidayResponse>>("Choose years between 2000 and 2100, at most 10 years apart.");
        var presetHolidays = HolidayPresets.Get(preset, targetYear, lastYear);
        if (presetHolidays == null)
            return ServiceResult.BadRequest<List<HolidayResponse>>(
                $"Unknown preset: {preset}. Available: {string.Join(", ", HolidayPresets.All().Select(p => p.Key))}");

        var existingDates = await _context.Holidays
            .Where(h => h.OrganizationId == org.Id)
            .Select(h => h.Date)
            .ToListAsync();

        var added = new List<HolidayResponse>();
        foreach (var (date, name, isRecurring) in presetHolidays)
        {
            if (existingDates.Contains(date)) continue;

            var holiday = new Holiday
            {
                OrganizationId = org.Id,
                Date = date,
                Name = name,
                IsRecurring = isRecurring
            };
            _context.Holidays.Add(holiday);
            added.Add(new HolidayResponse
            {
                Id = 0,
                OrganizationId = org.Id,
                Date = date,
                Name = name,
                IsRecurring = isRecurring,
                IsHalfDay = false
            });
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("{Count} holidays imported from preset {Preset} for org {OrgSlug} by user {UserId}", added.Count, preset, slug, callerUserId);
        return ServiceResult.Ok(added);
    }

    public List<HolidayPresetResponse> GetAvailablePresets() => HolidayPresets.All();

    // ────────────────────────────────────────────────────
    //  Private helpers
    // ────────────────────────────────────────────────────

    private static HolidayResponse MapToResponse(Holiday h) => new()
    {
        Id = h.Id,
        OrganizationId = h.OrganizationId,
        Date = h.Date,
        Name = h.Name,
        IsRecurring = h.IsRecurring,
        IsHalfDay = h.IsHalfDay
    };
}
