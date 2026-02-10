# Core Database

Plans, features, tenants, subscriptions, invoices, payments, and super admin records.

## Key Files

| File | Purpose |
|------|---------|
| `CoreDbContext.cs` | EF Core context — auto-discovers configs via `ICoreEntityConfiguration` |
| `CoreDataSeeder.cs` | Seeds plans, features (from modules), plan-feature matrix (via `MinPlanSlug`), super admin |
| `DevDataSeeder.cs` | Seeds demo tenant + users for local development (when `DevSeed:Enabled=true`) |
| `ICoreEntityConfiguration.cs` | Marker interface for entity configs belonging to this context |
| `WalModeInterceptor.cs` | Enforces SQLite WAL mode on connection open |

## Plan-Feature Matrix

Features declare their minimum plan tier via `ModuleFeature.MinPlanSlug`. The seeder assigns each feature to all plans with `SortOrder >=` the minimum plan's `SortOrder`:

| Plan | SortOrder | Gets Features Where MinPlanSlug... |
|------|-----------|-----------------------------------|
| Free | 0 | is `null` (available to all plans) |
| Starter | 1 | is `null` or `"free"` or `"starter"` |
| Professional | 2 | is `null`, `"free"`, `"starter"`, or `"professional"` |
| Enterprise | 3 | ALL features |
