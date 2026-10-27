using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public class WorkScheduleService : IWorkScheduleService
{
    private readonly TimeTrackingDbContext _context;
    private readonly ILogger<WorkScheduleService> _logger;

    public WorkScheduleService(TimeTrackingDbContext context, ILogger<WorkScheduleService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ────────────────────────────────────────────────────
    //  Self-service endpoints
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<WorkScheduleResponse>> GetMyWorkScheduleAsync(string slug, int userId)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("Organization not found");

        var membership = await GetMembershipAsync(userId, org.Id);
        if (membership == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("You are not a member of this organization.");

        var schedule = await GetCurrentScheduleAsync(userId, org);
        return ServiceResult.Ok(MapToResponse(schedule, userId, org, membership));
    }

    public async Task<ServiceResult<List<WorkScheduleResponse>>> GetMyWorkSchedulesAsync(string slug, int userId)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
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
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("Organization not found");

        var membership = await GetMembershipAsync(userId, org.Id);
        if (membership == null)
            return ServiceResult.Forbidden<WorkScheduleResponse>();

        var ruleModeError = CheckRuleMode(
            await EffectiveRuleResolver.ResolveAsync(_context, org, userId, RequestType.WorkScheduleChange), "Schedule changes");
        if (ruleModeError != null)
            return ServiceResult.Fail<WorkScheduleResponse>(ruleModeError.Value.Type, ruleModeError.Value.Message);

        var overlapError = await CheckOverlapAsync(_context, userId, org.Id, request.ValidFrom, request.ValidTo);
        if (overlapError != null)
            return ServiceResult.BadRequest<WorkScheduleResponse>(overlapError);

        var schedule = BuildSchedule(userId, org.Id, request);
        await InheritContractAsync(_context, schedule, null);
        _context.WorkSchedules.Add(schedule);
        await CloseOpenScheduleAsync(_context, userId, org.Id, request.ValidFrom);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Work schedule {ScheduleId} created by user {UserId} in org {OrgSlug}", schedule.Id, userId, slug);

        return ServiceResult.Ok(MapToResponse(schedule, userId, org, membership));
    }

    public async Task<ServiceResult<WorkScheduleResponse>> UpdateMyWorkScheduleAsync(
        string slug, int userId, int scheduleId, UpdateWorkScheduleRequest request)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("Organization not found");

        var membership = await GetMembershipAsync(userId, org.Id);
        if (membership == null)
            return ServiceResult.Forbidden<WorkScheduleResponse>();

        var ruleModeError = CheckRuleMode(
            await EffectiveRuleResolver.ResolveAsync(_context, org, userId, RequestType.WorkScheduleChange), "Schedule changes");
        if (ruleModeError != null)
            return ServiceResult.Fail<WorkScheduleResponse>(ruleModeError.Value.Type, ruleModeError.Value.Message);

        var schedule = await _context.WorkSchedules
            .FirstOrDefaultAsync(s => s.Id == scheduleId && s.UserId == userId && s.OrganizationId == org.Id);
        if (schedule == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("Work schedule not found");

        var updateError = await CheckUpdateAsync(schedule, request);
        if (updateError != null)
            return ServiceResult.BadRequest<WorkScheduleResponse>(updateError);

        ApplyUpdate(schedule, request);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Work schedule {ScheduleId} updated by user {UserId}", scheduleId, userId);

        return ServiceResult.Ok(MapToResponse(schedule, userId, org, membership));
    }

    public async Task<ServiceResult> DeleteMyWorkScheduleAsync(string slug, int userId, int scheduleId)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound("Organization not found");

        var membership = await GetMembershipAsync(userId, org.Id);
        if (membership == null)
            return ServiceResult.Forbidden("You are not a member of this organization.");

        var ruleModeError = CheckRuleMode(
            await EffectiveRuleResolver.ResolveAsync(_context, org, userId, RequestType.WorkScheduleChange), "Schedule changes");
        if (ruleModeError != null)
            return ServiceResult.Fail(ruleModeError.Value.Type, ruleModeError.Value.Message);

        var schedule = await _context.WorkSchedules
            .FirstOrDefaultAsync(s => s.Id == scheduleId && s.UserId == userId && s.OrganizationId == org.Id);
        if (schedule == null)
            return ServiceResult.NotFound("Work schedule not found");

        await RemoveScheduleAsync(schedule);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Work schedule {ScheduleId} deleted by user {UserId}", scheduleId, userId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<MemberInitialOvertimeResponse>> UpdateMyInitialOvertimeAsync(
        string slug, int userId, SetInitialOvertimeRequest request)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<MemberInitialOvertimeResponse>("Organization not found");

        var membership = await GetMembershipAsync(userId, org.Id);
        if (membership == null)
            return ServiceResult.NotFound<MemberInitialOvertimeResponse>("You are not a member of this organization.");

        var initialOvertimeMode = await EffectiveRuleResolver.ResolveAsync(_context, org, userId, RequestType.SetInitialOvertime);
        if (initialOvertimeMode == RuleMode.Disabled)
            return ServiceResult.BadRequest<MemberInitialOvertimeResponse>("Initial overtime is disabled for this organization.");
        if (initialOvertimeMode == RuleMode.RequiresApproval)
            return ServiceResult.BadRequest<MemberInitialOvertimeResponse>("Initial overtime requires admin approval. Please submit a request.");

        membership.InitialOvertimeHours = request.InitialOvertimeHours;
        await _context.SaveChangesAsync();
        _logger.LogInformation("Self initial overtime set to {Hours}h by user {UserId} in org {OrgSlug}", request.InitialOvertimeHours, userId, slug);

        return ServiceResult.Ok(new MemberInitialOvertimeResponse { UserId = userId, InitialOvertimeHours = membership.InitialOvertimeHours });
    }

    // ────────────────────────────────────────────────────
    //  Admin-managed endpoints
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<WorkScheduleResponse>> GetMemberWorkScheduleAsync(
        string slug, int callerUserId, int memberId)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("Organization not found");

        // Admins, the member, group admin rights and the visibility matrix (kind WorkSchedule)
        var visibility = await MemberVisibility.ForCallerAsync(_context, org, callerUserId);
        switch (visibility.Check(memberId, MemberInfo.WorkSchedule))
        {
            case ServiceErrorType.NotFound:
                return ServiceResult.NotFound<WorkScheduleResponse>("Member not found in this organization.");
            case ServiceErrorType.Forbidden:
                return ServiceResult.Forbidden<WorkScheduleResponse>();
        }

        var membership = await GetMembershipAsync(memberId, org.Id);
        if (membership == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("Member not found in this organization.");
        // The starting overtime balance is payroll data: the person and admins only
        if (!visibility.CallerIsOrgAdmin && memberId != callerUserId)
            membership = null;

        var schedule = await GetCurrentScheduleAsync(memberId, org);
        return ServiceResult.Ok(MapToResponse(schedule, memberId, org, membership));
    }

    public async Task<ServiceResult<List<WorkScheduleResponse>>> GetMemberWorkSchedulesAsync(
        string slug, int callerUserId, int memberId)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<List<WorkScheduleResponse>>("Organization not found");

        // Admins, the member, group admin rights and the visibility matrix (kind WorkSchedule)
        var visibility = await MemberVisibility.ForCallerAsync(_context, org, callerUserId);
        switch (visibility.Check(memberId, MemberInfo.WorkSchedule))
        {
            case ServiceErrorType.NotFound:
                return ServiceResult.NotFound<List<WorkScheduleResponse>>("Member not found in this organization.");
            case ServiceErrorType.Forbidden:
                return ServiceResult.Forbidden<List<WorkScheduleResponse>>();
        }

        var membership = await GetMembershipAsync(memberId, org.Id);
        if (membership == null)
            return ServiceResult.NotFound<List<WorkScheduleResponse>>("Member not found in this organization.");
        // The starting overtime balance is payroll data: the person and admins only
        if (!visibility.CallerIsOrgAdmin && memberId != callerUserId)
            membership = null;

        var schedules = await GetAllSchedulesAsync(memberId, org.Id);
        var result = schedules.Select(s => MapToResponse(s, memberId, org, membership)).ToList();
        return ServiceResult.Ok(result);
    }

    public async Task<ServiceResult<WorkScheduleResponse>> CreateMemberWorkScheduleAsync(
        string slug, int callerUserId, int memberId, CreateWorkScheduleRequest request)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<WorkScheduleResponse>();

        var membership = await GetMembershipAsync(memberId, org.Id);
        if (membership == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("Member not found");

        var overlapError = await CheckOverlapAsync(_context, memberId, org.Id, request.ValidFrom, request.ValidTo);
        if (overlapError != null)
            return ServiceResult.BadRequest<WorkScheduleResponse>(overlapError);

        var schedule = BuildSchedule(memberId, org.Id, request);
        if (await InheritContractAsync(_context, schedule, request) is { } contractError)
            return ServiceResult.BadRequest<WorkScheduleResponse>(contractError);
        _context.WorkSchedules.Add(schedule);
        await CloseOpenScheduleAsync(_context, memberId, org.Id, request.ValidFrom);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Work schedule set for member {MemberId} in org {OrgSlug} by admin {CallerId}", memberId, slug, callerUserId);

        return ServiceResult.Ok(MapToResponse(schedule, memberId, org, membership));
    }

    public async Task<ServiceResult<WorkScheduleResponse>> UpdateMemberWorkScheduleAsync(
        string slug, int callerUserId, int memberId, int scheduleId, UpdateWorkScheduleRequest request)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<WorkScheduleResponse>();

        var schedule = await _context.WorkSchedules
            .FirstOrDefaultAsync(s => s.Id == scheduleId && s.UserId == memberId && s.OrganizationId == org.Id);
        if (schedule == null)
            return ServiceResult.NotFound<WorkScheduleResponse>("Work schedule not found");

        var membership = await GetMembershipAsync(memberId, org.Id);

        var updateError = await CheckUpdateAsync(schedule, request);
        if (updateError != null)
            return ServiceResult.BadRequest<WorkScheduleResponse>(updateError);

        ApplyUpdate(schedule, request);
        if (ApplyContract(schedule, request) is { } contractError)
            return ServiceResult.BadRequest<WorkScheduleResponse>(contractError);
        await _context.SaveChangesAsync();

        return ServiceResult.Ok(MapToResponse(schedule, memberId, org, membership));
    }

    public async Task<ServiceResult> DeleteMemberWorkScheduleAsync(
        string slug, int callerUserId, int memberId, int scheduleId)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden();

        var schedule = await _context.WorkSchedules
            .FirstOrDefaultAsync(s => s.Id == scheduleId && s.UserId == memberId && s.OrganizationId == org.Id);
        if (schedule == null)
            return ServiceResult.NotFound("Work schedule not found");

        await RemoveScheduleAsync(schedule);
        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    // ────────────────────────────────────────────────────
    //  Private helpers
    // ────────────────────────────────────────────────────

    /// <summary>
    /// Removes a period. Adding it had closed the period right before it, so that one now runs
    /// over the removed range again (open-ended if the removed period was) — no silent gap.
    /// </summary>
    private async Task RemoveScheduleAsync(WorkSchedule schedule)
    {
        var dayBefore = schedule.ValidFrom.AddDays(-1);
        var predecessor = await _context.WorkSchedules.FirstOrDefaultAsync(s =>
            s.UserId == schedule.UserId && s.OrganizationId == schedule.OrganizationId && s.ValidTo == dayBefore);
        if (predecessor != null)
            predecessor.ValidTo = schedule.ValidTo;
        _context.WorkSchedules.Remove(schedule);
    }

    private static (ServiceErrorType Type, string Message)? CheckRuleMode(RuleMode mode, string featureName)
    {
        return mode switch
        {
            RuleMode.Disabled         => (ServiceErrorType.Forbidden, $"{featureName} are disabled for this organization."),
            RuleMode.RequiresApproval => (ServiceErrorType.BadRequest, $"{featureName} require admin approval. Please submit a WorkScheduleChange request via the organization requests endpoint."),
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
            schedule.TargetSat = request.TargetSat ?? 0;
            schedule.TargetSun = request.TargetSun ?? 0;
        }

        return schedule;
    }

    public const string VacationDaysRangeMessage = "Vacation days per year must be between 0 and 365.";

    /// <summary>
    /// A new period keeps the contract terms of the one before it (holiday region, vacation days) unless an admin
    /// (<paramref name="adminRequest"/>) names them — a member's own schedule change never moves their Arbeitsort.
    /// </summary>
    public static async Task<string?> InheritContractAsync(TimeTrackingDbContext db, WorkSchedule schedule, CreateWorkScheduleRequest? adminRequest)
    {
        var previous = await db.WorkSchedules.AsNoTracking()
            .Where(s => s.UserId == schedule.UserId && s.OrganizationId == schedule.OrganizationId && s.ValidFrom < schedule.ValidFrom)
            .OrderByDescending(s => s.ValidFrom)
            .Select(s => new { s.HolidayRegion, s.VacationDaysPerYear })
            .FirstOrDefaultAsync();
        schedule.HolidayRegion = previous?.HolidayRegion;
        schedule.VacationDaysPerYear = previous?.VacationDaysPerYear;
        if (adminRequest?.HolidayRegion is { } region)
        {
            if (HolidayPresets.Normalize(region) is not { } key) return HolidayPresets.UnknownRegionMessage;
            schedule.HolidayRegion = key == "" ? null : key;
        }
        if (adminRequest?.VacationDaysPerYear is { } days)
        {
            if (!double.IsFinite(days) || days is < 0 or > 365) return VacationDaysRangeMessage;
            schedule.VacationDaysPerYear = days;
        }
        return null;
    }

    /// <summary>An admin's change of the contract terms (region, vacation days) of an existing period.</summary>
    private static string? ApplyContract(WorkSchedule schedule, UpdateWorkScheduleRequest request)
    {
        if (request.HolidayRegion != null)
        {
            if (HolidayPresets.Normalize(request.HolidayRegion) is not { } key) return HolidayPresets.UnknownRegionMessage;
            schedule.HolidayRegion = key == "" ? null : key;
        }
        if (request.ClearVacationDaysPerYear) schedule.VacationDaysPerYear = null;
        else if (request.VacationDaysPerYear is { } days)
        {
            if (!double.IsFinite(days) || days is < 0 or > 365) return VacationDaysRangeMessage;
            schedule.VacationDaysPerYear = days;
        }
        return null;
    }

    private static void ApplyUpdate(WorkSchedule schedule, UpdateWorkScheduleRequest request)
    {
        if (request.ValidFrom.HasValue) schedule.ValidFrom = request.ValidFrom.Value;
        schedule.ValidTo = NewValidTo(schedule, request);

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
                schedule.TargetSat = 0;
                schedule.TargetSun = 0;
            }
            else
            {
                schedule.TargetMon = request.TargetMon ?? schedule.TargetMon;
                schedule.TargetTue = request.TargetTue ?? schedule.TargetTue;
                schedule.TargetWed = request.TargetWed ?? schedule.TargetWed;
                schedule.TargetThu = request.TargetThu ?? schedule.TargetThu;
                schedule.TargetFri = request.TargetFri ?? schedule.TargetFri;
                schedule.TargetSat = request.TargetSat ?? schedule.TargetSat;
                schedule.TargetSun = request.TargetSun ?? schedule.TargetSun;
            }
        }
    }

    /// <summary>A missing <c>validTo</c> keeps the end; <c>clearValidTo</c> makes the period open-ended.</summary>
    private static DateOnly? NewValidTo(WorkSchedule schedule, UpdateWorkScheduleRequest request) =>
        request.ClearValidTo ? null : request.ValidTo ?? schedule.ValidTo;

    /// <summary>The edited period must still end after it starts and fit between its neighbours.</summary>
    private async Task<string?> CheckUpdateAsync(WorkSchedule schedule, UpdateWorkScheduleRequest request)
    {
        var from = request.ValidFrom ?? schedule.ValidFrom;
        var to = NewValidTo(schedule, request);
        if (to < from)
            return "The end date must not be before the start date.";
        // An edit closes nothing automatically, so an open neighbour counts as an overlap too
        return await CheckOverlapAsync(_context, schedule.UserId, schedule.OrganizationId, from, to, schedule.Id, autoClose: false);
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
                ValidFrom = OrgClock.For(org).Today(),
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
            TargetSat = schedule.TargetSat,
            TargetSun = schedule.TargetSun,
            HolidayRegion = schedule.HolidayRegion,
            VacationDaysPerYear = schedule.VacationDaysPerYear,
            InitialOvertimeHours = membership?.InitialOvertimeHours ?? 0,
            InitialOvertimeMode = org.InitialOvertimeMode.ToString(),
            WorkScheduleChangeMode = org.WorkScheduleChangeMode.ToString()
        };
    }

    private async Task<WorkSchedule?> GetCurrentScheduleAsync(int userId, Organization org)
    {
        var today = OrgClock.For(org).Today();
        return await _context.WorkSchedules
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.OrganizationId == org.Id
                && s.ValidFrom <= today
                && (s.ValidTo == null || s.ValidTo >= today))
            .OrderByDescending(s => s.ValidFrom)
            .FirstOrDefaultAsync();
    }

    private async Task<List<WorkSchedule>> GetAllSchedulesAsync(int userId, int orgId)
    {
        return await _context.WorkSchedules
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.OrganizationId == orgId)
            .OrderBy(s => s.ValidFrom)
            .ToListAsync();
    }

    /// <summary>Closes the open-ended predecessor of a new period starting on <paramref name="newFrom"/> (no save). Shared with approved schedule-change requests.</summary>
    internal static async Task CloseOpenScheduleAsync(TimeTrackingDbContext context, int userId, int orgId, DateOnly newFrom)
    {
        var openSchedule = await context.WorkSchedules
            .Where(s => s.UserId == userId && s.OrganizationId == orgId
                     && s.ValidTo == null && s.ValidFrom < newFrom)
            .OrderByDescending(s => s.ValidFrom)
            .FirstOrDefaultAsync();

        if (openSchedule != null)
            openSchedule.ValidTo = newFrom.AddDays(-1);
    }

    /// <summary>
    /// Why a period [from, to] would overlap another of the member's periods, or null. Both ends are
    /// inclusive (a shared boundary day overlaps); with <paramref name="autoClose"/> an open-ended
    /// predecessor that <see cref="CloseOpenScheduleAsync"/> will close is no overlap.
    /// Shared with approved schedule-change requests.
    /// </summary>
    internal static async Task<string?> CheckOverlapAsync(TimeTrackingDbContext context,
        int userId, int orgId, DateOnly from, DateOnly? to, int? excludeId = null, bool autoClose = true)
    {
        var existing = await context.WorkSchedules
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.OrganizationId == orgId
                     && (excludeId == null || s.Id != excludeId))
            .ToListAsync();

        foreach (var s in existing)
        {
            var sEnd = s.ValidTo ?? DateOnly.MaxValue;
            var newEnd = to ?? DateOnly.MaxValue;

            // Inclusive on both ends: one period ending on the day the next starts would give that day two targets
            if (from <= sEnd && s.ValidFrom <= newEnd)
            {
                if (autoClose && s.ValidTo == null && s.ValidFrom < from)
                    continue; // Will be auto-closed

                return $"This schedule overlaps with an existing one ({s.ValidFrom:yyyy-MM-dd} – " +
                       $"{(s.ValidTo.HasValue ? s.ValidTo.Value.ToString("yyyy-MM-dd") : "ongoing")}). " +
                       "Only one schedule at a time is allowed.";
            }
        }
        return null;
    }

    private async Task<UserOrganization?> GetMembershipAsync(int userId, int orgId)
    {
        return await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == orgId && uo.UserId == userId);
    }

}
