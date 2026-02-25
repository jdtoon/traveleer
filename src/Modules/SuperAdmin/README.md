# SuperAdmin Module

Platform-wide administration dashboard for managing tenants, plans, features, billing, infrastructure, audit logs, sessions, announcements, and data export. Accessible only to super admins at `/super-admin/*`.

## Structure

```
SuperAdmin/
├── SuperAdminModule.cs
├── Entities/
│   ├── SuperAdmin.cs                    # Core DB: admin account (email, display name, 2FA fields)
│   └── Announcement.cs                  # Core DB: platform-wide announcements
├── Data/
│   └── SuperAdminCoreConfiguration.cs   # ICoreEntityConfiguration
├── Services/
│   ├── SuperAdminService.cs             # Core admin operations — tenants, plans, features, billing
│   ├── InfrastructureService.cs         # System health, Redis, RabbitMQ, disk, DB sizes, Hangfire
│   ├── TenantInspectionService.cs       # Deep tenant inspection (DB queries, user counts)
│   ├── SuperAdminAuditService.cs        # Query audit log entries
│   └── AnnouncementService.cs           # CRUD for platform announcements
├── Controllers/
│   ├── SuperAdminController.cs          # Main admin dashboard + tenant/plan/feature management
│   ├── InfrastructureController.cs      # Infrastructure monitoring pages
│   └── SuperAdminBillingController.cs   # Add-ons, discounts, webhook events management
└── Views/
    ├── SuperAdmin/                      # Tenants, Plans, Features, Config, Backups, Sessions, etc.
    ├── Infrastructure/                  # Health, Uptime, Redis, RabbitMQ, Jobs, Logs
    ├── SuperAdminBilling/               # Add-ons, Discounts, WebhookEvents
    └── Shared/                          # Shared partials and layout
```

## Auth

- **Auth scheme:** `SuperAdmin` (cookie: `.SuperAdmin.Auth`, 24-hour lifetime)
- **Policy:** `SuperAdmin` — all controllers use `[Authorize(Policy = "SuperAdmin")]`
- **Login:** `/super-admin/auth/login` (magic link — same flow as tenant auth but for super admins)
- **2FA:** Optional TOTP via `SuperAdminTwoFactorController` (in Auth module)
- **Seeded admin:** Created by `CoreDataSeeder` from `SuperAdmin:Email` in `appsettings.json`

## Key Service APIs

### `ISuperAdminService`

| Method | Purpose |
|--------|---------|
| `GetDashboardAsync()` | Dashboard stats (tenant count, revenue, active users, recent signups) |
| `GetTenantsAsync(search, page, pageSize)` | Paginated tenant list with search |
| `GetTenantDetailAsync(tenantId)` | Full tenant detail (subscription, invoices, users, health) |
| `SuspendTenantAsync(tenantId)` | Suspend a tenant (blocks access) |
| `ActivateTenantAsync(tenantId)` | Reactivate a suspended tenant |
| `ChangeTenantPlanAsync(tenantId, planId)` | Change tenant's plan (updates subscription) |
| `ExtendTrialAsync(tenantId, days)` | Extend a tenant's trial period |
| `ScheduleTenantDeletionAsync(tenantId)` | Schedule tenant for permanent deletion |
| `GetPlansAsync()` / `SavePlanAsync(model)` | View/edit billing plans |
| `GetFeatureMatrixAsync()` | Plans × Features matrix with enable/disable toggles |
| `TogglePlanFeatureAsync(planId, featureId)` | Toggle feature assignment to a plan |
| `GetTenantFeatureOverrideAsync(tenantId, featureId)` | View per-tenant feature override |
| `SaveTenantFeatureOverrideAsync(model)` | Create/update per-tenant feature override |
| `GetBillingDashboardAsync()` | Platform billing summary (MRR, churn, revenue breakdown) |
| `GetAdminsAsync()` / `CreateAdminAsync(email, name)` | Manage super admin accounts |
| `ToggleAdminStatusAsync(adminId)` | Enable/disable an admin account |
| `GetAllActiveSessionsAsync()` | View all active sessions across all tenants |
| `BroadcastAnnouncementAsync(...)` | Create platform-wide announcement |
| `ExportTenantsCsvAsync()` / `ExportBillingCsvAsync()` | Export data as CSV |
| `GetLitestreamStatusAsync()` | Backup replication status |

### `IInfrastructureService`

| Method | Purpose |
|--------|---------|
| `GetSystemHealthAsync()` | ASP.NET health checks aggregated status |
| `GetRedisInfoAsync()` | Redis server info (memory, connections, version) |
| `GetRabbitMqStatusAsync()` | RabbitMQ cluster status via management API |
| `GetDiskUsageAsync()` | Disk space on data volume |
| `GetDatabaseSizesAsync()` | File sizes of core.db, audit.db, all tenant DBs |
| `GetHangfireStatusAsync()` | Hangfire job queue status (enqueued, processing, failed) |

## Routes

All routes are prefixed with `/super-admin/` and require the `SuperAdmin` policy.

### Main Dashboard & Management (`SuperAdminController`)

| URL | Page |
|-----|------|
| `/super-admin` | Dashboard (stats, charts, recent activity) |
| `/super-admin/tenants` | Tenant list with search/filter |
| `/super-admin/tenants/{id}` | Tenant detail (subscription, users, invoices) |
| `/super-admin/tenants/{id}/database` | Tenant database inspection |
| `/super-admin/tenants/{id}/health` | Tenant health check |
| `/super-admin/plans` | Plan management (create/edit/delete) |
| `/super-admin/features` | Feature flag matrix (plans × features) |
| `/super-admin/config` | Platform configuration viewer |
| `/super-admin/backups` | Litestream backup status |
| `/super-admin/audit-log` | Audit log browser |
| `/super-admin/sessions` | Active sessions across all tenants |
| `/super-admin/query-console` | SQL query console for tenant DBs |
| `/super-admin/announcements` | Platform announcements |
| `/super-admin/admins` | Super admin account management |

### Infrastructure (`InfrastructureController`)

| URL | Page |
|-----|------|
| `/super-admin/infrastructure/health` | Health check dashboard |
| `/super-admin/infrastructure/uptime` | Uptime Kuma embed (if configured) |
| `/super-admin/infrastructure/redis` | Redis monitoring |
| `/super-admin/infrastructure/rabbitmq` | RabbitMQ monitoring |
| `/super-admin/infrastructure/jobs` | Hangfire job status + dashboard link |
| `/super-admin/infrastructure/logs` | Log viewer (Seq embed if configured) |

### Billing Management (`SuperAdminBillingController`)

| URL | Page |
|-----|------|
| `/super-admin/billing/addons` | Add-on management |
| `/super-admin/billing/discounts` | Discount code management |
| `/super-admin/billing/webhook-events` | Paystack webhook event log |

## Configuration

| Key | Purpose |
|-----|---------|
| `SuperAdmin:Email` | Email address of the default super admin (seeded on first boot) |
| `Infrastructure:SeqUrl` | URL for Seq log viewer iframe |
| `Infrastructure:RabbitMqManagementUrl` | URL for RabbitMQ management UI |
| `Infrastructure:UptimeKumaUrl` | URL for Uptime Kuma status page |

## Reserved Slugs

This module reserves: `super-admin`, `admin`

## Extending the Admin Dashboard

To add a new admin section:

1. Create a controller inheriting `SwapController` with `[Authorize(Policy = "SuperAdmin")]`
2. Add routes under `/super-admin/your-section`
3. Add the controller name to `ControllerViewPaths` and `PartialViewSearchPaths` in `SuperAdminModule.cs`
4. Create views in `Views/YourSection/`
