using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Handles organization CRUD, member management, settings, and time overview.
/// </summary>
public interface IOrganizationService
{
    // ── Organization CRUD ──
    Task<ServiceResult<PaginatedResponse<OrganizationResponse>>> GetOrganizationsAsync(int limit, int offset, int? callerUserId = null);
    Task<ServiceResult<OrganizationDetailResponse>> GetOrganizationAsync(string slug, int? callerUserId);
    Task<ServiceResult<List<UserOrganizationResponse>>> GetUserOrganizationsAsync(int callerUserId);
    Task<ServiceResult<OrganizationResponse>> CreateOrganizationAsync(int callerUserId, CreateOrganizationRequest request);
    Task<ServiceResult<OrganizationResponse>> UpdateOrganizationAsync(string slug, int callerUserId, UpdateOrganizationRequest request);
    Task<ServiceResult> DeleteOrganizationAsync(string slug, int callerUserId);

    // ── Member management ──
    Task<ServiceResult<OrganizationMemberResponse>> UpdateMemberRoleAsync(string slug, int callerUserId, int userId, UpdateMemberRoleRequest request);
    Task<ServiceResult> RemoveMemberAsync(string slug, int callerUserId, int userId);

    // ── Settings ──
    Task<ServiceResult<OrganizationSettingsResponse>> UpdateSettingsAsync(string slug, int callerUserId, UpdateOrganizationSettingsRequest request);

    // ── Team overview (dashboard) ──
    Task<ServiceResult<TeamOverviewResponse>> GetTeamOverviewAsync(string slug, int callerUserId);
    Task<ServiceResult<OrgChartResponse>> GetOrgChartAsync(string slug, int callerUserId);

    // ── Time overview (admin) ──
    Task<ServiceResult<List<MemberTimeOverviewResponse>>> GetTimeOverviewAsync(string slug, int callerUserId, DateTime? from, DateTime? to);
    Task<ServiceResult<PaginatedResponse<TimeEntryResponse>>> GetMemberEntriesAsync(string slug, int callerUserId, int userId, DateTime? from, DateTime? to, int limit, int offset);

    // ── Initial overtime (admin) ──
    Task<ServiceResult<MemberInitialOvertimeResponse>> SetMemberInitialOvertimeAsync(string slug, int callerUserId, int userId, SetInitialOvertimeRequest request);

    // ── Vacation days (admin) ──
    Task<ServiceResult<MemberPersonnelNumberResponse>> SetMemberPersonnelNumberAsync(string slug, int callerUserId, int userId, SetPersonnelNumberRequest request);
    Task<ServiceResult<MemberAccountantResponse>> SetMemberAccountantAsync(string slug, int callerUserId, int userId, SetAccountantRequest request);
    Task<ServiceResult<MemberVacationDaysResponse>> SetMemberVacationDaysAsync(string slug, int callerUserId, int userId, SetVacationDaysRequest request);
}
