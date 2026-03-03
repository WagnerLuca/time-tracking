using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public class NotificationService : INotificationService
{
    private readonly TimeTrackingDbContext _context;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(TimeTrackingDbContext context, ILogger<NotificationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ServiceResult<PaginatedResponse<object>>> GetNotificationsAsync(int userId, bool unreadOnly, int limit, int offset)
    {
        (limit, offset) = PaginationDefaults.Normalize(limit, offset);

        var query = _context.Notifications
            .Where(n => n.UserId == userId)
            .AsQueryable();

        if (unreadOnly)
            query = query.Where(n => !n.IsRead);

        var totalCount = await query.CountAsync();

        var notifications = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .Select(n => (object)new
            {
                n.Id,
                n.Title,
                n.Message,
                n.Type,
                n.IsRead,
                n.CreatedAt,
                n.OrganizationId
            })
            .ToListAsync();

        return ServiceResult.Ok(new PaginatedResponse<object>
        {
            Items = notifications,
            TotalCount = totalCount,
            Limit = limit,
            Offset = offset
        });
    }

    public async Task<ServiceResult<object>> GetUnreadCountAsync(int userId)
    {
        var count = await _context.Notifications
            .CountAsync(n => n.UserId == userId && !n.IsRead);

        return ServiceResult.Ok<object>(new { count });
    }

    public async Task<ServiceResult> MarkAsReadAsync(int userId, int notificationId)
    {
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);

        if (notification == null)
            return ServiceResult.NotFound("Notification not found");

        notification.IsRead = true;
        await _context.SaveChangesAsync();
        _logger.LogInformation("Notification {NotificationId} marked as read by user {UserId}", notificationId, userId);

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> MarkAllAsReadAsync(int userId)
    {
        var unread = await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        foreach (var n in unread)
            n.IsRead = true;

        await _context.SaveChangesAsync();
        _logger.LogInformation("All notifications marked as read for user {UserId}", userId);
        return ServiceResult.Ok();
    }
}
