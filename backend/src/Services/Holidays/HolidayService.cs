using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public class HolidayService : IHolidayService
{
    private readonly TimeTrackingDbContext _context;

    public HolidayService(TimeTrackingDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceResult<List<HolidayResponse>>> GetHolidaysAsync(string slug, int callerUserId)
    {
        var org = await GetOrgBySlugAsync(slug);
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
                IsRecurring = h.IsRecurring
            })
            .ToListAsync();

        return ServiceResult.Ok(holidays);
    }

    public async Task<ServiceResult<HolidayResponse>> CreateHolidayAsync(
        string slug, int callerUserId, CreateHolidayRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<HolidayResponse>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
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
            IsRecurring = request.IsRecurring
        };

        _context.Holidays.Add(holiday);
        await _context.SaveChangesAsync();

        return ServiceResult.Ok(MapToResponse(holiday));
    }

    public async Task<ServiceResult<HolidayResponse>> UpdateHolidayAsync(
        string slug, int callerUserId, int holidayId, UpdateHolidayRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<HolidayResponse>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<HolidayResponse>();

        var holiday = await _context.Holidays
            .FirstOrDefaultAsync(h => h.Id == holidayId && h.OrganizationId == org.Id);
        if (holiday == null)
            return ServiceResult.NotFound<HolidayResponse>("Holiday not found");

        if (request.Date.HasValue)       holiday.Date = request.Date.Value;
        if (request.Name != null)        holiday.Name = request.Name;
        if (request.IsRecurring.HasValue) holiday.IsRecurring = request.IsRecurring.Value;

        await _context.SaveChangesAsync();
        return ServiceResult.Ok(MapToResponse(holiday));
    }

    public async Task<ServiceResult> DeleteHolidayAsync(string slug, int callerUserId, int holidayId)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden();

        var holiday = await _context.Holidays
            .FirstOrDefaultAsync(h => h.Id == holidayId && h.OrganizationId == org.Id);
        if (holiday == null)
            return ServiceResult.NotFound("Holiday not found");

        _context.Holidays.Remove(holiday);
        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<List<HolidayResponse>>> ImportPresetHolidaysAsync(
        string slug, int callerUserId, string preset, int? year)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<List<HolidayResponse>>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<List<HolidayResponse>>();

        var targetYear = year ?? DateTime.UtcNow.Year;
        var presetHolidays = GetPresetHolidays(preset, targetYear);
        if (presetHolidays == null)
            return ServiceResult.BadRequest<List<HolidayResponse>>(
                $"Unknown preset: {preset}. Available: de, at, ch");

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
                IsRecurring = isRecurring
            });
        }

        await _context.SaveChangesAsync();
        return ServiceResult.Ok(added);
    }

    public List<object> GetAvailablePresets()
    {
        return new List<object>
        {
            new { Key = "de", Name = "Germany" },
            new { Key = "at", Name = "Austria" },
            new { Key = "ch", Name = "Switzerland" }
        };
    }

    // ────────────────────────────────────────────────────
    //  Private helpers
    // ────────────────────────────────────────────────────

    private static HolidayResponse MapToResponse(Holiday h) => new()
    {
        Id = h.Id,
        OrganizationId = h.OrganizationId,
        Date = h.Date,
        Name = h.Name,
        IsRecurring = h.IsRecurring
    };

    private async Task<Organization?> GetOrgBySlugAsync(string slug)
    {
        return await _context.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Slug == slug);
    }

    private async Task<OrganizationRole?> GetRoleAsync(int userId, int organizationId)
    {
        var membership = await _context.UserOrganizations
            .AsNoTracking()
            .FirstOrDefaultAsync(uo => uo.OrganizationId == organizationId
                                    && uo.UserId == userId);
        return membership?.Role;
    }

    // ────────────────────────────────────────────────────
    //  Preset holiday data
    // ────────────────────────────────────────────────────

    private static List<(DateOnly Date, string Name, bool IsRecurring)>? GetPresetHolidays(string preset, int year)
    {
        return preset.ToLowerInvariant() switch
        {
            "de" => GetGermanHolidays(year),
            "at" => GetAustrianHolidays(year),
            "ch" => GetSwissHolidays(year),
            _ => null
        };
    }

    private static DateOnly Easter(int year)
    {
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;
        int day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }

    private static List<(DateOnly, string, bool)> GetGermanHolidays(int year)
    {
        var easter = Easter(year);
        return new List<(DateOnly, string, bool)>
        {
            (new DateOnly(year, 1, 1),  "Neujahr", true),
            (easter.AddDays(-2),        "Karfreitag", false),
            (easter.AddDays(1),         "Ostermontag", false),
            (new DateOnly(year, 5, 1),  "Tag der Arbeit", true),
            (easter.AddDays(39),        "Christi Himmelfahrt", false),
            (easter.AddDays(50),        "Pfingstmontag", false),
            (new DateOnly(year, 10, 3), "Tag der Deutschen Einheit", true),
            (new DateOnly(year, 12, 25),"1. Weihnachtstag", true),
            (new DateOnly(year, 12, 26),"2. Weihnachtstag", true)
        };
    }

    private static List<(DateOnly, string, bool)> GetAustrianHolidays(int year)
    {
        var easter = Easter(year);
        return new List<(DateOnly, string, bool)>
        {
            (new DateOnly(year, 1, 1),  "Neujahr", true),
            (new DateOnly(year, 1, 6),  "Heilige Drei Könige", true),
            (easter.AddDays(1),         "Ostermontag", false),
            (new DateOnly(year, 5, 1),  "Staatsfeiertag", true),
            (easter.AddDays(39),        "Christi Himmelfahrt", false),
            (easter.AddDays(50),        "Pfingstmontag", false),
            (easter.AddDays(60),        "Fronleichnam", false),
            (new DateOnly(year, 8, 15), "Mariä Himmelfahrt", true),
            (new DateOnly(year, 10, 26),"Nationalfeiertag", true),
            (new DateOnly(year, 11, 1), "Allerheiligen", true),
            (new DateOnly(year, 12, 8), "Mariä Empfängnis", true),
            (new DateOnly(year, 12, 25),"Christtag", true),
            (new DateOnly(year, 12, 26),"Stefanitag", true)
        };
    }

    private static List<(DateOnly, string, bool)> GetSwissHolidays(int year)
    {
        var easter = Easter(year);
        return new List<(DateOnly, string, bool)>
        {
            (new DateOnly(year, 1, 1),  "Neujahr", true),
            (new DateOnly(year, 1, 2),  "Berchtoldstag", true),
            (easter.AddDays(-2),        "Karfreitag", false),
            (easter.AddDays(1),         "Ostermontag", false),
            (new DateOnly(year, 5, 1),  "Tag der Arbeit", true),
            (easter.AddDays(39),        "Auffahrt", false),
            (easter.AddDays(50),        "Pfingstmontag", false),
            (new DateOnly(year, 8, 1),  "Bundesfeier", true),
            (new DateOnly(year, 12, 25),"Weihnachten", true),
            (new DateOnly(year, 12, 26),"Stephanstag", true)
        };
    }
}
