using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public class AbsenceDayService : IAbsenceDayService
{
    private readonly TimeTrackingDbContext _context;

    public AbsenceDayService(TimeTrackingDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceResult<List<AbsenceDayResponse>>> GetAbsencesAsync(
        string slug, int callerUserId, int? filterUserId, DateOnly? from, DateOnly? to)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<List<AbsenceDayResponse>>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null)
            return ServiceResult.Forbidden<List<AbsenceDayResponse>>();

        var query = _context.AbsenceDays
            .Include(a => a.User)
            .Where(a => a.OrganizationId == org.Id);

        // Non-admins can only see their own
        if (callerRole < OrganizationRole.Admin)
            query = query.Where(a => a.UserId == callerUserId);
        else if (filterUserId.HasValue)
            query = query.Where(a => a.UserId == filterUserId.Value);

        if (from.HasValue) query = query.Where(a => a.Date >= from.Value);
        if (to.HasValue)   query = query.Where(a => a.Date <= to.Value);

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

        return ServiceResult.Ok(absences);
    }

    public async Task<ServiceResult<AbsenceDayResponse>> CreateAbsenceAsync(
        string slug, int userId, CreateAbsenceDayRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<AbsenceDayResponse>("Organization not found");

        var isMember = await _context.UserOrganizations
            .AnyAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId && uo.IsActive);
        if (!isMember)
            return ServiceResult.Forbidden<AbsenceDayResponse>();

        var exists = await _context.AbsenceDays
            .AnyAsync(a => a.UserId == userId && a.OrganizationId == org.Id && a.Date == request.Date);
        if (exists)
            return ServiceResult.Conflict<AbsenceDayResponse>("An absence already exists on this date.");

        var absence = new AbsenceDay
        {
            UserId = userId,
            OrganizationId = org.Id,
            Date = request.Date,
            Type = request.Type,
            Note = request.Note
        };

        _context.AbsenceDays.Add(absence);
        await _context.SaveChangesAsync();

        var user = await _context.Users.FindAsync(userId);
        return ServiceResult.Ok(MapToResponse(absence, user));
    }

    public async Task<ServiceResult<AbsenceDayResponse>> AdminCreateAbsenceAsync(
        string slug, int callerUserId, AdminCreateAbsenceDayRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<AbsenceDayResponse>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<AbsenceDayResponse>();

        var isMember = await _context.UserOrganizations
            .AnyAsync(uo => uo.OrganizationId == org.Id && uo.UserId == request.UserId && uo.IsActive);
        if (!isMember)
            return ServiceResult.NotFound<AbsenceDayResponse>("User is not a member of this organization.");

        var exists = await _context.AbsenceDays
            .AnyAsync(a => a.UserId == request.UserId && a.OrganizationId == org.Id && a.Date == request.Date);
        if (exists)
            return ServiceResult.Conflict<AbsenceDayResponse>("An absence already exists on this date for this user.");

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
        return ServiceResult.Ok(MapToResponse(absence, user));
    }

    public async Task<ServiceResult> DeleteAbsenceAsync(string slug, int callerUserId, int absenceId)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null)
            return ServiceResult.Forbidden();

        var absence = await _context.AbsenceDays
            .FirstOrDefaultAsync(a => a.Id == absenceId && a.OrganizationId == org.Id);
        if (absence == null)
            return ServiceResult.NotFound("Absence not found");

        // Members can only delete their own
        if (callerRole < OrganizationRole.Admin && absence.UserId != callerUserId)
            return ServiceResult.Forbidden();

        _context.AbsenceDays.Remove(absence);
        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    // ────────────────────────────────────────────────────
    //  Private helpers
    // ────────────────────────────────────────────────────

    private static AbsenceDayResponse MapToResponse(AbsenceDay absence, User? user) => new()
    {
        Id = absence.Id,
        UserId = absence.UserId,
        OrganizationId = absence.OrganizationId,
        Date = absence.Date,
        Type = absence.Type.ToString(),
        Note = absence.Note,
        UserFirstName = user?.FirstName,
        UserLastName = user?.LastName
    };

    private async Task<Organization?> GetOrgBySlugAsync(string slug)
    {
        return await _context.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Slug == slug && o.IsActive);
    }

    private async Task<OrganizationRole?> GetRoleAsync(int userId, int organizationId)
    {
        var membership = await _context.UserOrganizations
            .AsNoTracking()
            .FirstOrDefaultAsync(uo => uo.OrganizationId == organizationId
                                    && uo.UserId == userId
                                    && uo.IsActive);
        return membership?.Role;
    }
}
