using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Company holidays (organization or group), their exclusions, the members' calculated days off and preset imports.
/// </summary>
public interface IHolidayService
{
    Task<ServiceResult<List<HolidayResponse>>> GetHolidaysAsync(string slug, int callerUserId);
    Task<ServiceResult<HolidayResponse>> CreateHolidayAsync(string slug, int callerUserId, CreateHolidayRequest request);
    Task<ServiceResult<HolidayResponse>> UpdateHolidayAsync(string slug, int callerUserId, int holidayId, UpdateHolidayRequest request);
    Task<ServiceResult> DeleteHolidayAsync(string slug, int callerUserId, int holidayId);
    Task<ServiceResult<HolidayResponse>> SetExclusionsAsync(string slug, int callerUserId, int holidayId, SetExclusionsRequest request);
    Task<ServiceResult<MemberDaysOffResponse>> GetMemberDaysOffAsync(string slug, int callerUserId, int? userId, DateOnly from, DateOnly to);
    ServiceResult<List<PublicHolidayResponse>> GetPublicHolidays(string region, int year);
    Task<ServiceResult<int>> RemoveCoveredHolidaysAsync(string slug, int callerUserId);
    Task<ServiceResult<List<HolidayResponse>>> ImportPresetHolidaysAsync(string slug, int callerUserId, string preset, int? year, int? toYear = null);
    List<HolidayPresetResponse> GetAvailablePresets();
}
