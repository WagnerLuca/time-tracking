using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public class WorkScheduleService : IWorkScheduleService
{
    private readonly TimeTrackingDbContext _context;

    public WorkScheduleService(TimeTrackingDbContext context)
    {
        _context = context;
    }

    // ────────────────────────────────────────────────────
    //  Self-service endpoints
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<WorkScheduleResponse>> GetMyWorkScheduleAsync(string slug, int userId)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("Organization not found");

        var membership = await GetMembershipAsync(userId, org.Id);
        if (membership == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("You are not a member of this organization.");

        var schedule = await GetCurrentScheduleAsync(userId, org.Id);
        return ServiceResult.Ok(MapToResponse(schedule, userId, org, membership));
    }

    public async Task<ServiceResult<List<WorkScheduleResponse>>> GetMyWorkSchedulesAsync(string slug, int userId)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<List<WorkScheduleResponse>>("Organization not found");

        var membership = await GetMembershipAsync(userId, org.Id);
        if (membership == null)
            return ServiceResult.Forbidden<List<WorkScheduleResponse>>();

        var schedules = await GetAllSchedulesAsync(userId, org.Id);
        var result = schedules.Select(s => MapToResponse(s, userId, org, membership)).ToList();
        return ServiceResult.Ok(result);
    }

    public async Task<ServiceResult<WorkScheduleResponse>> CreateMyWorkScheduleAsync(
        string slug, int userId, CreateWorkScheduleRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("Organization not found");

        var membership = await GetMembershipAsync(userId, org.Id);
        if (membership == null)
            return ServiceResult.Forbidden<WorkScheduleResponse>();

        var ruleModeError = CheckRuleMode(org.WorkScheduleChangeMode, "Schedule changes");
        if (ruleModeError != null)
            return ServiceResult.BadRequest<WorkScheduleResponse>(ruleModeError);

        var overlapError = await CheckOverlapAsync(userId, org.Id, request.ValidFrom, request.ValidTo);
        if (overlapError != null)
            return ServiceResult.BadRequest<WorkScheduleResponse>(overlapError);

        var schedule = BuildSchedule(userId, org.Id, request);
        _context.WorkSchedules.Add(schedule);
        await CloseOpenScheduleAsync(userId, org.Id, request.ValidFrom);
        await _context.SaveChangesAsync();

        return ServiceResult.Ok(MapToResponse(schedule, userId, org, membership));
    }

    public async Task<ServiceResult<WorkScheduleResponse>> UpdateMyWorkScheduleAsync(
        string slug, int userId, int scheduleId, UpdateWorkScheduleRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("Organization not found");

        var membership = await GetMembershipAsync(userId, org.Id);
        if (membership == null)
            return ServiceResult.Forbidden<WorkScheduleResponse>();

        var ruleModeError = CheckRuleMode(org.WorkScheduleChangeMode, "Schedule changes");
        if (ruleModeError != null)
            return ServiceResult.BadRequest<WorkScheduleResponse>(ruleModeError);

        var schedule = await _context.WorkSchedules
            .FirstOrDefaultAsync(s => s.Id == scheduleId && s.UserId == userId && s.OrganizationId == org.Id);
        if (schedule == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("Work schedule not found");

        ApplyUpdate(schedule, request);
        await _context.SaveChangesAsync();

        return ServiceResult.Ok(MapToResponse(schedule, userId, org, membership));
    }

    public async Task<ServiceResult> DeleteMyWorkScheduleAsync(string slug, int userId, int scheduleId)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound("Organization not found");

        var ruleModeError = CheckRuleMode(org.WorkScheduleChangeMode, "Schedule changes");
        if (ruleModeError != null)
            return ServiceResult.BadRequest(ruleModeError);

        var schedule = await _context.WorkSchedules
            .FirstOrDefaultAsync(s => s.Id == scheduleId && s.UserId == userId && s.OrganizationId == org.Id);
        if (schedule == null)
            return ServiceResult.NotFound("Work schedule not found");

        _context.WorkSchedules.Remove(schedule);
        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<object>> UpdateMyInitialOvertimeAsync(
        string slug, int userId, SetInitialOvertimeRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<object>("Organization not found");

        var membership = await GetMembershipAsync(userId, org.Id);
        if (membership == null)
            return ServiceResult.NotFound<object>("You are not a member of this organization.");

        if (org.InitialOvertimeMode == RuleMode.Disabled)
            return ServiceResult.BadRequest<object>("Initial overtime is disabled for this organization.");
        if (org.InitialOvertimeMode == RuleMode.RequiresApproval)
            return ServiceResult.BadRequest<object>("Initial overtime requires admin approval. Please submit a request.");

        membership.InitialOvertimeHours = request.InitialOvertimeHours;
        await _context.SaveChangesAsync();

        return ServiceResult.Ok<object>(new { initialOvertimeHours = membership.InitialOvertimeHours });
    }

    // ────────────────────────────────────────────────────
    //  Admin-managed endpoints
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<WorkScheduleResponse>> GetMemberWorkScheduleAsync(
        string slug, int callerUserId, int memberId)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<WorkScheduleResponse>();

        var membership = await GetMembershipAsync(memberId, org.Id);
        if (membership == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("Member not found in this organization.");

        var schedule = await GetCurrentScheduleAsync(memberId, org.Id);
        return ServiceResult.Ok(MapToResponse(schedule, memberId, org, membership));
    }

    public async Task<ServiceResult<List<WorkScheduleResponse>>> GetMemberWorkSchedulesAsync(
        string slug, int callerUserId, int memberId)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<List<WorkScheduleResponse>>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<List<WorkScheduleResponse>>();

        var membership = await GetMembershipAsync(memberId, org.Id);
        if (membership == null)
            return ServiceResult.NotFound<List<WorkScheduleResponse>>("Member not found in this organization.");

        var schedules = await GetAllSchedulesAsync(memberId, org.Id);
        var result = schedules.Select(s => MapToResponse(s, memberId, org, membership)).ToList();
        return ServiceResult.Ok(result);
    }

    public async Task<ServiceResult<WorkScheduleResponse>> CreateMemberWorkScheduleAsync(
        string slug, int callerUserId, int memberId, CreateWorkScheduleRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<WorkScheduleResponse>();

        var membership = await GetMembershipAsync(memberId, org.Id);
        if (membership == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("Member not found");

        var overlapError = await CheckOverlapAsync(memberId, org.Id, request.ValidFrom, request.ValidTo);
        if (overlapError != null)
            return ServiceResult.BadRequest<WorkScheduleResponse>(overlapError);

        var schedule = BuildSchedule(memberId, org.Id, request);
        _context.WorkSchedules.Add(schedule);
        await CloseOpenScheduleAsync(memberId, org.Id, request.ValidFrom);
        await _context.SaveChangesAsync();

        return ServiceResult.Ok(MapToResponse(schedule, memberId, org, membership));
    }

    public async Task<ServiceResult<WorkScheduleResponse>> UpdateMemberWorkScheduleAsync(
        string slug, int callerUserId, int memberId, int scheduleId, UpdateWorkScheduleRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<WorkScheduleResponse>();

        var schedule = await _context.WorkSchedules
            .FirstOrDefaultAsync(s => s.Id == scheduleId && s.UserId == memberId && s.OrganizationId == org.Id);
        if (schedule == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("Work schedule not found");

        var membership = await GetMembershipAsync(memberId, org.Id);

        ApplyUpdate(schedule, request);
        await _context.SaveChangesAsync();

        return ServiceResult.Ok(MapToResponse(schedule, memberId, org, membership));
    }

    public async Task<ServiceResult> DeleteMemberWorkScheduleAsync(
        string slug, int callerUserId, int memberId, int scheduleId)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden();

        var schedule = await _context.WorkSchedules
            .FirstOrDefaultAsync(s => s.Id == scheduleId && s.UserId == memberId && s.OrganizationId == org.Id);
        if (schedule == null)
            return ServiceResult.NotFound("Work schedule not found");

        _context.WorkSchedules.Remove(schedule);
        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    // ────────────────────────────────────────────────────
    //  Private helpers
    // ────────────────────────────────────────────────────

    private static string? CheckRuleMode(RuleMode mode, string featureName)
    {
        return mode switch
        {
            RuleMode.Disabled         => $"{featureName} are disabled for this organization.",
            RuleMode.RequiresApproval => $"{featureName} require admin approval. Please submit a request.",
            _                         => null
        };
    }

    private static WorkSchedule BuildSchedule(int userId, int orgId, CreateWorkScheduleRequest request)
    {
        var schedule = new WorkSchedule
        {
            UserId    = userId,
            OrganizationId = orgId,
            ValidFrom = request.ValidFrom,
            ValidTo   = request.ValidTo,
            WeeklyWorkHours = request.WeeklyWorkHours
        };

        if (request.WeeklyWorkHours.HasValue && request.DistributeEvenly)
        {
            var daily = Math.Round(request.WeeklyWorkHours.Value / 5.0, 2);
            schedule.TargetMon = daily;
            schedule.TargetTue = daily;
            schedule.TargetWed = daily;
            schedule.TargetThu = daily;
            schedule.TargetFri = daily;
        }
        else
        {
            schedule.TargetMon = request.TargetMon ?? 0;
            schedule.TargetTue = request.TargetTue ?? 0;
            schedule.TargetWed = request.TargetWed ?? 0;
            schedule.TargetThu = request.TargetThu ?? 0;
            schedule.TargetFri = request.TargetFri ?? 0;
        }

        return schedule;
    }

    private static void ApplyUpdate(WorkSchedule schedule, UpdateWorkScheduleRequest request)
    {
        if (request.ValidFrom.HasValue) schedule.ValidFrom = request.ValidFrom.Value;
        schedule.ValidTo = request.ValidTo ?? schedule.ValidTo;

        if (request.WeeklyWorkHours.HasValue)
        {
            schedule.WeeklyWorkHours = request.WeeklyWorkHours.Value;

            if (request.DistributeEvenly)
            {
                var daily = Math.Round(request.WeeklyWorkHours.Value / 5.0, 2);
                schedule.TargetMon = daily;
                schedule.TargetTue = daily;
                schedule.TargetWed = daily;
                schedule.TargetThu = daily;
                schedule.TargetFri = daily;
            }
            else
            {
                schedule.TargetMon = request.TargetMon ?? schedule.TargetMon;
                schedule.TargetTue = request.TargetTue ?? schedule.TargetTue;
                schedule.TargetWed = request.TargetWed ?? schedule.TargetWed;
                schedule.TargetThu = request.TargetThu ?? schedule.TargetThu;
                schedule.TargetFri = request.TargetFri ?? schedule.TargetFri;
            }
        }
    }

    private static WorkScheduleResponse MapToResponse(
        WorkSchedule? schedule, int userId, Organization org, UserOrganization? membership)
    {
        if (schedule == null)
        {
            return new WorkScheduleResponse
            {
                Id = 0,
                UserId = userId,
                OrganizationId = org.Id,
                ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                ValidTo = null,
                WeeklyWorkHours = 0,
                TargetMon = 0, TargetTue = 0, TargetWed = 0, TargetThu = 0, TargetFri = 0,
                InitialOvertimeHours = membership?.InitialOvertimeHours ?? 0,
                InitialOvertimeMode = org.InitialOvertimeMode.ToString(),
                WorkScheduleChangeMode = org.WorkScheduleChangeMode.ToString()
            };
        }

        return new WorkScheduleResponse
        {
            Id = schedule.Id,
            UserId = userId,
            OrganizationId = org.Id,
            ValidFrom = schedule.ValidFrom,
            ValidTo = schedule.ValidTo,
            WeeklyWorkHours = schedule.WeeklyWorkHours,
            TargetMon = schedule.TargetMon,
            TargetTue = schedule.TargetTue,
            TargetWed = schedule.TargetWed,
            TargetThu = schedule.TargetThu,
            TargetFri = schedule.TargetFri,
            InitialOvertimeHours = membership?.InitialOvertimeHours ?? 0,
            InitialOvertimeMode = org.InitialOvertimeMode.ToString(),
            WorkScheduleChangeMode = org.WorkScheduleChangeMode.ToString()
        };
    }

    private async Task<WorkSchedule?> GetCurrentScheduleAsync(int userId, int orgId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await _context.WorkSchedules
            .Where(s => s.UserId == userId && s.OrganizationId == orgId
                && s.ValidFrom <= today
                && (s.ValidTo == null || s.ValidTo >= today))
            .OrderByDescending(s => s.ValidFrom)
            .FirstOrDefaultAsync();
    }

    private async Task<List<WorkSchedule>> GetAllSchedulesAsync(int userId, int orgId)
    {
        return await _context.WorkSchedules
            .Where(s => s.UserId == userId && s.OrganizationId == orgId)
            .OrderBy(s => s.ValidFrom)
            .ToListAsync();
    }

    private async Task CloseOpenScheduleAsync(int userId, int orgId, DateOnly newFrom)
    {
        var openSchedule = await _context.WorkSchedules
            .Where(s => s.UserId == userId && s.OrganizationId == orgId
                     && s.ValidTo == null && s.ValidFrom < newFrom)
            .OrderByDescending(s => s.ValidFrom)
            .FirstOrDefaultAsync();

        if (openSchedule != null)
            openSchedule.ValidTo = newFrom.AddDays(-1);
    }

    private async Task<string?> CheckOverlapAsync(
        int userId, int orgId, DateOnly from, DateOnly? to, int? excludeId = null)
    {
        var existing = await _context.WorkSchedules
            .Where(s => s.UserId == userId && s.OrganizationId == orgId
                     && (excludeId == null || s.Id != excludeId))
            .ToListAsync();

        foreach (var s in existing)
        {
            var sEnd = s.ValidTo ?? DateOnly.MaxValue;
            var newEnd = to ?? DateOnly.MaxValue;

            if (from <= sEnd && s.ValidFrom <= newEnd)
            {
                if (from < sEnd && s.ValidFrom < newEnd)
                {
                    if (s.ValidTo == null && s.ValidFrom < from)
                        continue; // Will be auto-closed

                    return $"This schedule overlaps with an existing one ({s.ValidFrom:yyyy-MM-dd} – " +
                           $"{(s.ValidTo.HasValue ? s.ValidTo.Value.ToString("yyyy-MM-dd") : "ongoing")}). " +
                           "Only one schedule at a time is allowed.";
                }
            }
        }
        return null;
    }

    private async Task<Organization?> GetOrgBySlugAsync(string slug)
    {
        return await _context.Organizations.FirstOrDefaultAsync(o => o.Slug == slug && o.IsActive);
    }

    private async Task<UserOrganization?> GetMembershipAsync(int userId, int orgId)
    {
        return await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == orgId && uo.UserId == userId && uo.IsActive);
    }

    private async Task<OrganizationRole?> GetRoleAsync(int userId, int orgId)
    {
        var membership = await GetMembershipAsync(userId, orgId);
        return membership?.Role;
    }
}
