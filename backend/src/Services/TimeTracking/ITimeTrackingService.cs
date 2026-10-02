using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Handles starting, stopping, querying, editing, and deleting time entries.
/// </summary>
public interface ITimeTrackingService
{
    /// <summary>
    /// Starts a timer now, or at <paramref name="atUtc"/> (a terminal's queued scan: validated with
    /// <see cref="EntryTimes"/> and against overlapping entries).
    /// </summary>
    Task<ServiceResult<TimeEntryResponse>> StartAsync(int userId, StartTimeEntryRequest? request, DateTime? atUtc = null, TimeEntrySource source = TimeEntrySource.App);
    /// <summary>Stops the running timer now, or at <paramref name="atUtc"/> (validated with <see cref="EntryTimes"/>).</summary>
    Task<ServiceResult<TimeEntryResponse>> StopAsync(int userId, StopTimeEntryRequest? request, DateTime? atUtc = null);
    Task<ServiceResult<TimeEntryResponse>> GetCurrentAsync(int userId);
    /// <summary>The caller's unresolved failure entries (auto-stopped at business day end), newest first.</summary>
    Task<ServiceResult<List<TimeEntryResponse>>> GetFailuresAsync(int userId);
    Task<ServiceResult<PaginatedResponse<TimeEntryResponse>>> GetHistoryAsync(int userId, int? organizationId, DateTime? from, DateTime? to, int limit, int offset);
    Task<ServiceResult<TimeEntryResponse>> UpdateAsync(int userId, int entryId, UpdateTimeEntryRequest request);
    Task<ServiceResult> DeleteAsync(int userId, int entryId);
    /// <summary>Adds a finished entry by hand: directly, or as an AddManualEntry request when the org requires approval.</summary>
    Task<ServiceResult<ManualEntryResponse>> CreateManualAsync(int userId, CreateManualEntryRequest request);
    /// <summary>Edit history of an entry, for its owner and for admins who may see the owner's entries.</summary>
    Task<ServiceResult<List<TimeEntryChangeResponse>>> GetEntryHistoryAsync(int callerUserId, int entryId);
}
