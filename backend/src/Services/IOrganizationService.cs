using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Handles organization CRUD, member management, settings, and time overview.
/// </summary>
public interface IOrganizationService
{
    // ── Organization CRUD ──
    Task<ServiceResult<List<OrganizationResponse>>> GetOrganizationsAsync();
    Task<ServiceResult<OrganizationDetailResponse>> GetOrganizationAsync(string slug);
    Task<ServiceResult<List<UserOrganizationResponse>>> GetUserOrganizationsAsync(int userId);
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
    Task<ServiceResult<List<object>>> GetMemberEntriesAsync(string slug, int callerUserId, int userId, DateTime? from, DateTime? to);

    // ── Initial overtime (admin) ──
    Task<ServiceResult<object>> SetMemberInitialOvertimeAsync(string slug, int callerUserId, int userId, SetInitialOvertimeRequest request);
}
