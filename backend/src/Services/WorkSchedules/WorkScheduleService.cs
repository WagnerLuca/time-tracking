using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Work schedules of a member (issue #82, <see cref="WorkSchedulePlan"/>): read the current one, all periods (calendars) or the
/// plan (current / future / history); change from a day, cancel the future one; admins also correct the current one.
/// </summary>
public class WorkScheduleService : IWorkScheduleService
{
    public const string StartBeforeToday = "A new schedule can start today at the earliest.";
    public const string StartInClosedMonth = "The new schedule would start in a closed month.";

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
        return ServiceResult.Ok(schedules.Select(s => MapToResponse(s, userId, org, membership)).ToList());
    }

    public async Task<ServiceResult<WorkSchedulePlanResponse>> GetMyPlanAsync(string slug, int userId)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<WorkSchedulePlanResponse>("Organization not found");
        var membership = await GetMembershipAsync(userId, org.Id);
        if (membership == null)
            return ServiceResult.Forbidden<WorkSchedulePlanResponse>();
        return ServiceResult.Ok(await PlanAsync(userId, org, membership));
    }

    /// <summary>"Ändern ab …" for oneself: only while the effective rule allows it (else 403 / request); members from today on,
    /// admins from any open day.</summary>
    public async Task<ServiceResult<WorkSchedulePlanResponse>> ChangeMyWorkScheduleAsync(string slug, int userId, ChangeWorkScheduleRequest request)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<WorkSchedulePlanResponse>("Organization not found");
        var membership = await GetMembershipAsync(userId, org.Id);
        if (membership == null)
            return ServiceResult.Forbidden<WorkSchedulePlanResponse>();
        if (await RuleErrorAsync(org, userId) is { } ruleError)
            return ServiceResult.Fail<WorkSchedulePlanResponse>(ruleError.Type, ruleError.Message);

        var today = OrgClock.For(org).Today();
        var from = request.ValidFrom ?? today;
        // Admins start their own on any open day too, members today at the earliest
        if (membership.Role >= OrganizationRole.Admin)
        {
            if (ClosedPeriods.FirstClosed(await ClosedPeriods.MonthsAsync(_context, org.Id), from, from) != null)
                return ServiceResult.Conflict<WorkSchedulePlanResponse>(StartInClosedMonth, "schedule.startInClosedMonth");
        }
        else if (from < today)
            return ServiceResult.BadRequest<WorkSchedulePlanResponse>(StartBeforeToday, "schedule.startBeforeToday");
        return await ChangeAsync(org, userId, membership, from, request, request.ReplaceFuture, today, $"by user {userId}");
    }

    /// <summary>Cancels one's own future schedule (only while the rule allows changes).</summary>
    public async Task<ServiceResult<WorkSchedulePlanResponse>> CancelMyFutureAsync(string slug, int userId)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<WorkSchedulePlanResponse>("Organization not found");
        var membership = await GetMembershipAsync(userId, org.Id);
        if (membership == null)
            return ServiceResult.Forbidden<WorkSchedulePlanResponse>();
        if (await RuleErrorAsync(org, userId) is { } ruleError)
            return ServiceResult.Fail<WorkSchedulePlanResponse>(ruleError.Type, ruleError.Message);
        return await CancelFutureAsync(org, userId, membership);
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

    public async Task<ServiceResult<WorkScheduleResponse>> GetMemberWorkScheduleAsync(string slug, int callerUserId, int memberId)
    {
        var (org, membership, error) = await VisibleMemberAsync(slug, callerUserId, memberId);
        if (error != null) return ServiceResult.Fail<WorkScheduleResponse>(error.Value.Type, error.Value.Message);
        var schedule = await GetCurrentScheduleAsync(memberId, org!);
        return ServiceResult.Ok(MapToResponse(schedule, memberId, org!, membership));
    }

    public async Task<ServiceResult<List<WorkScheduleResponse>>> GetMemberWorkSchedulesAsync(string slug, int callerUserId, int memberId)
    {
        var (org, membership, error) = await VisibleMemberAsync(slug, callerUserId, memberId);
        if (error != null) return ServiceResult.Fail<List<WorkScheduleResponse>>(error.Value.Type, error.Value.Message);
        var schedules = await GetAllSchedulesAsync(memberId, org!.Id);
        return ServiceResult.Ok(schedules.Select(s => MapToResponse(s, memberId, org, membership)).ToList());
    }

    public async Task<ServiceResult<WorkSchedulePlanResponse>> GetMemberPlanAsync(string slug, int callerUserId, int memberId)
    {
        var (org, membership, error) = await VisibleMemberAsync(slug, callerUserId, memberId);
        if (error != null) return ServiceResult.Fail<WorkSchedulePlanResponse>(error.Value.Type, error.Value.Message);
        return ServiceResult.Ok(await PlanAsync(memberId, org!, membership));
    }

    /// <summary>An admin's "Ändern ab …": from any open day (not in a closed month).</summary>
    public async Task<ServiceResult<WorkSchedulePlanResponse>> ChangeMemberWorkScheduleAsync(
        string slug, int callerUserId, int memberId, ChangeWorkScheduleRequest request)
    {
        var (org, membership, error) = await AdministeredMemberAsync(slug, callerUserId, memberId);
        if (error != null) return ServiceResult.Fail<WorkSchedulePlanResponse>(error.Value.Type, error.Value.Message);
        var today = OrgClock.For(org).Today();
        var from = request.ValidFrom ?? today;
        if (ClosedPeriods.FirstClosed(await ClosedPeriods.MonthsAsync(_context, org!.Id), from, from) != null)
            return ServiceResult.Conflict<WorkSchedulePlanResponse>(StartInClosedMonth, "schedule.startInClosedMonth");
        return await ChangeAsync(org, memberId, membership!, from, request, request.ReplaceFuture, today, $"by admin {callerUserId}");
    }

    /// <summary>An admin's correction of the current schedule (typos): never into a closed month.</summary>
    public async Task<ServiceResult<WorkSchedulePlanResponse>> CorrectMemberWorkScheduleAsync(
        string slug, int callerUserId, int memberId, CorrectWorkScheduleRequest request)
    {
        var (org, membership, error) = await AdministeredMemberAsync(slug, callerUserId, memberId);
        if (error != null) return ServiceResult.Fail<WorkSchedulePlanResponse>(error.Value.Type, error.Value.Message);
        var refusal = await WorkSchedulePlan.StageCorrectionAsync(_context, memberId, org!.Id, request.ValidFrom, request,
            OrgClock.For(org).Today(), await ClosedPeriods.MonthsAsync(_context, org.Id));
        if (refusal != null) return ServiceResult.Fail<WorkSchedulePlanResponse>(refusal.Type, refusal.Message, refusal.Code);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Current work schedule of member {MemberId} in org {OrgId} corrected by admin {CallerId}", memberId, org.Id, callerUserId);
        return ServiceResult.Ok(await PlanAsync(memberId, org, membership!));
    }

    public async Task<ServiceResult<WorkSchedulePlanResponse>> CancelMemberFutureAsync(string slug, int callerUserId, int memberId)
    {
        var (org, membership, error) = await AdministeredMemberAsync(slug, callerUserId, memberId);
        if (error != null) return ServiceResult.Fail<WorkSchedulePlanResponse>(error.Value.Type, error.Value.Message);
        return await CancelFutureAsync(org!, memberId, membership!);
    }

    // ────────────────────────────────────────────────────
    //  Private helpers
    // ────────────────────────────────────────────────────

    private async Task<ServiceResult<WorkSchedulePlanResponse>> ChangeAsync(Organization org, int userId, UserOrganization membership,
        DateOnly from, ScheduleTargets targets, bool replaceFuture, DateOnly today, string by)
    {
        var refusal = await WorkSchedulePlan.StageChangeAsync(_context, userId, org.Id, from, targets, replaceFuture, today);
        if (refusal != null) return ServiceResult.Fail<WorkSchedulePlanResponse>(refusal.Type, refusal.Message, refusal.Code);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Work schedule of user {UserId} in org {OrgId} changed from {From} {By}", userId, org.Id, from, by);
        return ServiceResult.Ok(await PlanAsync(userId, org, membership));
    }

    private async Task<ServiceResult<WorkSchedulePlanResponse>> CancelFutureAsync(Organization org, int userId, UserOrganization membership)
    {
        var refusal = await WorkSchedulePlan.StageCancelFutureAsync(_context, userId, org.Id, OrgClock.For(org).Today());
        if (refusal != null) return ServiceResult.Fail<WorkSchedulePlanResponse>(refusal.Type, refusal.Message, refusal.Code);
        await _context.SaveChangesAsync();
        return ServiceResult.Ok(await PlanAsync(userId, org, membership));
    }

    private async Task<WorkSchedulePlanResponse> PlanAsync(int userId, Organization org, UserOrganization? membership)
    {
        var today = OrgClock.For(org).Today();
        var state = WorkSchedulePlan.Split(await GetAllSchedulesAsync(userId, org.Id), today);
        var closedMonths = state.Current == null ? [] : await ClosedPeriods.MonthsAsync(_context, org.Id);
        return new WorkSchedulePlanResponse
        {
            Current = state.Current == null ? null : MapToResponse(state.Current, userId, org, membership),
            Future = state.Future == null ? null : MapToResponse(state.Future, userId, org, membership),
            History = state.History.Select(s => MapToResponse(s, userId, org, membership)).ToList(),
            CurrentInClosedMonth = state.Current != null
                && ClosedPeriods.FirstClosed(closedMonths, state.Current.ValidFrom, state.Current.ValidTo) != null,
            InitialOvertimeHours = membership?.InitialOvertimeHours ?? 0,
            InitialOvertimeMode = org.InitialOvertimeMode.ToString(),
            WorkScheduleChangeMode = org.WorkScheduleChangeMode.ToString()
        };
    }

    private async Task<(ServiceErrorType Type, string Message)?> RuleErrorAsync(Organization org, int userId) =>
        await EffectiveRuleResolver.ResolveAsync(_context, org, userId, RequestType.WorkScheduleChange) switch
        {
            RuleMode.Disabled => (ServiceErrorType.Forbidden, "Schedule changes are disabled for this organization."),
            RuleMode.RequiresApproval => (ServiceErrorType.BadRequest, "Schedule changes require admin approval. Please submit a WorkScheduleChange request via the organization requests endpoint."),
            _ => null
        };

    /// <summary>Org, membership (null for callers who may not see the initial overtime) — admins, the member, group admin rights and the visibility matrix (kind WorkSchedule).</summary>
    private async Task<(Organization? Org, UserOrganization? Membership, (ServiceErrorType Type, string Message)? Error)> VisibleMemberAsync(
        string slug, int callerUserId, int memberId)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null) return (null, null, (ServiceErrorType.NotFound, "Organization not found"));
        var visibility = await MemberVisibility.ForCallerAsync(_context, org, callerUserId);
        switch (visibility.Check(memberId, MemberInfo.WorkSchedule))
        {
            case ServiceErrorType.NotFound:
                return (org, null, (ServiceErrorType.NotFound, "Member not found in this organization."));
            case ServiceErrorType.Forbidden:
                return (org, null, (ServiceErrorType.Forbidden, "Forbidden"));
        }
        var membership = await GetMembershipAsync(memberId, org.Id);
        if (membership == null) return (org, null, (ServiceErrorType.NotFound, "Member not found in this organization."));
        // The starting overtime balance is payroll data: the person and admins only
        return (org, !visibility.CallerIsOrgAdmin && memberId != callerUserId ? null : membership, null);
    }

    private async Task<(Organization? Org, UserOrganization? Membership, (ServiceErrorType Type, string Message)? Error)> AdministeredMemberAsync(
        string slug, int callerUserId, int memberId)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null) return (null, null, (ServiceErrorType.NotFound, "Organization not found"));
        if (await _context.GetOrgRoleAsync(callerUserId, org.Id) is not >= OrganizationRole.Admin)
            return (org, null, (ServiceErrorType.Forbidden, "Forbidden"));
        var membership = await GetMembershipAsync(memberId, org.Id);
        return membership == null ? (org, null, (ServiceErrorType.NotFound, "Member not found")) : (org, membership, null);
    }

    private static WorkScheduleResponse MapToResponse(WorkSchedule? schedule, int userId, Organization org, UserOrganization? membership)
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

    private async Task<List<WorkSchedule>> GetAllSchedulesAsync(int userId, int orgId) =>
        await _context.WorkSchedules
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.OrganizationId == orgId)
            .OrderBy(s => s.ValidFrom)
            .ToListAsync();

    private async Task<UserOrganization?> GetMembershipAsync(int userId, int orgId) =>
        await _context.UserOrganizations.FirstOrDefaultAsync(uo => uo.OrganizationId == orgId && uo.UserId == userId);
}
