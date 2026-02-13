using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Notifications.Entities;

namespace saas.Modules.Notifications.Services;

public interface INotificationService
{
    Task SendAsync(string userId, string title, string? message = null, string? url = null, NotificationType type = NotificationType.Info);
    Task<List<Notification>> GetRecentAsync(string userId, int count = 10);
    Task<int> GetUnreadCountAsync(string userId);
    Task MarkAsReadAsync(Guid notificationId, string userId);
    Task MarkAllAsReadAsync(string userId);
}

public class NotificationService : INotificationService
{
    private readonly TenantDbContext _db;

    public NotificationService(TenantDbContext db)
    {
        _db = db;
    }

    public async Task SendAsync(string userId, string title, string? message = null, string? url = null, NotificationType type = NotificationType.Info)
    {
        _db.Set<Notification>().Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Message = message,
            Url = url,
            Type = type,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
    }

    public async Task<List<Notification>> GetRecentAsync(string userId, int count = 10)
    {
        return await _db.Set<Notification>()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<int> GetUnreadCountAsync(string userId)
    {
        return await _db.Set<Notification>()
            .CountAsync(n => n.UserId == userId && !n.IsRead);
    }

    public async Task MarkAsReadAsync(Guid notificationId, string userId)
    {
        var notification = await _db.Set<Notification>()
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);

        if (notification is not null)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    public async Task MarkAllAsReadAsync(string userId)
    {
        var unread = await _db.Set<Notification>()
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }
}
