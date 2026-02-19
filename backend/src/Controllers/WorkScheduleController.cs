using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Controllers;

[ApiController]
[Route("api/organizations")]
public class WorkScheduleController : OrganizationBaseController
{
    public WorkScheduleController(TimeTrackingDbContext context) : base(context) { }

    // GET  /api/organizations/{slug}/work-schedule
    [HttpGet("{slug}/work-schedule")]
    [Authorize]
    public async Task<ActionResult<WorkScheduleResponse>> GetMyWorkSchedule(string slug)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId.Value && uo.IsActive);

        if (membership == null)
            return NotFound(new { message = "You are not a member of this organization." });

        return Ok(await BuildScheduleResponse(userId.Value, org, membership));
    }

    // PUT  /api/organizations/{slug}/work-schedule
    [HttpPut("{slug}/work-schedule")]
    [Authorize]
    public async Task<ActionResult<WorkScheduleResponse>> UpdateMyWorkSchedule(string slug, [FromBody] UpdateWorkScheduleRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId.Value && uo.IsActive);

        if (membership == null)
            return NotFound(new { message = "You are not a member of this organization." });

        ApplyScheduleUpdate(membership, request);

        if (request.InitialOvertimeHours.HasValue && org.InitialOvertimeMode == RuleMode.Allowed)
            membership.InitialOvertimeHours = request.InitialOvertimeHours.Value;

        await _context.SaveChangesAsync();

        return Ok(await BuildScheduleResponse(userId.Value, org, membership));
    }

    // PUT  /api/organizations/{slug}/initial-overtime
    [HttpPut("{slug}/initial-overtime")]
    [Authorize]
    public async Task<ActionResult> UpdateMyInitialOvertime(string slug, [FromBody] SetInitialOvertimeRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId.Value && uo.IsActive);
        if (membership == null)
            return NotFound(new { message = "You are not a member of this organization." });

        if (org.InitialOvertimeMode == RuleMode.Disabled)
            return BadRequest(new { message = "Initial overtime is disabled for this organization." });
        if (org.InitialOvertimeMode == RuleMode.RequiresApproval)
            return BadRequest(new { message = "Initial overtime requires admin approval. Please submit a request." });

        membership.InitialOvertimeHours = request.InitialOvertimeHours;
        await _context.SaveChangesAsync();

        return Ok(new { initialOvertimeHours = membership.InitialOvertimeHours });
    }

    // GET  /api/organizations/{slug}/members/{memberId}/work-schedule
    [HttpGet("{slug}/members/{memberId}/work-schedule")]
    [Authorize]
    public async Task<ActionResult<WorkScheduleResponse>> GetMemberWorkSchedule(string slug, int memberId)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == memberId && uo.IsActive);
        if (membership == null)
            return NotFound(new { message = "Member not found in this organization." });

        return Ok(await BuildScheduleResponse(memberId, org, membership));
    }

    // PUT  /api/organizations/{slug}/members/{memberId}/work-schedule
    [HttpPut("{slug}/members/{memberId}/work-schedule")]
    [Authorize]
    public async Task<ActionResult<WorkScheduleResponse>> UpdateMemberWorkSchedule(
        string slug, int memberId, [FromBody] UpdateWorkScheduleRequest request)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == memberId && uo.IsActive);
        if (membership == null)
            return NotFound(new { message = "Member not found in this organization." });

        ApplyScheduleUpdate(membership, request);

        if (request.InitialOvertimeHours.HasValue)
            membership.InitialOvertimeHours = request.InitialOvertimeHours.Value;

        await _context.SaveChangesAsync();

        return Ok(await BuildScheduleResponse(memberId, org, membership));
    }

    // Helpers
    private async Task<WorkScheduleResponse> BuildScheduleResponse(int userId, Organization org, UserOrganization membership)
    {
        // Check for an active work schedule period covering today
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var activePeriod = await _context.WorkSchedulePeriods
            .Where(p => p.UserId == userId && p.OrganizationId == org.Id
                && p.ValidFrom <= today
                && (p.ValidTo == null || p.ValidTo >= today))
            .OrderByDescending(p => p.ValidFrom)
            .FirstOrDefaultAsync();

        if (activePeriod != null)
        {
            return new WorkScheduleResponse
            {
                UserId = userId,
                OrganizationId = org.Id,
                WeeklyWorkHours = activePeriod.WeeklyWorkHours,
                TargetMon = activePeriod.TargetMon,
                TargetTue = activePeriod.TargetTue,
                TargetWed = activePeriod.TargetWed,
                TargetThu = activePeriod.TargetThu,
                TargetFri = activePeriod.TargetFri,
                InitialOvertimeHours = membership.InitialOvertimeHours,
                InitialOvertimeMode = org.InitialOvertimeMode.ToString(),
                WorkScheduleChangeMode = org.WorkScheduleChangeMode.ToString()
            };
        }

        return new WorkScheduleResponse
        {
            UserId = userId,
            OrganizationId = org.Id,
            WeeklyWorkHours = membership.WeeklyWorkHours,
            TargetMon = membership.TargetMon,
            TargetTue = membership.TargetTue,
            TargetWed = membership.TargetWed,
            TargetThu = membership.TargetThu,
            TargetFri = membership.TargetFri,
            InitialOvertimeHours = membership.InitialOvertimeHours,
            InitialOvertimeMode = org.InitialOvertimeMode.ToString(),
            WorkScheduleChangeMode = org.WorkScheduleChangeMode.ToString()
        };
    }

    private static void ApplyScheduleUpdate(UserOrganization membership, UpdateWorkScheduleRequest request)
    {
        if (request.WeeklyWorkHours.HasValue)
        {
            membership.WeeklyWorkHours = request.WeeklyWorkHours.Value;

            if (request.DistributeEvenly)
            {
                var daily = Math.Round(request.WeeklyWorkHours.Value / 5.0, 2);
                membership.TargetMon = daily;
                membership.TargetTue = daily;
                membership.TargetWed = daily;
                membership.TargetThu = daily;
                membership.TargetFri = daily;
            }
            else
            {
                membership.TargetMon = request.TargetMon ?? membership.TargetMon;
                membership.TargetTue = request.TargetTue ?? membership.TargetTue;
                membership.TargetWed = request.TargetWed ?? membership.TargetWed;
                membership.TargetThu = request.TargetThu ?? membership.TargetThu;
                membership.TargetFri = request.TargetFri ?? membership.TargetFri;
            }
        }
        else if (!request.DistributeEvenly)
        {
            if (request.TargetMon.HasValue) membership.TargetMon = request.TargetMon.Value;
            if (request.TargetTue.HasValue) membership.TargetTue = request.TargetTue.Value;
            if (request.TargetWed.HasValue) membership.TargetWed = request.TargetWed.Value;
            if (request.TargetThu.HasValue) membership.TargetThu = request.TargetThu.Value;
            if (request.TargetFri.HasValue) membership.TargetFri = request.TargetFri.Value;
        }
    }
}
