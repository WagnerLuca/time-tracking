using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Handles user notification CRUD and read-status management.
/// </summary>
public interface INotificationService
{
    Task<ServiceResult<PaginatedResponse<object>>> GetNotificationsAsync(int userId, bool unreadOnly, int limit, int offset);
    Task<ServiceResult<object>> GetUnreadCountAsync(int userId);
    Task<ServiceResult> MarkAsReadAsync(int userId, int notificationId);
    Task<ServiceResult> MarkAllAsReadAsync(int userId);
}
