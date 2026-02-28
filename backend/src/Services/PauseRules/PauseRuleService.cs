using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public class PauseRuleService : IPauseRuleService
{
    private readonly TimeTrackingDbContext _context;

    public PauseRuleService(TimeTrackingDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceResult<List<PauseRuleResponse>>> GetPauseRulesAsync(string slug)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<List<PauseRuleResponse>>("Organization not found");

        var rules = await _context.PauseRules
            .Where(pr => pr.OrganizationId == org.Id)
            .OrderBy(pr => pr.MinHours)
            .Select(pr => new PauseRuleResponse
            {
                Id = pr.Id,
                OrganizationId = pr.OrganizationId,
                MinHours = pr.MinHours,
                PauseMinutes = pr.PauseMinutes
            })
            .ToListAsync();

        return ServiceResult.Ok(rules);
    }

    public async Task<ServiceResult<PauseRuleResponse>> CreatePauseRuleAsync(
        string slug, int callerUserId, CreatePauseRuleRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<PauseRuleResponse>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<PauseRuleResponse>();

        if (request.MinHours <= 0)
            return ServiceResult.BadRequest<PauseRuleResponse>("MinHours must be greater than 0.");
        if (request.PauseMinutes <= 0)
            return ServiceResult.BadRequest<PauseRuleResponse>("PauseMinutes must be greater than 0.");

        var exists = await _context.PauseRules
            .AnyAsync(pr => pr.OrganizationId == org.Id && Math.Abs(pr.MinHours - request.MinHours) < 0.01);
        if (exists)
            return ServiceResult.BadRequest<PauseRuleResponse>("A pause rule with this threshold already exists.");

        var rule = new PauseRule
        {
            OrganizationId = org.Id,
            MinHours = request.MinHours,
            PauseMinutes = request.PauseMinutes,
            CreatedAt = DateTime.UtcNow
        };

        _context.PauseRules.Add(rule);
        await _context.SaveChangesAsync();

        return ServiceResult.Ok(MapToResponse(rule));
    }

    public async Task<ServiceResult<PauseRuleResponse>> UpdatePauseRuleAsync(
        string slug, int callerUserId, int ruleId, UpdatePauseRuleRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<PauseRuleResponse>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<PauseRuleResponse>();

        var rule = await _context.PauseRules
            .FirstOrDefaultAsync(pr => pr.Id == ruleId && pr.OrganizationId == org.Id);
        if (rule == null)
            return ServiceResult.NotFound<PauseRuleResponse>("Pause rule not found.");

        rule.MinHours = request.MinHours;
        rule.PauseMinutes = request.PauseMinutes;
        await _context.SaveChangesAsync();

        return ServiceResult.Ok(MapToResponse(rule));
    }

    public async Task<ServiceResult> DeletePauseRuleAsync(string slug, int callerUserId, int ruleId)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden();

        var rule = await _context.PauseRules
            .FirstOrDefaultAsync(pr => pr.Id == ruleId && pr.OrganizationId == org.Id);
        if (rule == null)
            return ServiceResult.NotFound("Pause rule not found.");

        _context.PauseRules.Remove(rule);
        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    // ────────────────────────────────────────────────────
    //  Private helpers
    // ────────────────────────────────────────────────────

    private static PauseRuleResponse MapToResponse(PauseRule rule) => new()
    {
        Id = rule.Id,
        OrganizationId = rule.OrganizationId,
        MinHours = rule.MinHours,
        PauseMinutes = rule.PauseMinutes
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
