# Super Admin Command Center — Enhancement Plan

> Transform the super admin panel from a basic tenant/plan/feature manager into a full operational command center with deep infrastructure visibility, tenant database probing, impersonation, billing dashboards, and more.

**Decisions:**
- External service UIs (RabbitMQ, Seq, Uptime Kuma) → **embedded iframes** with native summary cards
- Tenant DB access → **read-only inspection** (no mutations)
- Impersonation → **tenant admins only** with full audit trail + 1-hour expiry
- Sidebar → **grouped collapsible sections** with localStorage persistence
- Delivery → **phased** (P0 → P1 → P2)

---

## Progress Tracker

| # | Item | Phase | Status |
|---|------|-------|--------|
| 1 | [Reorganize sidebar with collapsible sections](#1-reorganize-sidebar-with-collapsible-sections) | P0 | ✅ Done |
| 2 | [Register IConnectionMultiplexer in DI](#2-register-iconnectionmultiplexer-in-di) | P0 | ✅ Done |
| 3 | [Create IInfrastructureService abstraction](#3-create-iinfrastructureservice-abstraction) | P0 | ✅ Done |
| 4 | [Create ITenantInspectionService abstraction](#4-create-itenantinspectionservice-abstraction) | P0 | ✅ Done |
| 5 | [Audit super admin actions](#5-audit-super-admin-actions) | P0 | ✅ Done |
| 6 | [System Health Dashboard page](#6-system-health-dashboard-page) | P1 | ✅ Done |
| 7 | [Redis Dashboard page](#7-redis-dashboard-page) | P1 | ✅ Done |
| 8 | [RabbitMQ Dashboard page](#8-rabbitmq-dashboard-page) | P1 | ⬜ Not Started |
| 9 | [Seq Log Viewer page](#9-seq-log-viewer-page) | P1 | ⬜ Not Started |
| 10 | [Uptime Kuma integration page](#10-uptime-kuma-integration-page) | P1 | ⬜ Not Started |
| 11 | [Enhanced Backups page](#11-enhanced-backups-page) | P1 | ⬜ Not Started |
| 12 | [Enhanced Hangfire integration](#12-enhanced-hangfire-integration) | P1 | ⬜ Not Started |
| 13 | [Tenant Database Inspector page](#13-tenant-database-inspector-page) | P2 | ⬜ Not Started |
| 14 | [Tenant Health monitoring page](#14-tenant-health-monitoring-page) | P2 | ⬜ Not Started |
| 15 | [Enhanced Tenant Management actions](#15-enhanced-tenant-management-actions) | P2 | ⬜ Not Started |
| 16 | [Tenant Impersonation](#16-tenant-impersonation) | P2 | ⬜ Not Started |
| 17 | [Billing & Revenue Dashboard](#17-billing--revenue-dashboard) | P3 | ⬜ Not Started |
| 18 | [System Configuration Viewer](#18-system-configuration-viewer) | P3 | ⬜ Not Started |
| 19 | [Multi-Admin Management](#19-multi-admin-management) | P3 | ⬜ Not Started |
| 20 | [Active Sessions & Security page](#20-active-sessions--security-page) | P3 | ⬜ Not Started |
| 21 | [Notification/Announcement System](#21-notificationannouncement-system) | P3 | ⬜ Not Started |
| 22 | [Export & Reporting](#22-export--reporting) | P3 | ⬜ Not Started |
| 23 | [Docker Compose iframe support](#23-docker-compose-iframe-support) | P1 | ✅ Done |

---

## Phase 0 — Foundation

### 1. Reorganize sidebar with collapsible sections

**Files to modify:**
- `src/Modules/SuperAdmin/Views/Shared/_AdminLayout.cshtml`

**What:**
Replace the current flat nav list with grouped, collapsible DaisyUI sections:

| Section | Items |
|---------|-------|
| **Overview** | Dashboard |
| **Tenants** | Tenant List, Tenant Health |
| **Platform** | Plans, Features, Billing & Revenue |
| **Infrastructure** | System Health, Redis, RabbitMQ, Logs (Seq), Uptime, Backups, Jobs |
| **Security** | Audit Log, Active Sessions, Rate Limiting |
| **Settings** | System Config, Admins |

**Details:**
- Use DaisyUI `<details>` / `collapse` components for grouping
- Store collapse state in `localStorage` so sections stay open/closed across navigations
- Add badge indicators (red dot for degraded health, counts for pending items)
- Active page highlighting within sections
- Mobile responsive — drawer already exists, sections work inside it

---

### 2. Register IConnectionMultiplexer in DI

**Files to modify:**
- `src/Infrastructure/CachingExtensions.cs`

**What:**
When the caching provider is `Redis`, register `IConnectionMultiplexer` as a singleton in DI so we can query Redis server info (memory, clients, keyspace stats) from the admin panel.

**Details:**
- `StackExchange.Redis` is already a transitive dependency via `Microsoft.Extensions.Caching.StackExchangeRedis` — no new NuGet package needed
- Register `IConnectionMultiplexer` via `ConnectionMultiplexer.Connect(connectionString)`
- Wire `AddStackExchangeRedisCache` to use the shared multiplexer instance
- When provider is `Memory`, `IConnectionMultiplexer` is simply not registered (services that depend on it check for null/optional injection)

---

### 3. Create IInfrastructureService abstraction

**Files to create:**
- `src/Modules/SuperAdmin/Services/IInfrastructureService.cs` — interface + models
- `src/Modules/SuperAdmin/Services/InfrastructureService.cs` — implementation

**Files to modify:**
- `src/Modules/SuperAdmin/SuperAdminModule.cs` — register in DI

**What:**
Central service for querying infrastructure state: system health, Redis info, RabbitMQ status, disk usage, database sizes, cache stats, messaging stats.

**Methods:**
```csharp
Task<SystemHealthModel> GetSystemHealthAsync();
Task<RedisInfoModel?> GetRedisInfoAsync();           // null if Redis not configured
Task<RabbitMqStatusModel?> GetRabbitMqStatusAsync();  // null if InMemory
Task<DiskUsageModel> GetDiskUsageAsync();
Task<List<DatabaseSizeInfo>> GetDatabaseSizesAsync();
Task<HangfireStatusModel> GetHangfireStatusAsync();
```

**Dependencies:**
- `IConnectionMultiplexer` (optional — null when using Memory cache)
- `HealthCheckService` (from Microsoft.Extensions.Diagnostics.HealthChecks)
- `IConfiguration` (for reading provider settings)
- `IHttpClientFactory` (for RabbitMQ Management API calls)

---

### 4. Create ITenantInspectionService abstraction

**Files to create:**
- `src/Modules/SuperAdmin/Services/ITenantInspectionService.cs` — interface + models
- `src/Modules/SuperAdmin/Services/TenantInspectionService.cs` — implementation

**Files to modify:**
- `src/Modules/SuperAdmin/SuperAdminModule.cs` — register in DI

**What:**
Read-only service for probing individual tenant SQLite databases. Opens tenant DBs using the same pattern as `SuperAdminService.GetTenantUserCountAsync()` (scoped `TenantDbContext` pointing at `db/tenants/{slug}.db`).

**Methods:**
```csharp
Task<TenantDatabaseInfoModel> GetDatabaseInfoAsync(string slug);
Task<List<TenantUserInfo>> GetUsersAsync(string slug);
Task<List<TenantSessionInfo>> GetActiveSessionsAsync(string slug);
Task<List<TenantRoleInfo>> GetRolesAsync(string slug);
Task<TenantDataCountsModel> GetDataCountsAsync(string slug);
Task<List<TenantInvitationInfo>> GetPendingInvitationsAsync(string slug);
```

**Models include:**
- `TenantDatabaseInfoModel` — file path, size bytes, WAL size, last modified, table names + row counts
- `TenantUserInfo` — id, email, display name, is active, email confirmed, 2FA enabled, last login, lockout end
- `TenantSessionInfo` — session id, user email, device info, IP, created, last activity, expires
- `TenantRoleInfo` — role name, is system role, user count, permission list
- `TenantDataCountsModel` — users, roles, notes, notifications, sessions, invitations
- `TenantInvitationInfo` — email, role, status, invited by, sent at, expires at

---

### 5. Audit super admin actions

**Files to create:**
- `src/Modules/SuperAdmin/Services/ISuperAdminAuditService.cs` — interface
- `src/Modules/SuperAdmin/Services/SuperAdminAuditService.cs` — implementation

**Files to modify:**
- `src/Modules/Audit/Entities/AuditEntry.cs` — add `Source` property (or discriminator)
- `src/Modules/SuperAdmin/Controllers/SuperAdminController.cs` — wire audit calls into every action
- `src/Modules/Audit/Controllers/AuditLogController.cs` — add Source filter to audit log page
- `src/Modules/SuperAdmin/SuperAdminModule.cs` — register in DI

**What:**
Track every super admin action (currently only tenant DB entity changes are audited via the EF interceptor). Super admin actions like suspend/activate, plan edits, feature toggles, impersonation sessions, config views, and DB inspections should all be logged.

**Actions to audit:**
- Tenant suspended / activated
- Plan created / updated
- Feature toggled for plan
- Feature override saved for tenant
- Tenant created / deleted / reset (when those actions are added)
- Impersonation started / ended (when added)
- System config viewed (when added)
- Tenant database inspected (when added)
- Cache flushed (when added)
- Job manually triggered (when added)
- Admin invited / deactivated (when added)

**Implementation:**
- Write to the existing `AuditDbContext.AuditEntries` table with a new `Source` column (`"SuperAdmin"` vs `"Tenant"`)
- Include: action name, admin email, target entity, old/new values where applicable, IP address, timestamp
- Update the audit log UI to support filtering by source

---

## Phase 1 — Infrastructure Visibility

### 6. System Health Dashboard page

**Files to create:**
- `src/Modules/SuperAdmin/Controllers/InfrastructureController.cs` — new controller for all infrastructure pages
- `src/Modules/SuperAdmin/Views/Infrastructure/Health.cshtml`
- `src/Infrastructure/HealthChecks/RedisHealthCheck.cs`
- `src/Infrastructure/HealthChecks/RabbitMqHealthCheck.cs`
- `src/Infrastructure/HealthChecks/SeqHealthCheck.cs`
- `src/Infrastructure/HealthChecks/DiskSpaceHealthCheck.cs`
- `src/Infrastructure/HealthChecks/HangfireHealthCheck.cs`

**Files to modify:**
- `src/Program.cs` — register new health checks
- `src/Modules/SuperAdmin/SuperAdminModule.cs` — register controller view paths

**What:**
A dedicated health page that runs ALL health checks and displays results with status, duration, and description. Auto-refreshes every 30 seconds via HTMX polling.

**New health checks:**

| Check Name | Class | Validates |
|---|---|---|
| `redis` | `RedisHealthCheck` | `IConnectionMultiplexer.GetDatabase().PingAsync()` response time |
| `rabbitmq` | `RabbitMqHealthCheck` | TCP connect to configured host:port or MassTransit bus health |
| `seq` | `SeqHealthCheck` | HTTP GET to Seq API endpoint returns 200 |
| `disk-space` | `DiskSpaceHealthCheck` | Available disk space on `db/` volume > threshold (e.g., 500MB) |
| `hangfire` | `HangfireHealthCheck` | `JobStorage.Current` accessible + connected servers > 0 |

**Page layout:**
- Overall status banner: ✅ Healthy / ⚠️ Degraded / ❌ Unhealthy
- Grid of cards, one per health check, showing: name, status icon, response time, description/error
- Last checked timestamp
- Auto-refresh indicator with `hx-trigger="every 30s"`

---

### 7. Redis Dashboard page

**Files to create:**
- `src/Modules/SuperAdmin/Views/Infrastructure/Redis.cshtml`

**Files to modify:**
- `src/Modules/SuperAdmin/Controllers/InfrastructureController.cs` — add action

**What:**
Redis monitoring and cache management page using `IConnectionMultiplexer.GetServer().InfoAsync()`.

**Displays:**
- **Server info**: Redis version, uptime, connected clients, used memory (human-readable), peak memory, memory fragmentation ratio
- **Cache stats**: Keyspace hits vs misses, hit rate percentage, evicted keys
- **Key count**: Total keys in the configured database
- **Current connections**: Client list count

**Cache management actions:**
- "Flush Tenant Cache" button (per-tenant: removes `tenant-resolution-{slug}`, `feature-tenant-{tenantId}`, `plan-rate-limit-{slug}`)
- "Flush All App Caches" button (removes all known key patterns)
- "Invalidate Feature Cache" button (calls `FeatureCacheInvalidator`)
- Each action is confirmed via modal and audit-logged

**Graceful degradation:**
- If provider is `Memory`: show "Redis not configured — using in-memory cache" with basic memory cache info

**Auto-refresh:** HTMX polling every 15 seconds for stats cards

---

### 8. RabbitMQ Dashboard page

**Files to create:**
- `src/Modules/SuperAdmin/Views/Infrastructure/RabbitMQ.cshtml`

**Files to modify:**
- `src/Modules/SuperAdmin/Controllers/InfrastructureController.cs` — add action

**What:**
RabbitMQ monitoring via embedded iframe of the Management UI + native summary cards.

**Layout:**
1. **Connection status card** — provider config (RabbitMQ vs InMemory), host, vhost, connection state
2. **Native summary cards** (via RabbitMQ Management HTTP API at `http://rabbitmq:15672/api/overview`, authenticated with configured credentials):
   - Queue depths for known queues (default, emails, maintenance)
   - Message publish/deliver rates
   - Consumer count
   - Connection count
3. **Embedded iframe** — full RabbitMQ Management UI (`http://rabbitmq:15672`)

**Graceful degradation:**
- If provider is `InMemory`: show "RabbitMQ not configured — using in-memory messaging" message
- If iframe fails to load: fallback to "Open RabbitMQ Management →" external link
- If API unreachable: show connection error with retry button

---

### 9. Seq Log Viewer page

**Files to create:**
- `src/Modules/SuperAdmin/Views/Infrastructure/Logs.cshtml`

**Files to modify:**
- `src/Modules/SuperAdmin/Controllers/InfrastructureController.cs` — add action

**What:**
Seq structured log viewer via embedded iframe with quick-filter controls above.

**Layout:**
1. **Connection status card** — Seq URL, reachability indicator
2. **Quick-filter bar** (sets Seq URL parameters for the iframe):
   - "Errors Only" button → filters to `@Level='Error'`
   - "Warnings+" button → filters to `@Level in ['Warning','Error','Fatal']`
   - "By Tenant" dropdown → filters to `TenantSlug='{slug}'`
   - "Last Hour" / "Last 24h" / "Last 7d" time range buttons
3. **Embedded iframe** — full Seq UI

**Graceful degradation:**
- If Seq URL not configured: show "Seq not configured" message
- If iframe fails to load: fallback to "Open Seq →" external link

---

### 10. Uptime Kuma integration page

**Files to create:**
- `src/Modules/SuperAdmin/Views/Infrastructure/Uptime.cshtml`

**Files to modify:**
- `src/Modules/SuperAdmin/Controllers/InfrastructureController.cs` — add action

**What:**
Uptime Kuma monitoring dashboard embedded in the admin panel.

**Layout:**
1. **Info card** — Suggested monitors to configure (if first time), with setup guidance:
   - App health endpoint: `http://app:8080/health`
   - Redis: TCP `redis:6379`
   - RabbitMQ: TCP `rabbitmq:5672`
   - Seq: HTTP `seq:8081`
2. **Embedded iframe** — full Uptime Kuma UI

**Graceful degradation:**
- If iframe fails to load: show "Open Uptime Kuma →" external link

---

### 11. Enhanced Backups page

**Files to modify:**
- `src/Modules/SuperAdmin/Views/SuperAdmin/Backups.cshtml` — extend with new sections
- `src/Modules/SuperAdmin/Controllers/SuperAdminController.cs` — add manual sync trigger
- `src/Infrastructure/Services/LitestreamConfigSyncService.cs` — add `SyncNowAsync()` method
- `src/Shared/ILitestreamConfigSync.cs` — add `SyncNowAsync()` to interface
- `src/Modules/SuperAdmin/Services/ISuperAdminService.cs` — add backup detail methods
- `src/Modules/SuperAdmin/Services/SuperAdminService.cs` — implement

**What:**
Extend the existing read-only backups page with actionable controls and per-database detail.

**New sections:**
- **Per-database replication table**: Every database being replicated (core.db, audit.db, hangfire.db, each tenant .db) with columns: filename, file size, WAL size, last modified, replication status
- **Manual config sync**: Button to invoke `ILitestreamConfigSync.SyncNowAsync()` — forces immediate config regeneration and Litestream sidecar reload
- **Key backup detail**: Last backup time with freshness indicator (green < 1x interval, yellow 1-2x, red > 2x `KeyBackupInterval`)
- **Replication lag estimate**: Compare file modification times to sentinel timestamps
- **R2 configuration summary**: Bucket name, endpoint (masked credentials)
- **Interactive restore guide**: Step-by-step commands with copy-to-clipboard, customized per database name

---

### 12. Enhanced Hangfire integration

**Files to create:**
- `src/Modules/SuperAdmin/Views/Infrastructure/Jobs.cshtml`

**Files to modify:**
- `src/Modules/SuperAdmin/Controllers/InfrastructureController.cs` — add action

**What:**
Replace the external Hangfire link with a native summary page + embedded dashboard.

**Layout:**
1. **Job statistics cards**: Succeeded (24h), Failed (24h), Processing, Scheduled, Enqueued, Recurring count
2. **Failed jobs table**: Last 10 failures with job name, exception message, failed at, retry count — expandable for full stack trace
3. **Recurring jobs table**: Job ID, cron expression, next execution, last execution, last status — with "Trigger Now" button per job
4. **Server info**: Connected servers, workers per server, queues being processed
5. **Embedded iframe**: Full Hangfire dashboard below (already has `SuperAdminDashboardAuthFilter` auth)

**Actions:**
- "Trigger Now" per recurring job → calls `RecurringJob.TriggerJob(jobId)`, audit-logged
- Alert banner if any jobs have failed in the last 24 hours

---

### 23. Docker Compose iframe support

**Files to modify:**
- `docker-compose.yml`
- `docker-compose.local.yml`

**What:**
Configure external services to allow iframe embedding from the app's origin.

**Changes:**
- **RabbitMQ**: Add custom `rabbitmq.conf` or environment args to set `management.cors.allow_origins` and relax `Content-Security-Policy` / `X-Frame-Options`
- **Seq**: Configure `SEQ__API__CORSORIGINS` or a reverse proxy to allow framing
- **Uptime Kuma**: May need a reverse proxy approach or `--allow-all-origins` flag
- **Fallback strategy**: If framing proves problematic for a service, implement a server-side reverse proxy route (`/super-admin/proxy/{service}/...`) in the .NET app that forwards requests to the internal Docker network — this avoids all CORS/framing issues (same-origin)
- **CSP update**: Update `SecurityHeadersMiddleware.cs` to add `frame-src` directives for the allowed internal service origins on super admin pages

---

## Phase 2 — Tenant Deep Management

### 13. Tenant Database Inspector page

**Files to create:**
- `src/Modules/SuperAdmin/Views/SuperAdmin/TenantDatabase.cshtml`

**Files to modify:**
- `src/Modules/SuperAdmin/Controllers/SuperAdminController.cs` (or new `TenantManagementController.cs`) — add action
- `src/Modules/SuperAdmin/Views/SuperAdmin/TenantDetail.cshtml` — add "Inspect Database →" link

**Route:** `GET /super-admin/tenants/{id}/database`

**What:**
Read-only deep inspection of a tenant's SQLite database using `ITenantInspectionService`.

**Sections:**
- **Database file info**: path (`db/tenants/{slug}.db`), file size, WAL file size, last modified timestamp
- **Schema overview**: all table names with row counts (via `sqlite_master` + `COUNT(*)`)
- **Users**: full list — email, display name, active, email confirmed, 2FA enabled, last login, lockout
- **Active sessions**: session ID (truncated), user email, device, IP, created, last activity, expires
- **Roles & Permissions**: role names with assigned permission list and user count per role
- **Data counts**: notes, notifications (read/unread), invitations (pending/accepted/expired)
- **Team invitations**: pending invites with email, role, invited by, sent date, expiry

**Navigation:** Breadcrumb: Tenants → {Tenant Name} → Database

---

### 14. Tenant Health monitoring page

**Files to create:**
- `src/Modules/SuperAdmin/Views/SuperAdmin/TenantHealth.cshtml`

**Files to modify:**
- `src/Modules/SuperAdmin/Controllers/SuperAdminController.cs` — add action
- `src/Modules/SuperAdmin/Services/ISuperAdminService.cs` — add health methods
- `src/Modules/SuperAdmin/Services/SuperAdminService.cs` — implement

**Route:** `GET /super-admin/tenant-health`

**What:**
Aggregated health overview of all tenants with color-coded status indicators.

**Columns per tenant:**
- Tenant name + slug (link to detail)
- Database: file exists & readable (✅/❌), file size
- Users: current count / `MaxUsers` limit (flag > 80% = yellow, > 95% = red)
- Subscription: status badge (green=Active, blue=Trialing, yellow=PastDue, red=Cancelled/Expired)
- Trial: days remaining (if trialing), flag if < 3 days
- Last activity: last user login timestamp, flag if > 30 days dormant
- Litestream: replication status for this tenant's DB

**Features:**
- Color-coded rows: green (healthy), yellow (attention needed), red (critical)
- Sortable columns (click header)
- Filterable: "Show only issues" toggle to hide healthy tenants
- HTMX partial refresh for re-check
- Summary bar: X healthy, Y attention, Z critical

---

### 15. Enhanced Tenant Management actions

**Files to create:**
- `src/Modules/SuperAdmin/Controllers/TenantManagementController.cs`
- `src/Modules/SuperAdmin/Views/SuperAdmin/TenantCreate.cshtml`
- `src/Modules/SuperAdmin/Views/SuperAdmin/_TenantPlanChangeModal.cshtml`
- `src/Modules/SuperAdmin/Views/SuperAdmin/_TenantDeleteModal.cshtml`
- `src/Modules/SuperAdmin/Views/SuperAdmin/_TenantExtendTrialModal.cshtml`

**Files to modify:**
- `src/Modules/SuperAdmin/Views/SuperAdmin/TenantDetail.cshtml` — add action buttons
- `src/Modules/SuperAdmin/Services/ISuperAdminService.cs` — add methods
- `src/Modules/SuperAdmin/Services/SuperAdminService.cs` — implement
- `src/Modules/SuperAdmin/SuperAdminModule.cs` — register new controller paths

**New endpoints:**

| Method | Route | Action |
|--------|-------|--------|
| `GET` | `/super-admin/tenants/create` | Tenant creation form |
| `POST` | `/super-admin/tenants/create` | Create tenant → calls `TenantProvisionerService` |
| `GET` | `/super-admin/tenants/{id}/change-plan` | Plan change modal |
| `POST` | `/super-admin/tenants/{id}/change-plan` | Change plan → updates subscription via `IBillingService` |
| `POST` | `/super-admin/tenants/{id}/delete` | Delete tenant (immediate or scheduled) |
| `POST` | `/super-admin/tenants/{id}/reset` | Reset tenant DB (re-migrate + re-seed) |
| `GET` | `/super-admin/tenants/{id}/extend-trial` | Extend trial modal |
| `POST` | `/super-admin/tenants/{id}/extend-trial` | Update `TrialEndsAt` |
| `GET` | `/super-admin/tenants/{id}/export` | Export tenant data as JSON download |

**Details:**
- All actions require `hx-confirm` modal confirmation
- All actions are audit-logged via `ISuperAdminAuditService`
- Delete has two modes: "Schedule Deletion" (30-day grace) and "Permanent Delete" (immediate, requires typing tenant slug to confirm)
- Reset preserves the tenant record in core DB but drops and recreates the tenant SQLite database
- Export uses existing `TenantLifecycleService.ExportTenantDataAsync()`

---

### 16. Tenant Impersonation

**Files to create:**
- `src/Modules/SuperAdmin/Views/SuperAdmin/_ImpersonationBanner.cshtml` — partial shown on all tenant pages during impersonation
- `src/Modules/SuperAdmin/Views/SuperAdmin/_ImpersonateModal.cshtml` — user selection modal

**Files to modify:**
- `src/Modules/SuperAdmin/Controllers/SuperAdminController.cs` — add impersonate/end actions
- `src/Modules/SuperAdmin/Views/SuperAdmin/TenantDetail.cshtml` — add "Impersonate Admin" button
- `src/Infrastructure/Middleware/CurrentUserMiddleware.cs` — detect impersonation claims
- `src/Shared/ICurrentUser.cs` — add `IsImpersonating`, `ImpersonatedBy` properties
- `src/Modules/Auth/Services/CurrentUser.cs` — implement new properties
- `src/Views/Shared/_Layout.cshtml` (or tenant layout) — render impersonation banner partial
- `src/Modules/SuperAdmin/Services/ISuperAdminAuditService.cs` — add impersonation audit methods

**New endpoints:**

| Method | Route | Action |
|--------|-------|--------|
| `GET` | `/super-admin/tenants/{id}/impersonate` | Show user selection modal (lists tenant admins) |
| `POST` | `/super-admin/tenants/{id}/impersonate` | Start impersonation session |
| `POST` | `/super-admin/impersonate/end` | End impersonation, return to super admin |

**Auth flow:**
1. Super admin clicks "Impersonate" on tenant detail → modal shows list of tenant admin users
2. Selects a user → POST creates a `Tenant` auth cookie with claims:
   - Standard tenant user claims (name, email, tenant_slug, roles, permissions)
   - Extra: `impersonated_by` = super admin ID
   - Extra: `impersonation_session` = new GUID
   - Extra: `impersonation_expires` = UTC now + 1 hour
3. Redirects to `/{slug}/dashboard`
4. **Impersonation banner** rendered on every tenant page (bright orange/warning bar):
   - "You are viewing {tenant} as {user}. [End Session]"
5. End session → clears Tenant cookie → redirects to `/super-admin/tenants/{id}`
6. Session auto-expires after 1 hour (middleware checks `impersonation_expires` claim)

**Audit trail:**
- Impersonation start: who, which tenant, which user, timestamp
- Impersonation end: duration
- All actions during impersonation: marked with `impersonated_by` in both tenant audit trail and super admin audit trail

---

## Phase 3 — Analytics, Billing Visibility & Advanced Features

### 17. Billing & Revenue Dashboard

**Files to create:**
- `src/Modules/SuperAdmin/Views/SuperAdmin/Billing.cshtml`
- `src/Modules/SuperAdmin/Services/IBillingDashboardService.cs` — interface + models
- `src/Modules/SuperAdmin/Services/BillingDashboardService.cs` — implementation

**Files to modify:**
- `src/Modules/SuperAdmin/Controllers/SuperAdminController.cs` — add action
- `src/Modules/SuperAdmin/SuperAdminModule.cs` — register service

**Route:** `GET /super-admin/billing`

**What:**
Financial overview using data from `CoreDbContext` (Invoices, Payments, Subscriptions).

**Sections:**
- **Revenue cards**: Total revenue (sum of paid invoices), estimated MRR, estimated ARR
- **Subscription breakdown**: Count by status (Active, Trialing, PastDue, Cancelled, Expired, NonRenewing) — displayed as stat cards + plan distribution table
- **Recent payments table**: last 20 — amount, currency, tenant name, status badge, Paystack reference, date
- **Recent invoices table**: last 20 — invoice number, tenant name, amount, status badge, due date, paid date
- **Plan distribution**: table showing each plan with subscriber count, revenue contribution
- **Trial conversion**: tenants that converted from trial → paid in last 30/60/90 days
- **Churn indicator**: cancelled/expired subscriptions count for current month vs previous

**Filters:**
- Date range picker (HTMX partial refresh)
- Plan filter dropdown

**Export:**
- CSV export for payments: `GET /super-admin/billing/export/payments`
- CSV export for invoices: `GET /super-admin/billing/export/invoices`

---

### 18. System Configuration Viewer

**Files to create:**
- `src/Modules/SuperAdmin/Views/SuperAdmin/Config.cshtml`

**Files to modify:**
- `src/Modules/SuperAdmin/Controllers/SuperAdminController.cs` — add action

**Route:** `GET /super-admin/settings/config`

**What:**
Read-only tree view of the current application configuration from `IConfiguration`.

**Details:**
- Render all config sections as a collapsible tree (Site, Billing, Email, Caching, Messaging, Litestream, RateLimiting, Hangfire, Storage, Tenancy, etc.)
- **Secret masking**: Any key containing `Password`, `Secret`, `Key`, `Token`, `ConnectionString`, `Credential` shows `●●●●●●●●` with a "Reveal" toggle button (reveals for 5 seconds then re-masks)
- Show current environment name (Development/Production)
- Show .NET runtime version, app version, OS info
- Indicate provider selections: Email (Console/SMTP/MailerSend), Billing (Mock/Paystack), Cache (Redis/Memory), Messaging (RabbitMQ/InMemory), Storage (Local/R2), Hangfire (SQLite/InMemory)
- **Audit logged**: viewing the config page creates an audit entry

---

### 19. Multi-Admin Management

**Files to create:**
- `src/Modules/SuperAdmin/Views/SuperAdmin/Admins.cshtml`
- `src/Modules/SuperAdmin/Views/SuperAdmin/_AdminInviteModal.cshtml`

**Files to modify:**
- `src/Modules/SuperAdmin/Controllers/SuperAdminController.cs` — add actions
- `src/Modules/SuperAdmin/Services/ISuperAdminService.cs` — add admin management methods
- `src/Modules/SuperAdmin/Services/SuperAdminService.cs` — implement

**Routes:**

| Method | Route | Action |
|--------|-------|--------|
| `GET` | `/super-admin/settings/admins` | List all super admins |
| `GET` | `/super-admin/settings/admins/invite` | Invite modal |
| `POST` | `/super-admin/settings/admins` | Create new admin |
| `POST` | `/super-admin/settings/admins/{id}/deactivate` | Deactivate admin |
| `POST` | `/super-admin/settings/admins/{id}/activate` | Reactivate admin |

**What:**
Currently only one super admin exists (seeded from `SuperAdmin:Email` config). Add UI to manage multiple admins.

**Details:**
- **Admin list table**: email, display name, is active badge, last login, created at
- **Invite form**: email + display name → inserts into `CoreDbContext.SuperAdmins` with `IsActive = true`
- The invited admin can then use the existing magic link login flow (`/super-admin/login`)
- **Deactivate**: sets `IsActive = false`, preventing login (magic link verification checks `IsActive`)
- **Protection**: the seed admin (matching `SuperAdmin:Email` config) cannot be deactivated
- **Admin activity**: link to audit log filtered to that admin's actions
- All actions audit-logged

---

### 20. Active Sessions & Security page

**Files to create:**
- `src/Modules/SuperAdmin/Views/SuperAdmin/Sessions.cshtml`
- `src/Modules/SuperAdmin/Views/SuperAdmin/RateLimiting.cshtml`

**Files to modify:**
- `src/Modules/SuperAdmin/Controllers/SuperAdminController.cs` — add actions

**Routes:**
- `GET /super-admin/security/sessions` — Active sessions overview
- `GET /super-admin/security/rate-limiting` — Rate limit configuration view

**Sessions page:**
- Aggregate view across all tenant DBs:
  - Total active sessions across all tenants
  - Top 10 tenants by session count
  - Active impersonation sessions (if any)
  - Super admin sessions from `SuperAdmins.LastLoginAt`
- Sessions are read from individual tenant SQLite DBs via `ITenantInspectionService`

**Rate limiting page:**
- Display current rate limit configuration in a readable table:
  - Global: X per minute per IP
  - Strict: X per minute (sensitive endpoints)
  - Registration: X per Y minutes
  - Contact: X per Y minutes
  - Webhook: X per minute
  - Per-plan tenant limits: table of plan → `MaxRequestsPerMinute`
- Read-only — configuration changes require env var updates and restart

---

### 21. Notification/Announcement System

**Files to create:**
- `src/Modules/SuperAdmin/Views/SuperAdmin/Announcements.cshtml`
- `src/Modules/SuperAdmin/Views/SuperAdmin/_AnnouncementFormModal.cshtml`
- `src/Modules/SuperAdmin/Services/IAnnouncementService.cs`
- `src/Modules/SuperAdmin/Services/AnnouncementService.cs`

**Files to modify:**
- `src/Modules/SuperAdmin/Controllers/SuperAdminController.cs` — add actions
- `src/Modules/SuperAdmin/SuperAdminModule.cs` — register service

**Routes:**

| Method | Route | Action |
|--------|-------|--------|
| `GET` | `/super-admin/announcements` | List sent/scheduled announcements |
| `GET` | `/super-admin/announcements/create` | Create form |
| `POST` | `/super-admin/announcements` | Send/schedule announcement |

**What:**
Broadcast notifications from super admin to all tenants or selected tenants.

**Details:**
- Create a `Notification` in each target tenant's database using the existing `Notifications` module infrastructure
- Announcement types: `Info`, `Warning`, `Maintenance`, `FeatureAnnouncement`
- Target options: "All tenants", "Specific tenants" (multi-select), "Tenants on plan X"
- Optional: schedule for future delivery (stored, dispatched via Hangfire job)
- History table: past announcements with delivery count, target, type, timestamp
- Uses MassTransit to dispatch asynchronously across tenant DBs
- Audit logged

---

### 22. Export & Reporting

**Files to modify:**
- `src/Modules/SuperAdmin/Controllers/SuperAdminController.cs` — add export endpoints
- `src/Modules/Audit/Controllers/AuditLogController.cs` — add export endpoint

**New endpoints:**

| Method | Route | Returns |
|--------|-------|---------|
| `GET` | `/super-admin/tenants/export` | CSV — full tenant list with plan, status, users, subscription, dates |
| `GET` | `/super-admin/audit-log/export` | CSV — filtered audit entries matching current filter |
| `GET` | `/super-admin/billing/export/payments` | CSV — payment history with date range |
| `GET` | `/super-admin/billing/export/invoices` | CSV — invoice history with date range |

**Details:**
- Use `StreamWriter` writing directly to response with `Content-Type: text/csv` and `Content-Disposition: attachment`
- Accept same filter parameters as the corresponding list pages
- Audit logged: track who exports what and when
- Filename includes date: `tenants-export-2026-02-21.csv`

---

## New Files Summary

| File | Purpose |
|------|---------|
| `src/Modules/SuperAdmin/Controllers/InfrastructureController.cs` | Health, Redis, RabbitMQ, Logs, Uptime, Jobs pages |
| `src/Modules/SuperAdmin/Controllers/TenantManagementController.cs` | Create, delete, reset, extend trial, impersonate |
| `src/Modules/SuperAdmin/Services/IInfrastructureService.cs` | Infrastructure monitoring interface + models |
| `src/Modules/SuperAdmin/Services/InfrastructureService.cs` | Infrastructure monitoring implementation |
| `src/Modules/SuperAdmin/Services/ITenantInspectionService.cs` | Tenant DB inspection interface + models |
| `src/Modules/SuperAdmin/Services/TenantInspectionService.cs` | Tenant DB inspection implementation |
| `src/Modules/SuperAdmin/Services/ISuperAdminAuditService.cs` | Admin action audit interface |
| `src/Modules/SuperAdmin/Services/SuperAdminAuditService.cs` | Admin action audit implementation |
| `src/Modules/SuperAdmin/Services/IBillingDashboardService.cs` | Billing analytics interface + models |
| `src/Modules/SuperAdmin/Services/BillingDashboardService.cs` | Billing analytics implementation |
| `src/Modules/SuperAdmin/Services/IAnnouncementService.cs` | Announcement/broadcast interface |
| `src/Modules/SuperAdmin/Services/AnnouncementService.cs` | Announcement/broadcast implementation |
| `src/Modules/SuperAdmin/Views/Infrastructure/Health.cshtml` | System health dashboard |
| `src/Modules/SuperAdmin/Views/Infrastructure/Redis.cshtml` | Redis dashboard |
| `src/Modules/SuperAdmin/Views/Infrastructure/RabbitMQ.cshtml` | RabbitMQ embedded UI |
| `src/Modules/SuperAdmin/Views/Infrastructure/Logs.cshtml` | Seq embedded UI |
| `src/Modules/SuperAdmin/Views/Infrastructure/Uptime.cshtml` | Uptime Kuma embedded UI |
| `src/Modules/SuperAdmin/Views/Infrastructure/Jobs.cshtml` | Enhanced Hangfire page |
| `src/Modules/SuperAdmin/Views/SuperAdmin/TenantDatabase.cshtml` | Tenant DB inspector |
| `src/Modules/SuperAdmin/Views/SuperAdmin/TenantHealth.cshtml` | Tenant health overview |
| `src/Modules/SuperAdmin/Views/SuperAdmin/TenantCreate.cshtml` | Admin tenant creation form |
| `src/Modules/SuperAdmin/Views/SuperAdmin/_TenantPlanChangeModal.cshtml` | Plan change modal |
| `src/Modules/SuperAdmin/Views/SuperAdmin/_TenantDeleteModal.cshtml` | Tenant delete modal |
| `src/Modules/SuperAdmin/Views/SuperAdmin/_TenantExtendTrialModal.cshtml` | Trial extension modal |
| `src/Modules/SuperAdmin/Views/SuperAdmin/_ImpersonationBanner.cshtml` | Impersonation warning banner |
| `src/Modules/SuperAdmin/Views/SuperAdmin/_ImpersonateModal.cshtml` | Impersonation user picker |
| `src/Modules/SuperAdmin/Views/SuperAdmin/Billing.cshtml` | Revenue/billing dashboard |
| `src/Modules/SuperAdmin/Views/SuperAdmin/Config.cshtml` | System config viewer |
| `src/Modules/SuperAdmin/Views/SuperAdmin/Admins.cshtml` | Multi-admin management |
| `src/Modules/SuperAdmin/Views/SuperAdmin/_AdminInviteModal.cshtml` | Admin invite modal |
| `src/Modules/SuperAdmin/Views/SuperAdmin/Sessions.cshtml` | Active sessions overview |
| `src/Modules/SuperAdmin/Views/SuperAdmin/RateLimiting.cshtml` | Rate limit config view |
| `src/Modules/SuperAdmin/Views/SuperAdmin/Announcements.cshtml` | Announcements page |
| `src/Modules/SuperAdmin/Views/SuperAdmin/_AnnouncementFormModal.cshtml` | Announcement form |
| `src/Infrastructure/HealthChecks/RedisHealthCheck.cs` | Redis ping health check |
| `src/Infrastructure/HealthChecks/RabbitMqHealthCheck.cs` | RabbitMQ connectivity check |
| `src/Infrastructure/HealthChecks/SeqHealthCheck.cs` | Seq API health check |
| `src/Infrastructure/HealthChecks/DiskSpaceHealthCheck.cs` | Disk space check |
| `src/Infrastructure/HealthChecks/HangfireHealthCheck.cs` | Hangfire server check |

## Modified Files Summary

| File | Changes |
|------|---------|
| `src/Modules/SuperAdmin/Views/Shared/_AdminLayout.cshtml` | Grouped collapsible sidebar |
| `src/Infrastructure/CachingExtensions.cs` | Register `IConnectionMultiplexer` |
| `src/Modules/SuperAdmin/SuperAdminModule.cs` | Register new services + controller paths |
| `src/Modules/Audit/Entities/AuditEntry.cs` | Add `Source` column |
| `src/Modules/SuperAdmin/Controllers/SuperAdminController.cs` | Wire audit + new actions |
| `src/Modules/Audit/Controllers/AuditLogController.cs` | Add Source filter |
| `src/Program.cs` | Register new health checks |
| `src/Modules/SuperAdmin/Views/SuperAdmin/Backups.cshtml` | Enhanced backup detail |
| `src/Infrastructure/Services/LitestreamConfigSyncService.cs` | Add `SyncNowAsync()` |
| `src/Shared/ILitestreamConfigSync.cs` | Add `SyncNowAsync()` interface |
| `src/Modules/SuperAdmin/Views/SuperAdmin/TenantDetail.cshtml` | Add action buttons + inspect link |
| `src/Infrastructure/Middleware/CurrentUserMiddleware.cs` | Detect impersonation claims |
| `src/Shared/ICurrentUser.cs` | Add impersonation properties |
| `src/Modules/Auth/Services/CurrentUser.cs` | Implement impersonation properties |
| `src/Views/Shared/_Layout.cshtml` | Render impersonation banner |
| `docker-compose.yml` | Iframe embedding config |
| `docker-compose.local.yml` | Iframe embedding config |
| `src/Infrastructure/Middleware/SecurityHeadersMiddleware.cs` | CSP frame-src for admin pages |
