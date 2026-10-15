using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Handles absence day CRUD for members and admins.
/// </summary>
public interface IAbsenceDayService
{
    Task<ServiceResult<PaginatedResponse<AbsenceDayResponse>>> GetAbsencesAsync(string slug, int callerUserId, int? userId, DateOnly? from, DateOnly? to, int limit, int offset, int? groupId = null);
    Task<ServiceResult<AbsenceDayResponse>> CreateAbsenceAsync(string slug, int userId, CreateAbsenceDayRequest request);
    Task<ServiceResult<AbsenceDayResponse>> AdminCreateAbsenceAsync(string slug, int callerUserId, AdminCreateAbsenceDayRequest request);
    /// <summary>All weekdays of a range, all or nothing (own absences, self-service rule applies).</summary>
    Task<ServiceResult<List<AbsenceDayResponse>>> CreateAbsenceRangeAsync(string slug, int userId, CreateAbsenceRangeRequest request);
    /// <summary>All weekdays of a range for a member, all or nothing (admins).</summary>
    Task<ServiceResult<List<AbsenceDayResponse>>> AdminCreateAbsenceRangeAsync(string slug, int callerUserId, CreateAbsenceRangeRequest request);
    Task<ServiceResult> DeleteAbsenceAsync(string slug, int callerUserId, int absenceId);

    // Custom absence type definitions
    Task<ServiceResult<List<AbsenceTypeDefinitionResponse>>> GetAbsenceTypesAsync(string slug, int callerUserId);
    Task<ServiceResult<AbsenceTypeDefinitionResponse>> CreateAbsenceTypeAsync(string slug, int callerUserId, UpsertAbsenceTypeDefinitionRequest request);
    Task<ServiceResult<AbsenceTypeDefinitionResponse>> UpdateAbsenceTypeAsync(string slug, int callerUserId, int typeId, UpsertAbsenceTypeDefinitionRequest request);
    Task<ServiceResult> DeleteAbsenceTypeAsync(string slug, int callerUserId, int typeId);
}
