using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Handles absence day CRUD for members and admins.
/// </summary>
public interface IAbsenceDayService
{
    Task<ServiceResult<List<AbsenceDayResponse>>> GetAbsencesAsync(string slug, int callerUserId, int? userId, DateOnly? from, DateOnly? to);
    Task<ServiceResult<AbsenceDayResponse>> CreateAbsenceAsync(string slug, int userId, CreateAbsenceDayRequest request);
    Task<ServiceResult<AbsenceDayResponse>> AdminCreateAbsenceAsync(string slug, int callerUserId, AdminCreateAbsenceDayRequest request);
    Task<ServiceResult> DeleteAbsenceAsync(string slug, int callerUserId, int absenceId);
}
