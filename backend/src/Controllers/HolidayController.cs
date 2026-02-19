using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Controllers;

[ApiController]
[Route("api/organizations")]
public class HolidayController : OrganizationBaseController
{
    public HolidayController(TimeTrackingDbContext context) : base(context) { }

    // ────────────────────────────────────────────────────
    //  GET  /api/organizations/{slug}/holidays
    //  Get all holidays for this organization
    // ────────────────────────────────────────────────────
    [HttpGet("{slug}/holidays")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<HolidayResponse>>> GetHolidays(string slug)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        // Must be a member
        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId.Value && uo.IsActive);
        if (membership == null) return Forbid();

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

        return Ok(holidays);
    }

    // ────────────────────────────────────────────────────
    //  POST  /api/organizations/{slug}/holidays
    //  Admin+: Create a holiday
    // ────────────────────────────────────────────────────
    [HttpPost("{slug}/holidays")]
    [Authorize]
    public async Task<ActionResult<HolidayResponse>> CreateHoliday(string slug, [FromBody] CreateHolidayRequest request)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        // Check duplicate
        var exists = await _context.Holidays
            .AnyAsync(h => h.OrganizationId == org.Id && h.Date == request.Date);
        if (exists) return Conflict(new { message = "A holiday already exists on this date." });

        var holiday = new Holiday
        {
            OrganizationId = org.Id,
            Date = request.Date,
            Name = request.Name,
            IsRecurring = request.IsRecurring
        };

        _context.Holidays.Add(holiday);
        await _context.SaveChangesAsync();

        return Ok(new HolidayResponse
        {
            Id = holiday.Id,
            OrganizationId = holiday.OrganizationId,
            Date = holiday.Date,
            Name = holiday.Name,
            IsRecurring = holiday.IsRecurring
        });
    }

    // ────────────────────────────────────────────────────
    //  PUT  /api/organizations/{slug}/holidays/{id}
    //  Admin+: Update a holiday
    // ────────────────────────────────────────────────────
    [HttpPut("{slug}/holidays/{id}")]
    [Authorize]
    public async Task<ActionResult<HolidayResponse>> UpdateHoliday(string slug, int id, [FromBody] UpdateHolidayRequest request)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        var holiday = await _context.Holidays
            .FirstOrDefaultAsync(h => h.Id == id && h.OrganizationId == org.Id);
        if (holiday == null) return NotFound(new { message = "Holiday not found" });

        if (request.Date.HasValue) holiday.Date = request.Date.Value;
        if (request.Name != null) holiday.Name = request.Name;
        if (request.IsRecurring.HasValue) holiday.IsRecurring = request.IsRecurring.Value;

        await _context.SaveChangesAsync();

        return Ok(new HolidayResponse
        {
            Id = holiday.Id,
            OrganizationId = holiday.OrganizationId,
            Date = holiday.Date,
            Name = holiday.Name,
            IsRecurring = holiday.IsRecurring
        });
    }

    // ────────────────────────────────────────────────────
    //  DELETE  /api/organizations/{slug}/holidays/{id}
    //  Admin+: Delete a holiday
    // ────────────────────────────────────────────────────
    [HttpDelete("{slug}/holidays/{id}")]
    [Authorize]
    public async Task<ActionResult> DeleteHoliday(string slug, int id)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        var holiday = await _context.Holidays
            .FirstOrDefaultAsync(h => h.Id == id && h.OrganizationId == org.Id);
        if (holiday == null) return NotFound(new { message = "Holiday not found" });

        _context.Holidays.Remove(holiday);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // ────────────────────────────────────────────────────
    //  POST  /api/organizations/{slug}/holidays/import-preset
    //  Admin+: Import a predefined list of holidays for a given year
    // ────────────────────────────────────────────────────
    [HttpPost("{slug}/holidays/import-preset")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<HolidayResponse>>> ImportPresetHolidays(
        string slug, [FromQuery] string preset = "de", [FromQuery] int? year = null)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        var targetYear = year ?? DateTime.UtcNow.Year;
        var presetHolidays = GetPresetHolidays(preset, targetYear);
        if (presetHolidays == null)
            return BadRequest(new { message = $"Unknown preset: {preset}. Available: de, at, ch" });

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
                Id = 0, // will be set after save
                OrganizationId = org.Id,
                Date = date,
                Name = name,
                IsRecurring = isRecurring
            });
        }

        await _context.SaveChangesAsync();
        return Ok(added);
    }

    // ────────────────────────────────────────────────────
    //  GET  /api/organizations/holiday-presets
    //  Get available preset lists
    // ────────────────────────────────────────────────────
    [HttpGet("/api/organizations/holiday-presets")]
    [Authorize]
    public ActionResult GetAvailablePresets()
    {
        return Ok(new[]
        {
            new { Key = "de", Name = "Germany" },
            new { Key = "at", Name = "Austria" },
            new { Key = "ch", Name = "Switzerland" }
        });
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
        // Butcher's algorithm for computing Easter Sunday
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
            (new DateOnly(year, 1, 1), "Neujahr", true),
            (easter.AddDays(-2), "Karfreitag", false),
            (easter.AddDays(1), "Ostermontag", false),
            (new DateOnly(year, 5, 1), "Tag der Arbeit", true),
            (easter.AddDays(39), "Christi Himmelfahrt", false),
            (easter.AddDays(50), "Pfingstmontag", false),
            (new DateOnly(year, 10, 3), "Tag der Deutschen Einheit", true),
            (new DateOnly(year, 12, 25), "1. Weihnachtstag", true),
            (new DateOnly(year, 12, 26), "2. Weihnachtstag", true)
        };
    }

    private static List<(DateOnly, string, bool)> GetAustrianHolidays(int year)
    {
        var easter = Easter(year);
        return new List<(DateOnly, string, bool)>
        {
            (new DateOnly(year, 1, 1), "Neujahr", true),
            (new DateOnly(year, 1, 6), "Heilige Drei Könige", true),
            (easter.AddDays(1), "Ostermontag", false),
            (new DateOnly(year, 5, 1), "Staatsfeiertag", true),
            (easter.AddDays(39), "Christi Himmelfahrt", false),
            (easter.AddDays(50), "Pfingstmontag", false),
            (easter.AddDays(60), "Fronleichnam", false),
            (new DateOnly(year, 8, 15), "Mariä Himmelfahrt", true),
            (new DateOnly(year, 10, 26), "Nationalfeiertag", true),
            (new DateOnly(year, 11, 1), "Allerheiligen", true),
            (new DateOnly(year, 12, 8), "Mariä Empfängnis", true),
            (new DateOnly(year, 12, 25), "Christtag", true),
            (new DateOnly(year, 12, 26), "Stefanitag", true)
        };
    }

    private static List<(DateOnly, string, bool)> GetSwissHolidays(int year)
    {
        var easter = Easter(year);
        return new List<(DateOnly, string, bool)>
        {
            (new DateOnly(year, 1, 1), "Neujahr", true),
            (new DateOnly(year, 1, 2), "Berchtoldstag", true),
            (easter.AddDays(-2), "Karfreitag", false),
            (easter.AddDays(1), "Ostermontag", false),
            (new DateOnly(year, 5, 1), "Tag der Arbeit", true),
            (easter.AddDays(39), "Auffahrt", false),
            (easter.AddDays(50), "Pfingstmontag", false),
            (new DateOnly(year, 8, 1), "Bundesfeier", true),
            (new DateOnly(year, 12, 25), "Weihnachten", true),
            (new DateOnly(year, 12, 26), "Stephanstag", true)
        };
    }
}
