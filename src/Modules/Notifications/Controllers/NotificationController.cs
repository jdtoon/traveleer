using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Notifications.Services;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Notifications.Controllers;

[Route("{slug}/notifications")]
[Authorize(Policy = "TenantUser")]
public class NotificationController : SwapController
{
    private readonly INotificationService _notifications;
    private readonly ICurrentUser _currentUser;

    public NotificationController(INotificationService notifications, ICurrentUser currentUser)
    {
        _notifications = notifications;
        _currentUser = currentUser;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var userId = _currentUser.UserId ?? string.Empty;
        var notifications = await _notifications.GetRecentAsync(userId, 20);
        return SwapView("NotificationList", notifications);
    }

    [HttpGet("dropdown")]
    public async Task<IActionResult> Dropdown()
    {
        var userId = _currentUser.UserId ?? string.Empty;
        var notifications = await _notifications.GetRecentAsync(userId, 5);
        var unreadCount = await _notifications.GetUnreadCountAsync(userId);
        ViewData["UnreadCount"] = unreadCount;
        return PartialView("_NotificationDropdown", notifications);
    }

    [HttpGet("count")]
    public async Task<IActionResult> UnreadCount()
    {
        var userId = _currentUser.UserId ?? string.Empty;
        var count = await _notifications.GetUnreadCountAsync(userId);
        return Content(count > 0 ? count.ToString() : string.Empty);
    }

    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        var userId = _currentUser.UserId ?? string.Empty;
        await _notifications.MarkAsReadAsync(id, userId);
        return Ok();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead([FromRoute] string slug)
    {
        var userId = _currentUser.UserId ?? string.Empty;
        await _notifications.MarkAllAsReadAsync(userId);
        return await Dropdown();
    }
}
