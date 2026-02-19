using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Controllers;

[ApiController]
[Route("api/organizations")]
public class AbsenceDayController : OrganizationBaseController
{
    public AbsenceDayController(TimeTrackingDbContext context) : base(context) { }

    // ────────────────────────────────────────────────────
    //  GET  /api/organizations/{slug}/absences
    //  Get absences — own if member, all if admin
    // ────────────────────────────────────────────────────
    [HttpGet("{slug}/absences")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<AbsenceDayResponse>>> GetAbsences(
        string slug, [FromQuery] int? userId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null) return Forbid();

        var query = _context.AbsenceDays
            .Include(a => a.User)
            .Where(a => a.OrganizationId == org.Id);

        // Non-admins can only see their own
        if (callerRole < OrganizationRole.Admin)
        {
            query = query.Where(a => a.UserId == callerId.Value);
        }
        else if (userId.HasValue)
        {
            query = query.Where(a => a.UserId == userId.Value);
        }

        if (from.HasValue) query = query.Where(a => a.Date >= from.Value);
        if (to.HasValue) query = query.Where(a => a.Date <= to.Value);

        var absences = await query
            .OrderBy(a => a.Date)
            .Select(a => new AbsenceDayResponse
            {
                Id = a.Id,
                UserId = a.UserId,
                OrganizationId = a.OrganizationId,
                Date = a.Date,
                Type = a.Type.ToString(),
                Note = a.Note,
                UserFirstName = a.User.FirstName,
                UserLastName = a.User.LastName
            })
            .ToListAsync();

        return Ok(absences);
    }

    // ────────────────────────────────────────────────────
    //  POST  /api/organizations/{slug}/absences
    //  Member: create own absence day
    // ────────────────────────────────────────────────────
    [HttpPost("{slug}/absences")]
    [Authorize]
    public async Task<ActionResult<AbsenceDayResponse>> CreateAbsence(string slug, [FromBody] CreateAbsenceDayRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId.Value && uo.IsActive);
        if (membership == null) return Forbid();

        // Check duplicate
        var exists = await _context.AbsenceDays
            .AnyAsync(a => a.UserId == userId.Value && a.OrganizationId == org.Id && a.Date == request.Date);
        if (exists) return Conflict(new { message = "An absence already exists on this date." });

        var absence = new AbsenceDay
        {
            UserId = userId.Value,
            OrganizationId = org.Id,
            Date = request.Date,
            Type = request.Type,
            Note = request.Note
        };

        _context.AbsenceDays.Add(absence);
        await _context.SaveChangesAsync();

        var user = await _context.Users.FindAsync(userId.Value);

        return Ok(new AbsenceDayResponse
        {
            Id = absence.Id,
            UserId = absence.UserId,
            OrganizationId = absence.OrganizationId,
            Date = absence.Date,
            Type = absence.Type.ToString(),
            Note = absence.Note,
            UserFirstName = user?.FirstName,
            UserLastName = user?.LastName
        });
    }

    // ────────────────────────────────────────────────────
    //  POST  /api/organizations/{slug}/absences/admin
    //  Admin+: create absence for any member
    // ────────────────────────────────────────────────────
    [HttpPost("{slug}/absences/admin")]
    [Authorize]
    public async Task<ActionResult<AbsenceDayResponse>> AdminCreateAbsence(string slug, [FromBody] AdminCreateAbsenceDayRequest request)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        // Verify target user is a member
        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == request.UserId && uo.IsActive);
        if (membership == null) return NotFound(new { message = "User is not a member of this organization." });

        var exists = await _context.AbsenceDays
            .AnyAsync(a => a.UserId == request.UserId && a.OrganizationId == org.Id && a.Date == request.Date);
        if (exists) return Conflict(new { message = "An absence already exists on this date for this user." });

        var absence = new AbsenceDay
        {
            UserId = request.UserId,
            OrganizationId = org.Id,
            Date = request.Date,
            Type = request.Type,
            Note = request.Note
        };

        _context.AbsenceDays.Add(absence);
        await _context.SaveChangesAsync();

        var user = await _context.Users.FindAsync(request.UserId);

        return Ok(new AbsenceDayResponse
        {
            Id = absence.Id,
            UserId = absence.UserId,
            OrganizationId = absence.OrganizationId,
            Date = absence.Date,
            Type = absence.Type.ToString(),
            Note = absence.Note,
            UserFirstName = user?.FirstName,
            UserLastName = user?.LastName
        });
    }

    // ────────────────────────────────────────────────────
    //  DELETE  /api/organizations/{slug}/absences/{id}
    //  Delete an absence (own or admin)
    // ────────────────────────────────────────────────────
    [HttpDelete("{slug}/absences/{id}")]
    [Authorize]
    public async Task<ActionResult> DeleteAbsence(string slug, int id)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null) return Forbid();

        var absence = await _context.AbsenceDays
            .FirstOrDefaultAsync(a => a.Id == id && a.OrganizationId == org.Id);
        if (absence == null) return NotFound(new { message = "Absence not found" });

        // Members can only delete their own
        if (callerRole < OrganizationRole.Admin && absence.UserId != callerId.Value)
            return Forbid();

        _context.AbsenceDays.Remove(absence);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
