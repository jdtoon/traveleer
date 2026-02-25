# Dashboard Module

Lightweight tenant landing page after login. This is a view-only module with no services, entities, or features — it serves as the entry point after authentication.

## Structure

```
Dashboard/
├── DashboardModule.cs
├── Controllers/
│   └── DashboardController.cs      # SwapController, [Authorize("TenantUser")]
└── Views/
    └── Dashboard/
        └── Index.cshtml             # Tenant home page
```

## Route

| Method | URL | Action | Auth |
|--------|-----|--------|------|
| GET | `/{slug}/Dashboard` | `Index` | TenantUser policy |

## How It Works

- After a tenant user logs in, they're redirected to `/{slug}/Dashboard`
- The controller inherits `SwapController` (Swap.Htmx base), so it supports both full page and HTMX partial requests automatically
- No services or DI — the view renders directly

## Extending for Your App

This is where you'd build your tenant's main dashboard. Common patterns:

1. **Inject services** into the controller to fetch dashboard data (stats, charts, recent activity)
2. **Add HTMX partials** for widgets that load asynchronously (counts, graphs, activity feeds)
3. **Use `[RequireFeature]`** to show/hide widgets based on the tenant's plan

Example:

```csharp
public class DashboardController : SwapController
{
    private readonly IMyDashboardService _dashboard;
    private readonly INotificationService _notifications;

    public DashboardController(IMyDashboardService dashboard, INotificationService notifications)
    {
        _dashboard = dashboard;
        _notifications = notifications;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var stats = await _dashboard.GetStatsAsync(TenantId);
        return SwapView(stats);
    }

    [HttpGet("widgets/recent-activity")]
    public async Task<IActionResult> RecentActivity()
    {
        var items = await _dashboard.GetRecentActivityAsync(TenantId);
        return SwapView("_RecentActivity", items);
    }
}
```

## Module Registration

```csharp
// Program.cs — already registered as a framework module
new saas.Modules.Dashboard.DashboardModule(),
```

No services to register — `RegisterServices` is a no-op.
