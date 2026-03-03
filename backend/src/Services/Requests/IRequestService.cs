using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Handles organization requests: create, list, respond, and user/admin notifications.
/// </summary>
public interface IRequestService
{
    Task<ServiceResult<OrgRequestResponse>> CreateRequestAsync(string slug, int userId, CreateOrgRequestRequest request);
    Task<ServiceResult<PaginatedResponse<OrgRequestResponse>>> GetRequestsAsync(string slug, int callerUserId, RequestType? type, RequestStatus? status, int limit, int offset);
    Task<ServiceResult<OrgRequestResponse>> RespondToRequestAsync(string slug, int callerUserId, int requestId, RespondToOrgRequestRequest request);
    Task<ServiceResult<PaginatedResponse<OrgRequestResponse>>> GetMyRequestsAsync(int userId, RequestType? type, int limit, int offset);
    Task<ServiceResult<AdminNotificationResponse>> GetAdminNotificationsAsync(int userId);
    Task<ServiceResult<UserNotificationResponse>> GetUserNotificationsAsync(int userId);
    Task<ServiceResult<object>> MarkNotificationsSeenAsync(int userId, List<int>? requestIds);
}
