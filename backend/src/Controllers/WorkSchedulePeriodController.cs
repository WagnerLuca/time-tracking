using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Controllers;

[ApiController]
[Route("api/organizations")]
public class WorkSchedulePeriodController : OrganizationBaseController
{
    public WorkSchedulePeriodController(TimeTrackingDbContext context) : base(context) { }

    // ────────────────────────────────────────────────────
    //  GET  /api/organizations/{slug}/schedule-periods
    //  Get the current user's schedule periods
    // ────────────────────────────────────────────────────
    [HttpGet("{slug}/schedule-periods")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<WorkSchedulePeriodResponse>>> GetMySchedulePeriods(string slug)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId.Value && uo.IsActive);
        if (membership == null) return Forbid();

        var periods = await GetSchedulePeriods(userId.Value, org.Id);
        return Ok(periods);
    }

    // ────────────────────────────────────────────────────
    //  POST  /api/organizations/{slug}/schedule-periods
    //  Create a schedule period (self, respects WorkScheduleChangeMode)
    // ────────────────────────────────────────────────────
    [HttpPost("{slug}/schedule-periods")]
    [Authorize]
    public async Task<ActionResult<WorkSchedulePeriodResponse>> CreateMySchedulePeriod(
        string slug, [FromBody] CreateWorkSchedulePeriodRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId.Value && uo.IsActive);
        if (membership == null) return Forbid();

        if (org.WorkScheduleChangeMode == RuleMode.Disabled)
            return BadRequest(new { message = "Schedule changes are disabled for this organization." });
        if (org.WorkScheduleChangeMode == RuleMode.RequiresApproval)
            return BadRequest(new { message = "Schedule changes require admin approval. Please submit a request." });

        var period = BuildPeriod(userId.Value, org.Id, request);

        // Validate no overlapping periods
        var overlapError = await CheckOverlap(userId.Value, org.Id, request.ValidFrom, request.ValidTo);
        if (overlapError != null) return BadRequest(new { message = overlapError });

        _context.WorkSchedulePeriods.Add(period);

        // Auto-close previous open-ended period
        await CloseOpenPeriod(userId.Value, org.Id, request.ValidFrom);

        await _context.SaveChangesAsync();

        return Ok(MapPeriod(period));
    }

    // ────────────────────────────────────────────────────
    //  PUT  /api/organizations/{slug}/schedule-periods/{id}
    //  Update a schedule period (self, respects WorkScheduleChangeMode)
    // ────────────────────────────────────────────────────
    [HttpPut("{slug}/schedule-periods/{id}")]
    [Authorize]
    public async Task<ActionResult<WorkSchedulePeriodResponse>> UpdateMySchedulePeriod(
        string slug, int id, [FromBody] UpdateWorkSchedulePeriodRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId.Value && uo.IsActive);
        if (membership == null) return Forbid();

        if (org.WorkScheduleChangeMode == RuleMode.Disabled)
            return BadRequest(new { message = "Schedule changes are disabled." });
        if (org.WorkScheduleChangeMode == RuleMode.RequiresApproval)
            return BadRequest(new { message = "Schedule changes require admin approval." });

        var period = await _context.WorkSchedulePeriods
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId.Value && p.OrganizationId == org.Id);
        if (period == null) return NotFound(new { message = "Schedule period not found" });

        ApplyUpdate(period, request);
        await _context.SaveChangesAsync();

        return Ok(MapPeriod(period));
    }

    // ────────────────────────────────────────────────────
    //  DELETE  /api/organizations/{slug}/schedule-periods/{id}
    //  Delete own schedule period
    // ────────────────────────────────────────────────────
    [HttpDelete("{slug}/schedule-periods/{id}")]
    [Authorize]
    public async Task<ActionResult> DeleteMySchedulePeriod(string slug, int id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        if (org.WorkScheduleChangeMode == RuleMode.Disabled)
            return BadRequest(new { message = "Schedule changes are disabled." });
        if (org.WorkScheduleChangeMode == RuleMode.RequiresApproval)
            return BadRequest(new { message = "Schedule changes require admin approval." });

        var period = await _context.WorkSchedulePeriods
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId.Value && p.OrganizationId == org.Id);
        if (period == null) return NotFound(new { message = "Schedule period not found" });

        _context.WorkSchedulePeriods.Remove(period);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // ────────────────────────────────────────────────────
    //  Admin endpoints for managing member schedule periods
    // ────────────────────────────────────────────────────

    // GET  /api/organizations/{slug}/members/{memberId}/schedule-periods
    [HttpGet("{slug}/members/{memberId}/schedule-periods")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<WorkSchedulePeriodResponse>>> GetMemberSchedulePeriods(
        string slug, int memberId)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin) return Forbid();

        var periods = await GetSchedulePeriods(memberId, org.Id);
        return Ok(periods);
    }

    // POST  /api/organizations/{slug}/members/{memberId}/schedule-periods
    [HttpPost("{slug}/members/{memberId}/schedule-periods")]
    [Authorize]
    public async Task<ActionResult<WorkSchedulePeriodResponse>> CreateMemberSchedulePeriod(
        string slug, int memberId, [FromBody] CreateWorkSchedulePeriodRequest request)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin) return Forbid();

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == memberId && uo.IsActive);
        if (membership == null) return NotFound(new { message = "Member not found" });

        var period = BuildPeriod(memberId, org.Id, request);

        // Validate no overlapping periods
        var overlapError = await CheckOverlap(memberId, org.Id, request.ValidFrom, request.ValidTo);
        if (overlapError != null) return BadRequest(new { message = overlapError });

        _context.WorkSchedulePeriods.Add(period);

        await CloseOpenPeriod(memberId, org.Id, request.ValidFrom);
        await _context.SaveChangesAsync();

        return Ok(MapPeriod(period));
    }

    // PUT  /api/organizations/{slug}/members/{memberId}/schedule-periods/{id}
    [HttpPut("{slug}/members/{memberId}/schedule-periods/{id}")]
    [Authorize]
    public async Task<ActionResult<WorkSchedulePeriodResponse>> UpdateMemberSchedulePeriod(
        string slug, int memberId, int id, [FromBody] UpdateWorkSchedulePeriodRequest request)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin) return Forbid();

        var period = await _context.WorkSchedulePeriods
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == memberId && p.OrganizationId == org.Id);
        if (period == null) return NotFound(new { message = "Schedule period not found" });

        ApplyUpdate(period, request);
        await _context.SaveChangesAsync();

        return Ok(MapPeriod(period));
    }

    // DELETE  /api/organizations/{slug}/members/{memberId}/schedule-periods/{id}
    [HttpDelete("{slug}/members/{memberId}/schedule-periods/{id}")]
    [Authorize]
    public async Task<ActionResult> DeleteMemberSchedulePeriod(string slug, int memberId, int id)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin) return Forbid();

        var period = await _context.WorkSchedulePeriods
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == memberId && p.OrganizationId == org.Id);
        if (period == null) return NotFound(new { message = "Schedule period not found" });

        _context.WorkSchedulePeriods.Remove(period);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // ────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────

    private async Task<List<WorkSchedulePeriodResponse>> GetSchedulePeriods(int userId, int orgId)
    {
        return await _context.WorkSchedulePeriods
            .Where(p => p.UserId == userId && p.OrganizationId == orgId)
            .OrderBy(p => p.ValidFrom)
            .Select(p => new WorkSchedulePeriodResponse
            {
                Id = p.Id,
                UserId = p.UserId,
                OrganizationId = p.OrganizationId,
                ValidFrom = p.ValidFrom,
                ValidTo = p.ValidTo,
                WeeklyWorkHours = p.WeeklyWorkHours,
                TargetMon = p.TargetMon,
                TargetTue = p.TargetTue,
                TargetWed = p.TargetWed,
                TargetThu = p.TargetThu,
                TargetFri = p.TargetFri
            })
            .ToListAsync();
    }

    private static WorkSchedulePeriod BuildPeriod(int userId, int orgId, CreateWorkSchedulePeriodRequest request)
    {
        var period = new WorkSchedulePeriod
        {
            UserId = userId,
            OrganizationId = orgId,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            WeeklyWorkHours = request.WeeklyWorkHours
        };

        if (request.WeeklyWorkHours.HasValue && request.DistributeEvenly)
        {
            var daily = Math.Round(request.WeeklyWorkHours.Value / 5.0, 2);
            period.TargetMon = daily;
            period.TargetTue = daily;
            period.TargetWed = daily;
            period.TargetThu = daily;
            period.TargetFri = daily;
        }
        else
        {
            period.TargetMon = request.TargetMon ?? 0;
            period.TargetTue = request.TargetTue ?? 0;
            period.TargetWed = request.TargetWed ?? 0;
            period.TargetThu = request.TargetThu ?? 0;
            period.TargetFri = request.TargetFri ?? 0;
        }

        return period;
    }

    private static void ApplyUpdate(WorkSchedulePeriod period, UpdateWorkSchedulePeriodRequest request)
    {
        if (request.ValidFrom.HasValue) period.ValidFrom = request.ValidFrom.Value;
        period.ValidTo = request.ValidTo ?? period.ValidTo;

        if (request.WeeklyWorkHours.HasValue)
        {
            period.WeeklyWorkHours = request.WeeklyWorkHours.Value;

            if (request.DistributeEvenly)
            {
                var daily = Math.Round(request.WeeklyWorkHours.Value / 5.0, 2);
                period.TargetMon = daily;
                period.TargetTue = daily;
                period.TargetWed = daily;
                period.TargetThu = daily;
                period.TargetFri = daily;
            }
            else
            {
                period.TargetMon = request.TargetMon ?? period.TargetMon;
                period.TargetTue = request.TargetTue ?? period.TargetTue;
                period.TargetWed = request.TargetWed ?? period.TargetWed;
                period.TargetThu = request.TargetThu ?? period.TargetThu;
                period.TargetFri = request.TargetFri ?? period.TargetFri;
            }
        }
    }

    private async Task CloseOpenPeriod(int userId, int orgId, DateOnly newFrom)
    {
        var openPeriod = await _context.WorkSchedulePeriods
            .Where(p => p.UserId == userId && p.OrganizationId == orgId && p.ValidTo == null && p.ValidFrom < newFrom)
            .OrderByDescending(p => p.ValidFrom)
            .FirstOrDefaultAsync();

        if (openPeriod != null)
        {
            openPeriod.ValidTo = newFrom.AddDays(-1);
        }
    }

    private async Task<string?> CheckOverlap(int userId, int orgId, DateOnly from, DateOnly? to, int? excludeId = null)
    {
        var existing = await _context.WorkSchedulePeriods
            .Where(p => p.UserId == userId && p.OrganizationId == orgId && (excludeId == null || p.Id != excludeId))
            .ToListAsync();

        foreach (var p in existing)
        {
            var pEnd = p.ValidTo ?? DateOnly.MaxValue;
            var newEnd = to ?? DateOnly.MaxValue;

            // Two ranges overlap if start1 <= end2 AND start2 <= end1
            if (from <= pEnd && p.ValidFrom <= newEnd)
            {
                // But allow adjacent periods (e.g., old ends day before new starts)
                // The CloseOpenPeriod will handle this, so only block true overlaps
                // where the overlap is more than just touching
                if (from < pEnd && p.ValidFrom < newEnd)
                {
                    // Check if CloseOpenPeriod would fix this (old open period gets closed to from-1)
                    if (p.ValidTo == null && p.ValidFrom < from)
                    {
                        // This will be auto-closed by CloseOpenPeriod, allow it
                        continue;
                    }
                    return $"This period overlaps with an existing period ({p.ValidFrom:yyyy-MM-dd} – {(p.ValidTo.HasValue ? p.ValidTo.Value.ToString("yyyy-MM-dd") : "ongoing")}). Only one schedule period at a time is allowed.";
                }
            }
        }
        return null;
    }

    private static WorkSchedulePeriodResponse MapPeriod(WorkSchedulePeriod p) => new()
    {
        Id = p.Id,
        UserId = p.UserId,
        OrganizationId = p.OrganizationId,
        ValidFrom = p.ValidFrom,
        ValidTo = p.ValidTo,
        WeeklyWorkHours = p.WeeklyWorkHours,
        TargetMon = p.TargetMon,
        TargetTue = p.TargetTue,
        TargetWed = p.TargetWed,
        TargetThu = p.TargetThu,
        TargetFri = p.TargetFri
    };
}
