using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Handles organization holiday CRUD and preset imports.
/// </summary>
public interface IHolidayService
{
    Task<ServiceResult<List<HolidayResponse>>> GetHolidaysAsync(string slug, int callerUserId);
    Task<ServiceResult<HolidayResponse>> CreateHolidayAsync(string slug, int callerUserId, CreateHolidayRequest request);
    Task<ServiceResult<HolidayResponse>> UpdateHolidayAsync(string slug, int callerUserId, int holidayId, UpdateHolidayRequest request);
    Task<ServiceResult> DeleteHolidayAsync(string slug, int callerUserId, int holidayId);
    Task<ServiceResult<List<HolidayResponse>>> ImportPresetHolidaysAsync(string slug, int callerUserId, string preset, int? year);
    List<object> GetAvailablePresets();
}
