# Data

EF Core database layer — 3 SQLite databases with marker interface config discovery.

## Architecture

| Database | DbContext | File | Purpose |
|----------|-----------|------|---------|
| Core | `CoreDbContext` | `db/core.db` | Plans, features, tenants, subscriptions, invoices, payments, super admins |
| Tenant | `TenantDbContext` | `db/tenants/{slug}.db` | Per-tenant: users, roles, permissions, notes (Identity tables) |
| Audit | `AuditDbContext` | `db/audit.db` | Change audit log entries |

## Marker Interfaces

Entity configurations are discovered automatically via marker interfaces:

- `ICoreEntityConfiguration` → applied to `CoreDbContext`
- `ITenantEntityConfiguration` → applied to `TenantDbContext`
- `IAuditEntityConfiguration` → applied to `AuditDbContext`

Modules place their EF configurations in `Modules/{Name}/Data/` implementing the appropriate interface.

## Seeders

| Seeder | Location | When |
|--------|----------|------|
| `CoreDataSeeder` | `Data/Core/CoreDataSeeder.cs` | Every startup (idempotent) — plans, features, plan-feature matrix, super admin |
| `DevDataSeeder` | `Data/Core/DevDataSeeder.cs` | Startup when `DevSeed:Enabled=true` — demo tenant, users, sample data |

## Migrations

SQLite WAL mode is enforced via `WalModeInterceptor`. Migrations are applied automatically on startup for all databases including existing tenant DBs.
