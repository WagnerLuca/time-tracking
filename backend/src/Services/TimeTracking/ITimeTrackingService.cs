using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Handles starting, stopping, querying, editing, and deleting time entries.
/// </summary>
public interface ITimeTrackingService
{
    Task<ServiceResult<TimeEntryResponse>> StartAsync(int userId, StartTimeEntryRequest? request);
    Task<ServiceResult<TimeEntryResponse>> StopAsync(int userId, StopTimeEntryRequest? request);
    Task<ServiceResult<TimeEntryResponse>> GetCurrentAsync(int userId);
    Task<ServiceResult<PaginatedResponse<TimeEntryResponse>>> GetHistoryAsync(int userId, int? organizationId, DateTime? from, DateTime? to, int limit, int offset);
    Task<ServiceResult<TimeEntryResponse>> UpdateAsync(int userId, int entryId, UpdateTimeEntryRequest request);
    Task<ServiceResult> DeleteAsync(int userId, int entryId);
}
