using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Handles organization CRUD, member management, settings, and time overview.
/// </summary>
public interface IOrganizationService
{
    // ── Organization CRUD ──
    Task<ServiceResult<PaginatedResponse<OrganizationResponse>>> GetOrganizationsAsync(int limit, int offset);
    Task<ServiceResult<OrganizationDetailResponse>> GetOrganizationAsync(string slug, int? callerUserId);
    Task<ServiceResult<List<UserOrganizationResponse>>> GetUserOrganizationsAsync(int callerUserId);
    Task<ServiceResult<OrganizationResponse>> CreateOrganizationAsync(int callerUserId, CreateOrganizationRequest request);
    Task<ServiceResult<OrganizationResponse>> UpdateOrganizationAsync(string slug, int callerUserId, UpdateOrganizationRequest request);
    Task<ServiceResult> DeleteOrganizationAsync(string slug, int callerUserId);

    // ── Member management ──
    Task<ServiceResult<OrganizationMemberResponse>> AddMemberAsync(string slug, int callerUserId, AddMemberRequest request);
    Task<ServiceResult<OrganizationMemberResponse>> UpdateMemberRoleAsync(string slug, int callerUserId, int userId, UpdateMemberRoleRequest request);
    Task<ServiceResult> RemoveMemberAsync(string slug, int callerUserId, int userId);

    // ── Settings ──
    Task<ServiceResult<object>> UpdateSettingsAsync(string slug, int callerUserId, UpdateOrganizationSettingsRequest request);

    // ── Time overview (admin) ──
    Task<ServiceResult<List<MemberTimeOverviewResponse>>> GetTimeOverviewAsync(string slug, int callerUserId, DateTime? from, DateTime? to);
    Task<ServiceResult<PaginatedResponse<object>>> GetMemberEntriesAsync(string slug, int callerUserId, int userId, DateTime? from, DateTime? to, int limit, int offset);

    // ── Initial overtime (admin) ──
    Task<ServiceResult<object>> SetMemberInitialOvertimeAsync(string slug, int callerUserId, int userId, SetInitialOvertimeRequest request);
}
