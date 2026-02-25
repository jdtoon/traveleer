# Tenancy Module

Core framework module that owns the `Tenant` entity, defines the default roles (Admin, Member), contributes reserved slugs, and declares public route prefixes. This module is the foundation that other modules build upon — it has no controllers or views of its own.

## Structure

```
Tenancy/
├── TenancyModule.cs                     # IModule: roles, reserved slugs, public route prefixes
├── Entities/
│   └── Tenant.cs                        # Core DB: the tenant entity + TenantStatus enum
├── Data/
│   └── TenantCoreConfiguration.cs       # ICoreEntityConfiguration — EF Core mapping
└── Services/
    └── PendingTenantCleanupService.cs   # BackgroundService — cleans up abandoned signups
```

## Entity

**`Tenant`** (Core DB):

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `Guid` | Primary key |
| `Name` | `string` | Display name (e.g. "Acme Corp") |
| `Slug` | `string` | URL slug — unique, lowercase (e.g. `acme-corp`) |
| `ContactEmail` | `string` | Primary contact email |
| `Status` | `TenantStatus` | `PendingSetup`, `Active`, `Suspended`, `Cancelled` |
| `DatabaseName` | `string?` | Tenant DB filename (e.g. `acme-corp.db`) |
| `PlanId` | `Guid` | Current plan (FK → Plan) |
| `IsDeleted` | `bool` | Soft delete flag |
| `DeletedAt` | `DateTime?` | When soft-deleted |
| `ScheduledDeletionAt` | `DateTime?` | Scheduled permanent deletion date |

**Navigation properties:** `Plan`, `ActiveSubscription`, `BillingProfile`, `Invoices`, `Payments`, `AddOns`, `Discounts`, `Credits`

### `TenantStatus` Lifecycle

```
PendingSetup ──→ Active ──→ Suspended ──→ Cancelled
                   │            │
                   │            └──→ Active (reactivated)
                   │
                   └──→ Cancelled (direct cancellation)
```

| Status | Meaning | Tenant Can Access App? |
|--------|---------|----------------------|
| `PendingSetup` | Provisioning in progress | No |
| `Active` | Fully operational | Yes |
| `Suspended` | Blocked (payment failure, admin action) | No (sees suspension page) |
| `Cancelled` | Subscription cancelled | No |

## Default Roles

Defined by this module, created for every new tenant during provisioning:

| Role | Description | System Role? | Permissions |
|------|-------------|-------------|-------------|
| `Admin` | Full access to all features | Yes | All module permissions automatically |
| `Member` | Default member role | Yes | Module-defined subset (typically read-only) |

System roles cannot be deleted by tenant admins. The Admin role always receives all permissions — modules don't need to explicitly map permissions to Admin.

## Reserved Slugs

Framework-level slugs that cannot be used as tenant names:

```
www, app, cdn, docs, help, support, blog, status
```

These are combined with module-contributed slugs (e.g. `register`, `super-admin`, `login`) at startup and enforced during registration slug validation.

## Public Route Prefixes

Framework-level URL prefixes that `TenantResolutionMiddleware` skips (doesn't try to resolve as a tenant):

```
"" (root), health, api, static, assets, favicon.ico
```

Each module contributes its own prefixes (e.g. Marketing adds `pricing`, `about`, `contact`). These are collected in `Program.cs` at startup.

## Background Service

### `PendingTenantCleanupService`

A `BackgroundService` that runs hourly and removes abandoned `PendingSetup` tenants:

- Finds tenants with `Status == PendingSetup` older than `Tenancy:PendingCleanupHours` (default 24h)
- Deletes the tenant record and any orphaned subscription
- Prevents accumulation of incomplete signups

Configuration:

```json
{
  "Tenancy": {
    "PendingCleanupHours": 24,
    "DatabasePath": "db/tenants"
  }
}
```

## How Other Modules Use Tenancy

### Tenant Resolution (Infrastructure)

`TenantResolutionMiddleware` resolves the first URL segment (`/{slug}/...`) to a `Tenant`:
1. Checks if segment matches a public route prefix → skip
2. Looks up `Tenant` by slug (cached in `IMemoryCache` for `TTL:TenantResolutionMinutes`)
3. Populates `ITenantContext` (TenantId, Slug, PlanSlug, TenantName)
4. Suspended tenants → returns 403 with suspension page
5. Unknown slug → passes through (no tenant context set)

### Provisioning (Registration Module)

`TenantProvisionerService` creates new tenants and seeds their databases using the roles/permissions declared by all modules.

### Billing (Billing Module)

The `Tenant` entity has navigation properties to `Subscription`, `Invoices`, `Payments`, etc. The Billing module operates on tenants via `TenantId`.

### Feature Flags

`TenantPlanFeatureFilter` reads the tenant's current `PlanId` to determine feature access.
