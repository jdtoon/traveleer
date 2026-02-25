# Notifications Module

In-app notification system scoped per-tenant, per-user. Provides a notification bell component, dropdown list, and full notification page. Other modules send notifications via `INotificationService`.

## Structure

```
Notifications/
├── NotificationsModule.cs
├── Entities/
│   └── Notification.cs                    # Tenant DB entity
├── Data/
│   └── NotificationTenantConfiguration.cs # ITenantEntityConfiguration
├── Services/
│   └── NotificationService.cs             # INotificationService implementation
├── Controllers/
│   └── NotificationController.cs          # HTMX endpoints for bell/dropdown/list
└── Views/
    └── Notification/
        ├── NotificationList.cshtml        # Full notification page
        ├── _NotificationBell.cshtml       # Bell icon with unread count badge
        └── _NotificationDropdown.cshtml   # Dropdown with recent notifications
```

## Entity

**`Notification`** (Tenant DB — stored per-tenant database):

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `Guid` | Primary key |
| `UserId` | `string` | ASP.NET Identity user ID |
| `Title` | `string` | Notification title |
| `Message` | `string?` | Optional detail text |
| `Url` | `string?` | Optional link — clicking navigates here |
| `Type` | `NotificationType` | `Info`, `Success`, `Warning`, `Error` |
| `IsRead` | `bool` | Read status |
| `ReadAt` | `DateTime?` | When it was marked read |

## Service API

**`INotificationService`** — inject this to send notifications from any module:

```csharp
public interface INotificationService
{
    Task SendAsync(string userId, string title, string? message = null,
                   string? url = null, NotificationType type = NotificationType.Info);

    Task<List<Notification>> GetRecentAsync(string userId, int count = 10);
    Task<int> GetUnreadCountAsync(string userId);
    Task MarkAsReadAsync(Guid notificationId, string userId);
    Task MarkAllAsReadAsync(string userId);
}
```

### Sending Notifications from Your Module

```csharp
public class ClaimService
{
    private readonly INotificationService _notifications;

    public async Task OnClaimApproved(string userId, string claimNumber)
    {
        await _notifications.SendAsync(
            userId,
            $"Claim {claimNumber} approved",
            message: "Your medical claim has been approved and processed.",
            url: $"/claims/{claimNumber}",
            type: NotificationType.Success);
    }
}
```

## Routes

All routes require `TenantUser` policy. Base path: `/{slug}/notifications`.

| Method | URL | Action | Description |
|--------|-----|--------|-------------|
| GET | `/{slug}/notifications` | `Index` | Full notification list page |
| GET | `/{slug}/notifications/dropdown` | `Dropdown` | HTMX partial — recent notifications |
| GET | `/{slug}/notifications/count` | `UnreadCount` | HTMX partial — badge count for bell |
| POST | `/{slug}/notifications/{id}/read` | `MarkRead` | Mark one notification as read |
| POST | `/{slug}/notifications/read-all` | `MarkAllRead` | Mark all notifications as read |

## UI Components

### Notification Bell (`_NotificationBell`)
A bell icon with an unread count badge. Placed in the tenant layout header. Polls for unread count via HTMX (auto-refreshing badge).

### Notification Dropdown (`_NotificationDropdown`)
Triggered by clicking the bell. Shows the 10 most recent notifications. Each item is clickable (navigates to `Url` if set) and can be individually marked as read.

### Notification List (`NotificationList`)
Full page listing all notifications with pagination.

## No Configuration Required

The module registers `INotificationService` automatically. No `appsettings.json` keys needed. Notifications are stored in the tenant database and scoped to individual users.
