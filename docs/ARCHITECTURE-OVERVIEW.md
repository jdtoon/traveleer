# Architecture Overview

High-level architecture of the SaaS starter kit — request lifecycle, data model, module system, and cross-cutting concerns.

---

## Technology Stack

| Layer | Technology |
|-------|-----------|
| Runtime | .NET 10, ASP.NET Core |
| UI | Swap.Htmx (server-driven HTMX), DaisyUI 5, Tailwind CSS v4 |
| Database | SQLite (3-database model), EF Core |
| Payments | Paystack (ZAR, tax-inclusive) |
| Auth | ASP.NET Identity, magic link (passwordless), TOTP 2FA |
| Background Jobs | Hangfire (InMemory / SQLite) |
| Messaging | MassTransit (InMemory / RabbitMQ) |
| Caching | IDistributedCache (Memory / Redis) |
| Logging | Serilog → Console + Seq |
| Backup | Litestream → Cloudflare R2 |
| Bot Protection | Cloudflare Turnstile |

---

## Request Lifecycle

```
HTTP Request
  │
  ├── ResponseCompression
  ├── ForwardedHeaders
  ├── Serilog Request Logging
  ├── SecurityHeadersMiddleware     ← CSP, X-Frame-Options, HSTS
  ├── ExceptionHandler / HSTS       ← production only
  ├── StatusCodePages               ← custom error pages
  ├── HTTPS Redirection
  ├── WebOptimizer                  ← CSS/JS bundling & minification
  ├── Static Files
  ├── Routing
  ├── TenantResolutionMiddleware    ← resolves /{slug}/ → ITenantContext
  ├── RateLimiter                   ← tenant-aware rate limiting
  ├── Authentication                ← cookie auth (Tenant + SuperAdmin schemes)
  ├── SwapHtmx                     ← HTMX layout suppression, navigation targets
  ├── Authorization                 ← policies, [HasPermission], [RequireFeature]
  ├── CurrentUserMiddleware         ← populates ICurrentUser from claims
  │
  └── Controller Action
        ├── SwapView() → Razor View → HTML response
        └── SwapResponse() → HTMX partial + triggers + toasts
```

### Key Middleware Details

**TenantResolutionMiddleware** — Extracts tenant slug from the first URL segment. Looks up slug in `CoreDbContext` (cached via `IDistributedCache` with configurable TTL). Sets `ITenantContext.Slug`, `TenantId`, `TenantName`, `IsTenantRequest`. Skips resolution for public route prefixes declared by modules (e.g., `/super-admin`, `/register`, marketing routes).

**CurrentUserMiddleware** — After authentication, resolves the authenticated user's permissions, tenant role, and display name into `ICurrentUser`. Available via DI throughout the request.

**RateLimiter** — Three policies: `global` (per-IP), `strict` (sensitive operations), `tenant` (uses plan's `MaxRequestsPerMinute`). Placed after tenant resolution so the tenant policy can access plan limits.

---

## Database Architecture

### Three-Database Model

```
db/
├── core.db              ← CoreDbContext (shared)
├── audit.db             ← AuditDbContext (shared)
└── tenants/
    ├── acme.db          ← TenantDbContext (per-tenant)
    ├── demo.db
    └── {slug}.db
```

**Core DB** (`CoreDbContext`) — Platform-wide data:
- Tenants, Plans, Features, PlanFeatures
- Subscriptions, Invoices, Payments, BillingProfiles
- UsageRecords, Discounts
- PendingRegistrations
- SuperAdmins, Announcements

**Audit DB** (`AuditDbContext`) — Entity change tracking:
- Automatic via `AuditSaveChangesInterceptor`
- Captures before/after values for all entity changes
- Opt-out with `[AuditIgnore]` attribute
- Queryable by entity type, tenant, user, and date range

**Tenant DB** (`TenantDbContext` extends `IdentityDbContext`) — Per-tenant isolation:
- ASP.NET Identity: AppUser, AppRole, claims, tokens
- Permissions, RolePermissions
- UserSessions
- Notifications
- App entities (Notes, etc.)

### Dynamic Connection Routing

`TenantDbContext` is registered with a dynamic connection string. On each request, `ITenantContext.Slug` determines the SQLite file path:

```
Request to /acme/notes → TenantContext.Slug = "acme" 
  → Data Source=db/tenants/acme.db
```

Non-tenant requests get an in-memory SQLite instance (no-op).

### Entity Configuration Discovery

EF configurations are auto-discovered via marker interfaces:
- `ICoreEntityConfiguration` → applied to `CoreDbContext`
- `ITenantEntityConfiguration` → applied to `TenantDbContext`

No manual registration needed — just implement the marker on your `IEntityTypeConfiguration<T>`.

---

## Module System

### Architecture

The application is a modular monolith. Each module is a self-contained feature implementing `IModule`:

```
src/Modules/
├── Audit/           ← Entity change tracking
├── Auth/            ← Authentication & 2FA
├── Billing/         ← Subscriptions, invoicing, payments
├── Dashboard/       ← Tenant landing page
├── FeatureFlags/    ← Plan-gated features
├── Litestream/      ← Backup monitoring
├── Marketing/       ← Public pages, pricing, contact
├── Notes/           ← Example app module
├── Notifications/   ← In-app notifications
├── Registration/    ← Tenant signup flow
├── SuperAdmin/      ← Platform administration
├── Tenancy/         ← Core tenant entity & lifecycle
└── TenantAdmin/     ← Tenant settings & team management
```

### Module Contract

Each module declares:

| Method/Property | Purpose |
|----------------|---------|
| `Name` | Module identifier |
| `ControllerViewPaths` | Maps controllers → module view folders |
| `Features` | Feature flags seeded to core DB with minimum plan |
| `Permissions` | Permissions seeded to each tenant DB |
| `DefaultRoles` | Roles created during provisioning |
| `DefaultRolePermissions` | Role → permission mappings |
| `PublicRoutePrefixes` | Routes that skip tenant resolution |
| `ReservedSlugs` | Slugs that cannot be used as tenant names |
| `RegisterServices()` | DI registration |
| `RegisterMiddleware()` | Module-specific middleware |
| `SeedTenantAsync()` | Per-tenant data seeding during provisioning |

### Registration Flow

```
Program.cs
  ├── new IModule[] { ... }              ← Module instantiation
  ├── module.RegisterServices(...)       ← DI registration loop
  ├── Collect ControllerViewPaths        ← View resolution
  ├── Collect PublicRoutePrefixes        ← Route exclusions
  ├── app.Build()
  ├── module.RegisterMiddleware(app)     ← Middleware registration
  ├── app.ConfigurePipeline()            ← Standard pipeline
  ├── app.MapEndpoints()                 ← Route mapping
  └── app.RegisterRecurringJobs()        ← Hangfire jobs
```

---

## Authentication & Authorization

### Dual Auth Schemes

| Scheme | Cookie | Lifetime | Use |
|--------|--------|----------|-----|
| `TenantAuth` | `.saas.tenant` | 12 hours | Tenant users |
| `SuperAdminAuth` | `.saas.superadmin` | 24 hours | Platform admins |

Both use cookie authentication with sliding expiration.

### Auth Flow

```
1. User navigates to /{tenant}/login
2. Enters email → magic link sent 
3. Clicks link → session created (TenantAuth cookie)
4. Optional: Enable TOTP 2FA → prompted on each login
```

### Authorization Layers

| Layer | Mechanism | Example |
|-------|-----------|---------|
| Authentication | `[Authorize(Policy = "TenantUser")]` | Must be logged into a tenant |
| Feature Gating | `[RequireFeature("notes")]` | Tenant's plan must include feature |
| Permission | `[HasPermission("notes.create")]` | User's role must have permission |
| View-level | `<has-permission name="notes.delete">` | Conditional UI rendering |

### Permission Resolution

Permissions flow: Plan → Features → Tenant → Roles → User

1. Plan defines which features are available (`PlanFeature`)
2. Features map to modules via `ModuleFeature`
3. Tenant can override features (`TenantFeatureOverride`)
4. Roles hold permissions (`RolePermission`)
5. User inherits permissions from their role
6. `ICurrentUser` exposes `HasPermission()` for runtime checks

---

## Feature Flag System

```
IsEnabled("notes")
  │
  ├── GlobalToggle filter     ← Is the feature globally enabled?
  ├── TenantOverride filter   ← Does the tenant have a specific override?
  └── PlanMapping filter      ← Does the tenant's plan include this feature?
```

Features are defined in modules:
```csharp
new ModuleFeature("notes", "Notes", "Note taking", "starter")
//                 slug     name    description     min plan
```

The minimum plan parameter gates the feature to tenants on that plan or higher. Features are cached with configurable TTLs and invalidated via `FeatureCacheInvalidator` when plans or overrides change.

---

## Billing Architecture

### Hybrid Model

Billing supports two charge types:
1. **Subscriptions** — Recurring plans via Paystack subscription API
2. **Variable charges** — Per-usage charges via `charge_authorization` on the stored payment method

### Flow

```
Registration → Plan Selection → Paystack Checkout
  │
  ├── Paystack callback → Activate subscription
  ├── Webhook: subscription.charge.success → Invoice + Payment
  ├── Webhook: subscription.charge.failed → Grace period → Dunning
  │
  └── Renewal time (VariableChargeService):
      ├── Query usage metrics for billing period
      ├── Calculate overage charges
      └── charge_authorization on stored card
```

### Key Entities (Core DB)

| Entity | Purpose |
|--------|---------|
| `Plan` | Pricing plan with monthly/annual amounts |
| `Subscription` | Tenant → Plan binding with status lifecycle |
| `Invoice` | Billing records with line items |
| `Payment` | Payment records linked to invoices |
| `BillingProfile` | Stored payment authorization token |
| `UsageRecord` | Metered usage tracking per metric |
| `Discount` | Discount codes (percentage or fixed) |

### Subscription Status Lifecycle

```
Trialing → Active → PastDue → Suspended → Cancelled
                  ↘ Cancelled
```

---

## Tenant Provisioning

When a new tenant is created (via registration or super admin):

```
1. Validate slug (format, reserved words, uniqueness)
2. Create Tenant record in core DB
3. Create Subscription (trialing, 14-day default)
4. Create SQLite file at db/tenants/{slug}.db
5. Run TenantDbContext migrations
6. Sync Litestream config (if enabled)
7. Create Identity roles (Admin, Member + module defaults)
8. Seed permissions (from all modules)
9. Map permissions to roles (Admin gets all; others per DefaultRolePermissions)
10. Call SeedTenantAsync() on each module
11. Create admin user with Admin role
12. Publish TenantCreatedEvent via MassTransit
```

Full rollback on failure — removes subscription, tenant record, and database file.

---

## Provider Switching

Seven services switch implementation via configuration — no code changes:

| Service | Interface | Providers |
|---------|-----------|-----------|
| Email | `IEmailService` | Console, Smtp, MailerSend |
| Billing | `IBillingService` | Mock, Paystack |
| Bot Protection | `IBotProtection` | Mock, Cloudflare Turnstile |
| Storage | `IStorageService` | Local filesystem, Cloudflare R2 |
| Messaging | MassTransit | InMemory, RabbitMQ |
| Caching | `IDistributedCache` | Memory, Redis |
| Jobs | Hangfire | InMemory, SQLite |

All providers are registered in their respective module's `RegisterServices()` method based on the configuration value. See `docs/CONFIGURATION-REFERENCE.md` for the config keys.

---

## Background Jobs

Hangfire manages recurring and fire-and-forget jobs:

| Job | Schedule | Purpose |
|-----|----------|---------|
| `BillingReconciliationJob` | Daily 2 AM | Reconcile billing state |
| `UsageBillingJob` | Daily 1 AM | Process metered charges |
| `DunningJob` | Hourly | Retry failed payments |
| `ExpiredTrialJob` | Daily 6 AM | Handle expired trials |
| `TenantDeletionJob` | Daily 3 AM | Clean up deleted tenants |
| `StaleSessionCleanupJob` | Daily 3:30 AM | Remove expired sessions |
| `DiscountExpiryJob` | Daily 4 AM | Expire past-due discounts |

Three queues: `default`, `emails`, `maintenance`.

Dashboard at `/super-admin/hangfire` (SuperAdmin auth required).

---

## Domain Events

MassTransit handles async domain events with automatic consumer discovery:

```
Service publishes event → MassTransit bus → Consumer processes
```

Built-in events:
- `TenantCreatedEvent` → Sends welcome email
- `TenantPlanChangedEvent` → Logs plan changes
- `SendEmailCommand` → Async email delivery

Retry policy: 1s, 5s, 15s, 30s intervals.

---

## Backup Strategy (Litestream)

When enabled, Litestream continuously replicates all SQLite databases to Cloudflare R2:

```
SQLite WAL changes → Litestream sidecar → R2 bucket
```

- Core DB, Audit DB, and all tenant DBs are replicated
- New tenant DBs are auto-registered (config sync via sentinel file)
- Auto-restore on startup if DB files are missing
- DataProtection keys backed up separately
- SuperAdmin dashboard shows replication status and lag

---

## Health Monitoring

Two health endpoints:

| Endpoint | Scope | Use |
|----------|-------|-----|
| `/health` | Core checks only | Docker HEALTHCHECK, uptime monitors |
| `/health/full` | All checks including infrastructure | SuperAdmin health dashboard |

Checks: core-database, tenant-directory, litestream-readiness, redis, rabbitmq, seq, disk-space, hangfire.

---

## Caching Strategy

Tenant resolution and feature flags are cached via `IDistributedCache`:

| Cache Key Pattern | TTL | Purpose |
|-------------------|-----|---------|
| `tenant:{slug}` | 3 min | Tenant resolution (slug → ID/name) |
| `features:definitions` | 5 min | Global feature definitions |
| `features:overrides:{tenantId}` | 5 min | Per-tenant feature overrides |
| `features:plan:{tenantId}` | 10 min | Tenant's plan ID |
| `ratelimit:plan:{tenantId}` | 5 min | Plan rate limit config |

Cache invalidation happens through `FeatureCacheInvalidator` when plans or overrides change.

---

## Audit Trail

Entity changes are automatically tracked by `AuditSaveChangesInterceptor`:

1. Intercepts `SaveChanges` on CoreDbContext and TenantDbContext
2. Captures entity type, operation (Create/Update/Delete), old/new values
3. Records user ID, tenant slug, timestamp
4. Writes to AuditDbContext asynchronously (background writer)
5. Opt-out per property with `[AuditIgnore]`

SuperAdmin can view audit logs filtered by entity, tenant, user, and date range.
