using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Handles work schedule CRUD for self and admin-managed members.
/// </summary>
public interface IWorkScheduleService
{
    // ── Self-service ──
    Task<ServiceResult<WorkScheduleResponse>> GetMyWorkScheduleAsync(string slug, int userId);
    Task<ServiceResult<List<WorkScheduleResponse>>> GetMyWorkSchedulesAsync(string slug, int userId);
    Task<ServiceResult<WorkScheduleResponse>> CreateMyWorkScheduleAsync(string slug, int userId, CreateWorkScheduleRequest request);
    Task<ServiceResult<WorkScheduleResponse>> UpdateMyWorkScheduleAsync(string slug, int userId, int scheduleId, UpdateWorkScheduleRequest request);
    Task<ServiceResult> DeleteMyWorkScheduleAsync(string slug, int userId, int scheduleId);
    Task<ServiceResult<object>> UpdateMyInitialOvertimeAsync(string slug, int userId, SetInitialOvertimeRequest request);

    // ── Admin-managed ──
    Task<ServiceResult<WorkScheduleResponse>> GetMemberWorkScheduleAsync(string slug, int callerUserId, int memberId);
    Task<ServiceResult<List<WorkScheduleResponse>>> GetMemberWorkSchedulesAsync(string slug, int callerUserId, int memberId);
    Task<ServiceResult<WorkScheduleResponse>> CreateMemberWorkScheduleAsync(string slug, int callerUserId, int memberId, CreateWorkScheduleRequest request);
    Task<ServiceResult<WorkScheduleResponse>> UpdateMemberWorkScheduleAsync(string slug, int callerUserId, int memberId, int scheduleId, UpdateWorkScheduleRequest request);
    Task<ServiceResult> DeleteMemberWorkScheduleAsync(string slug, int callerUserId, int memberId, int scheduleId);
}
