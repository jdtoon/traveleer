using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Notifications.Entities;
using saas.Modules.Notifications.Services;
using Xunit;

namespace saas.Tests.Modules.Notifications;

public class NotificationServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TenantDbContext _db = null!;
    private NotificationService _service = null!;

    private const string UserId = "user-1";

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TenantDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _service = new NotificationService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task SendAsync_CreatesNotification()
    {
        await _service.SendAsync(UserId, "Test Title", "Test message", "/test", NotificationType.Info);

        var notifications = await _db.Set<Notification>().ToListAsync();
        Assert.Single(notifications);
        Assert.Equal("Test Title", notifications[0].Title);
        Assert.Equal("Test message", notifications[0].Message);
        Assert.Equal("/test", notifications[0].Url);
        Assert.Equal(NotificationType.Info, notifications[0].Type);
        Assert.False(notifications[0].IsRead);
        Assert.Equal(UserId, notifications[0].UserId);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsOrderedNotifications()
    {
        await _service.SendAsync(UserId, "First");
        await _service.SendAsync(UserId, "Second");
        await _service.SendAsync(UserId, "Third");

        var recent = await _service.GetRecentAsync(UserId, 2);

        Assert.Equal(2, recent.Count);
        Assert.Equal("Third", recent[0].Title);
        Assert.Equal("Second", recent[1].Title);
    }

    [Fact]
    public async Task GetRecentAsync_DoesNotReturnOtherUsersNotifications()
    {
        await _service.SendAsync(UserId, "My notification");
        await _service.SendAsync("other-user", "Other notification");

        var recent = await _service.GetRecentAsync(UserId);

        Assert.Single(recent);
        Assert.Equal("My notification", recent[0].Title);
    }

    [Fact]
    public async Task GetUnreadCountAsync_ReturnsCorrectCount()
    {
        await _service.SendAsync(UserId, "Unread 1");
        await _service.SendAsync(UserId, "Unread 2");
        await _service.SendAsync(UserId, "Unread 3");

        var count = await _service.GetUnreadCountAsync(UserId);
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task MarkAsReadAsync_MarksOnlySpecifiedNotification()
    {
        await _service.SendAsync(UserId, "Read me");
        await _service.SendAsync(UserId, "Leave me");

        var all = await _db.Set<Notification>().ToListAsync();
        var toRead = all.First(n => n.Title == "Read me");

        await _service.MarkAsReadAsync(toRead.Id, UserId);

        var readNotification = await _db.Set<Notification>().FindAsync(toRead.Id);
        var unreadNotification = await _db.Set<Notification>().FirstAsync(n => n.Title == "Leave me");

        Assert.True(readNotification!.IsRead);
        Assert.NotNull(readNotification.ReadAt);
        Assert.False(unreadNotification.IsRead);
    }

    [Fact]
    public async Task MarkAsReadAsync_DoesNotMarkOtherUsersNotification()
    {
        await _service.SendAsync("other-user", "Not mine");
        var notification = await _db.Set<Notification>().FirstAsync();

        await _service.MarkAsReadAsync(notification.Id, UserId);

        var unchanged = await _db.Set<Notification>().FindAsync(notification.Id);
        Assert.False(unchanged!.IsRead);
    }

    [Fact]
    public async Task MarkAllAsReadAsync_MarksAllUnreadForUser()
    {
        await _service.SendAsync(UserId, "Unread 1");
        await _service.SendAsync(UserId, "Unread 2");
        await _service.SendAsync("other-user", "Other unread");

        await _service.MarkAllAsReadAsync(UserId);

        var userNotifications = await _db.Set<Notification>()
            .Where(n => n.UserId == UserId)
            .ToListAsync();
        Assert.All(userNotifications, n => Assert.True(n.IsRead));

        var otherNotification = await _db.Set<Notification>()
            .FirstAsync(n => n.UserId == "other-user");
        Assert.False(otherNotification.IsRead);
    }

    [Fact]
    public async Task GetUnreadCountAsync_UpdatesAfterMarkAsRead()
    {
        await _service.SendAsync(UserId, "N1");
        await _service.SendAsync(UserId, "N2");

        Assert.Equal(2, await _service.GetUnreadCountAsync(UserId));

        var first = await _db.Set<Notification>().FirstAsync();
        await _service.MarkAsReadAsync(first.Id, UserId);

        Assert.Equal(1, await _service.GetUnreadCountAsync(UserId));
    }
}
