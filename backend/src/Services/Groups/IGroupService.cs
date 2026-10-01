using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public interface IGroupService
{
    Task<ServiceResult<List<GroupResponse>>> GetGroupsAsync(string slug, int callerUserId);
    Task<ServiceResult<GroupDetailResponse>> GetGroupAsync(string slug, int groupId, int callerUserId);
    Task<ServiceResult<GroupDetailResponse>> CreateGroupAsync(string slug, int callerUserId, CreateGroupRequest request);
    Task<ServiceResult<GroupDetailResponse>> UpdateGroupAsync(string slug, int groupId, int callerUserId, UpdateGroupRequest request);
    Task<ServiceResult> DeleteGroupAsync(string slug, int groupId, int callerUserId);
    Task<ServiceResult<GroupMemberResponse>> AddMemberAsync(string slug, int groupId, int callerUserId, AddGroupMemberRequest request);
    Task<ServiceResult<GroupMemberResponse>> UpdateMemberRoleAsync(string slug, int groupId, int targetUserId, int callerUserId, UpdateGroupMemberRoleRequest request);
    Task<ServiceResult> RemoveMemberAsync(string slug, int groupId, int targetUserId, int callerUserId);
    Task<ServiceResult<GroupPermissionsResponse>> GetPermissionsAsync(string slug, int groupId, int callerUserId);
    /// <summary>Permission overrides of every group of the organization in one call (same access as a single group's).</summary>
    Task<ServiceResult<List<GroupPermissionsResponse>>> GetAllPermissionsAsync(string slug, int callerUserId);
    Task<ServiceResult<GroupPermissionsResponse>> UpdatePermissionsAsync(string slug, int groupId, int callerUserId, UpdateGroupPermissionsRequest request);
    Task<ServiceResult<EffectivePermissionsResponse>> GetEffectivePermissionsAsync(string slug, int callerUserId, int? targetUserId = null);
}
