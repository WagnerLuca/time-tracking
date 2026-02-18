using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Controllers;

[ApiController]
[Route("api/organizations")]
public class PauseRulesController : OrganizationBaseController
{
    public PauseRulesController(TimeTrackingDbContext context) : base(context) { }

    // ────────────────────────────────────────────────────
    //  GET  /api/organizations/{slug}/pause-rules
    // ────────────────────────────────────────────────────
    [HttpGet("{slug}/pause-rules")]
    public async Task<ActionResult<IEnumerable<PauseRuleResponse>>> GetPauseRules(string slug)
    {
        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

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

        return Ok(rules);
    }

    // ────────────────────────────────────────────────────
    //  POST  /api/organizations/{slug}/pause-rules
    // ────────────────────────────────────────────────────
    [HttpPost("{slug}/pause-rules")]
    [Authorize]
    public async Task<ActionResult<PauseRuleResponse>> CreatePauseRule(string slug, [FromBody] CreatePauseRuleRequest request)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        if (request.MinHours <= 0)
            return BadRequest(new { message = "MinHours must be greater than 0." });
        if (request.PauseMinutes <= 0)
            return BadRequest(new { message = "PauseMinutes must be greater than 0." });

        var exists = await _context.PauseRules
            .AnyAsync(pr => pr.OrganizationId == org.Id && Math.Abs(pr.MinHours - request.MinHours) < 0.01);
        if (exists)
            return BadRequest(new { message = "A pause rule with this threshold already exists." });

        var rule = new PauseRule
        {
            OrganizationId = org.Id,
            MinHours = request.MinHours,
            PauseMinutes = request.PauseMinutes,
            CreatedAt = DateTime.UtcNow
        };

        _context.PauseRules.Add(rule);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetPauseRules), new { slug }, new PauseRuleResponse
        {
            Id = rule.Id,
            OrganizationId = rule.OrganizationId,
            MinHours = rule.MinHours,
            PauseMinutes = rule.PauseMinutes
        });
    }

    // ────────────────────────────────────────────────────
    //  PUT  /api/organizations/{slug}/pause-rules/{ruleId}
    // ────────────────────────────────────────────────────
    [HttpPut("{slug}/pause-rules/{ruleId}")]
    [Authorize]
    public async Task<ActionResult<PauseRuleResponse>> UpdatePauseRule(string slug, int ruleId, [FromBody] UpdatePauseRuleRequest request)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        var rule = await _context.PauseRules
            .FirstOrDefaultAsync(pr => pr.Id == ruleId && pr.OrganizationId == org.Id);

        if (rule == null)
            return NotFound(new { message = "Pause rule not found." });

        rule.MinHours = request.MinHours;
        rule.PauseMinutes = request.PauseMinutes;
        await _context.SaveChangesAsync();

        return Ok(new PauseRuleResponse
        {
            Id = rule.Id,
            OrganizationId = rule.OrganizationId,
            MinHours = rule.MinHours,
            PauseMinutes = rule.PauseMinutes
        });
    }

    // ────────────────────────────────────────────────────
    //  DELETE  /api/organizations/{slug}/pause-rules/{ruleId}
    // ────────────────────────────────────────────────────
    [HttpDelete("{slug}/pause-rules/{ruleId}")]
    [Authorize]
    public async Task<IActionResult> DeletePauseRule(string slug, int ruleId)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        var rule = await _context.PauseRules
            .FirstOrDefaultAsync(pr => pr.Id == ruleId && pr.OrganizationId == org.Id);

        if (rule == null)
            return NotFound(new { message = "Pause rule not found." });

        _context.PauseRules.Remove(rule);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
