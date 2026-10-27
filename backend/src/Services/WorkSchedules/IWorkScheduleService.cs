using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>Work schedules of oneself and of members (issue #82, <see cref="WorkSchedulePlan"/>).</summary>
public interface IWorkScheduleService
{
    // ── Self-service ──
    Task<ServiceResult<WorkScheduleResponse>> GetMyWorkScheduleAsync(string slug, int userId);
    Task<ServiceResult<List<WorkScheduleResponse>>> GetMyWorkSchedulesAsync(string slug, int userId);
    Task<ServiceResult<WorkSchedulePlanResponse>> GetMyPlanAsync(string slug, int userId);
    Task<ServiceResult<WorkSchedulePlanResponse>> ChangeMyWorkScheduleAsync(string slug, int userId, ChangeWorkScheduleRequest request);
    Task<ServiceResult<WorkSchedulePlanResponse>> CancelMyFutureAsync(string slug, int userId);
    Task<ServiceResult<MemberInitialOvertimeResponse>> UpdateMyInitialOvertimeAsync(string slug, int userId, SetInitialOvertimeRequest request);

    // ── A member's ──
    Task<ServiceResult<WorkScheduleResponse>> GetMemberWorkScheduleAsync(string slug, int callerUserId, int memberId);
    Task<ServiceResult<List<WorkScheduleResponse>>> GetMemberWorkSchedulesAsync(string slug, int callerUserId, int memberId);
    Task<ServiceResult<WorkSchedulePlanResponse>> GetMemberPlanAsync(string slug, int callerUserId, int memberId);
    Task<ServiceResult<WorkSchedulePlanResponse>> ChangeMemberWorkScheduleAsync(string slug, int callerUserId, int memberId, ChangeWorkScheduleRequest request);
    Task<ServiceResult<WorkSchedulePlanResponse>> CorrectMemberWorkScheduleAsync(string slug, int callerUserId, int memberId, CorrectWorkScheduleRequest request);
    Task<ServiceResult<WorkSchedulePlanResponse>> CancelMemberFutureAsync(string slug, int callerUserId, int memberId);
}
