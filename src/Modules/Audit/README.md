# Audit Module

Automatic entity change tracking via EF Core interceptors, stored in a dedicated SQLite database.

## Architecture

```
Controller ──▶ TenantDbContext.SaveChanges()
                     │
            AuditSaveChangesInterceptor
            (snapshots before + after save)
                     │
              ChannelAuditWriter
            (fire-and-forget channel)
                     │
               BackgroundService
            (flushes to AuditDbContext)
```

## Components

| File | Purpose |
|------|---------|
| `AuditModule.cs` | Module registration. Feature key: `audit_log`, min plan: `professional` |
| `Entities/AuditEntry.cs` | Entity model — captures entity type, action, old/new values, user, IP |
| `Services/AuditSaveChangesInterceptor.cs` | EF Core `SaveChangesInterceptor` — hooks into `SavingChanges`/`SavedChanges` to snapshot tracked changes |
| `Services/ChannelAuditWriter.cs` | Singleton `BackgroundService` — unbounded channel consumer that flushes entries to `AuditDbContext` |
| `Data/AuditEntryConfiguration.cs` | EF Core entity configuration for `AuditEntry` |
| `Controllers/AuditLogController.cs` | Super-admin UI — paginated log viewer with entity/action/tenant filters |
| `Models/AuditViewModels.cs` | View models for the audit log UI |

## How It Works

1. **Before save**: The interceptor snapshots all tracked `Added`, `Modified`, and `Deleted` entities, capturing old property values.
2. **After save**: The interceptor builds `AuditEntry` records with new values (including DB-generated IDs for inserts) and enqueues them via `ChannelAuditWriter`.
3. **Background flush**: The channel consumer reads entries and writes them to the `AuditDbContext` (separate SQLite DB at `db/audit.db`).
4. **Auditable metadata**: Entities implementing `IAuditableEntity` get `CreatedBy`, `UpdatedBy`, `CreatedAt`, `UpdatedAt` populated automatically.

## Opt-Out

- Apply `[AuditIgnore]` to a class to skip it entirely.
- Apply `[AuditIgnore]` to a property to exclude it from change tracking (e.g., password hashes).

## Feature Flag

The audit log UI is gated behind the `audit_log` feature. The interceptor always runs (writes are cheap), but the viewer requires the feature to be enabled for the tenant's plan.

## Routes

| Method | Path | Description |
|--------|------|-------------|
| GET | `/super-admin/audit-log` | Full audit log page with filters |
| GET | `/super-admin/audit-log/list` | HTMX partial — paginated list |
| GET | `/super-admin/audit-log/detail/{id}` | HTMX modal — entry detail with old/new value diff |
