using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;

namespace TimeTracking.Api.Services;

public class NotificationService : INotificationService
{
    private readonly TimeTrackingDbContext _context;

    public NotificationService(TimeTrackingDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceResult<List<object>>> GetNotificationsAsync(int userId, bool unreadOnly)
    {
        var query = _context.Notifications
            .Where(n => n.UserId == userId)
            .AsQueryable();

        if (unreadOnly)
            query = query.Where(n => !n.IsRead);

        var notifications = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
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

        return ServiceResult.Ok(notifications);
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

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> MarkAllAsReadAsync(int userId)
    {
        await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(n => n.SetProperty(x => x.IsRead, true));

        return ServiceResult.Ok();
    }
}
