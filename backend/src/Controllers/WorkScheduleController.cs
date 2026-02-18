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

    // ────────────────────────────────────────────────────
    //  GET  /api/organizations/{slug}/work-schedule
    //  Get the current user's work schedule for this org
    // ────────────────────────────────────────────────────
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

        return Ok(new WorkScheduleResponse
        {
            UserId = userId.Value,
            OrganizationId = org.Id,
            WeeklyWorkHours = membership.WeeklyWorkHours,
            TargetMon = membership.TargetMon,
            TargetTue = membership.TargetTue,
            TargetWed = membership.TargetWed,
            TargetThu = membership.TargetThu,
            TargetFri = membership.TargetFri,
            InitialOvertimeHours = membership.InitialOvertimeHours,
            InitialOvertimeMode = org.InitialOvertimeMode.ToString()
        });
    }

    // ────────────────────────────────────────────────────
    //  PUT  /api/organizations/{slug}/work-schedule
    //  Update the current user's work schedule
    // ────────────────────────────────────────────────────
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

        // Update initial overtime balance if provided (only if org allows it directly)
        if (request.InitialOvertimeHours.HasValue && org.InitialOvertimeMode == RuleMode.Allowed)
        {
            membership.InitialOvertimeHours = request.InitialOvertimeHours.Value;
        }

        await _context.SaveChangesAsync();

        return Ok(new WorkScheduleResponse
        {
            UserId = userId.Value,
            OrganizationId = org.Id,
            WeeklyWorkHours = membership.WeeklyWorkHours,
            TargetMon = membership.TargetMon,
            TargetTue = membership.TargetTue,
            TargetWed = membership.TargetWed,
            TargetThu = membership.TargetThu,
            TargetFri = membership.TargetFri,
            InitialOvertimeHours = membership.InitialOvertimeHours,
            InitialOvertimeMode = org.InitialOvertimeMode.ToString()
        });
    }
}
