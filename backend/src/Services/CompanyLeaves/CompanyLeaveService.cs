using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Betriebsurlaub (see <see cref="CompanyLeaves"/>): every change stages the generated vacation rows and saves them with the
/// definition in one save, so a closed month asks for the reason once and a refusal leaves nothing half done.
/// </summary>
public class CompanyLeaveService
{
    private readonly TimeTrackingDbContext _context;
    private readonly ILogger<CompanyLeaveService> _logger;

    public CompanyLeaveService(TimeTrackingDbContext context, ILogger<CompanyLeaveService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public const string NotFoundMessage = "Betriebsurlaub not found.";

    public async Task<ServiceResult<List<CompanyLeaveResponse>>> ListAsync(string slug, int callerUserId)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null) return ServiceResult.NotFound<List<CompanyLeaveResponse>>("Organization not found");
        var role = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (role == null) return ServiceResult.Forbidden<List<CompanyLeaveResponse>>();

        var leaves = await _context.CompanyLeaves.AsNoTracking().Include(l => l.Exclusions)
            .Where(l => l.OrganizationId == org.Id).OrderByDescending(l => l.From).ToListAsync();
        return ServiceResult.Ok(leaves.Select(l => Map(l, role >= OrganizationRole.Admin)).ToList());
    }

    public async Task<ServiceResult<CompanyLeaveResponse>> CreateAsync(string slug, int callerUserId, CreateCompanyLeaveRequest request)
    {
        var (org, error) = await AdminOrgAsync(slug, callerUserId);
        if (org == null) return error!;
        if (await ValidateAsync(org, request.From, request.To, request.GroupId) is { } invalid) return invalid;

        var leave = new CompanyLeave
        {
            OrganizationId = org.Id, Name = request.Name.Trim(), From = request.From, To = request.To,
            GroupId = request.GroupId, CreatedByUserId = callerUserId
        };
        _context.CompanyLeaves.Add(leave);
        await CompanyLeaves.StageAsync(_context, org, [leave], leave.From, createdBy: callerUserId);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Betriebsurlaub {LeaveId} created in org {Slug} by user {UserId}", leave.Id, slug, callerUserId);
        return ServiceResult.Ok(Map(leave, true));
    }

    public async Task<ServiceResult<CompanyLeaveResponse>> UpdateAsync(string slug, int callerUserId, int id, UpdateCompanyLeaveRequest request)
    {
        var (org, error) = await AdminOrgAsync(slug, callerUserId);
        if (org == null) return error!;
        var leave = await _context.CompanyLeaves.Include(l => l.Exclusions)
            .FirstOrDefaultAsync(l => l.Id == id && l.OrganizationId == org.Id);
        if (leave == null) return ServiceResult.NotFound<CompanyLeaveResponse>(NotFoundMessage);

        var oldFrom = leave.From;
        var from = request.From ?? leave.From;
        var to = request.To ?? leave.To;
        var groupId = request.ForWholeOrganization ? null : request.GroupId ?? leave.GroupId;
        if (await ValidateAsync(org, from, to, groupId) is { } invalid) return invalid;
        var renamed = request.Name != null && request.Name.Trim() != leave.Name;
        (leave.Name, leave.From, leave.To, leave.GroupId) = (request.Name?.Trim() ?? leave.Name, from, to, groupId);

        // Rows outside the new range go, the rest follows the definition; a new name is the generated rows' note
        await CompanyLeaves.StageAsync(_context, org, [leave], oldFrom < from ? oldFrom : from, createdBy: callerUserId);
        if (renamed)
            foreach (var row in await _context.AbsenceDays.Where(a => a.CompanyLeaveId == leave.Id).ToListAsync())
                if (_context.Entry(row).State != EntityState.Deleted) row.Note = leave.Name;
        await _context.SaveChangesAsync();
        return ServiceResult.Ok(Map(leave, true));
    }

    public async Task<ServiceResult> DeleteAsync(string slug, int callerUserId, int id)
    {
        var (org, error) = await AdminOrgAsync(slug, callerUserId);
        if (org == null) return error!;
        var leave = await _context.CompanyLeaves.Include(l => l.Exclusions)
            .FirstOrDefaultAsync(l => l.Id == id && l.OrganizationId == org.Id);
        if (leave == null) return ServiceResult.NotFound(NotFoundMessage);

        _context.CompanyLeaves.Remove(leave);
        await CompanyLeaves.StageAsync(_context, org, [leave], DateOnly.MinValue);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Betriebsurlaub {LeaveId} deleted from org {Slug} by user {UserId}", id, slug, callerUserId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<CompanyLeaveResponse>> SetExclusionsAsync(string slug, int callerUserId, int id, SetExclusionsRequest request)
    {
        var (org, error) = await AdminOrgAsync(slug, callerUserId);
        if (org == null) return error!;
        var leave = await _context.CompanyLeaves.Include(l => l.Exclusions)
            .FirstOrDefaultAsync(l => l.Id == id && l.OrganizationId == org.Id);
        if (leave == null) return ServiceResult.NotFound<CompanyLeaveResponse>(NotFoundMessage);

        if (await HolidayService.ReplaceExclusionsAsync(_context, org.Id, leave.Exclusions, request.UserIds, x => x.CompanyLeave = leave) is { } invalid)
            return ServiceResult.BadRequest<CompanyLeaveResponse>(invalid);
        await CompanyLeaves.StageAsync(_context, org, [leave], leave.From, createdBy: callerUserId);
        await _context.SaveChangesAsync();
        return ServiceResult.Ok(Map(leave, true));
    }

    private async Task<(Organization? Org, ServiceResult<CompanyLeaveResponse>? Error)> AdminOrgAsync(string slug, int callerUserId)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null) return (null, ServiceResult.NotFound<CompanyLeaveResponse>("Organization not found"));
        var role = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        return role >= OrganizationRole.Admin ? (org, null) : (null, ServiceResult.Forbidden<CompanyLeaveResponse>());
    }

    private async Task<ServiceResult<CompanyLeaveResponse>?> ValidateAsync(Organization org, DateOnly from, DateOnly to, int? groupId)
    {
        if (to < from || to.DayNumber - from.DayNumber >= CompanyLeaves.MaxDays || from.Year < 2000)
            return ServiceResult.BadRequest<CompanyLeaveResponse>(CompanyLeaves.RangeMessage);
        if (groupId is int g && !await _context.Groups.AnyAsync(x => x.Id == g && x.OrganizationId == org.Id))
            return ServiceResult.NotFound<CompanyLeaveResponse>(HolidayService.GroupNotFoundMessage);
        return null;
    }

    private static CompanyLeaveResponse Map(CompanyLeave l, bool admin) => new()
    {
        Id = l.Id, Name = l.Name, From = l.From, To = l.To, GroupId = l.GroupId,
        ExcludedUserIds = admin ? l.Exclusions.Select(x => x.UserId).Order().ToList() : null
    };
}
