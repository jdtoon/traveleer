# Modules

Self-contained vertical-slice feature modules. Each module owns its entities, services, views, permissions, and features.

## Module Contract (`IModule`)

Every module implements `IModule` from `Shared/`. The contract provides:

| Property | Purpose |
|----------|---------|
| `Name` | Human-readable name for logging |
| `Features` | `ModuleFeature` records for core DB seeding — includes `MinPlanSlug` for plan tier |
| `Permissions` | `ModulePermission` records seeded into each tenant DB on provisioning |
| `DefaultRoles` | Role definitions (e.g. Admin, Member) seeded per tenant |
| `DefaultRolePermissions` | Maps permissions to non-admin roles |
| `PublicRoutePrefixes` | URL prefixes that bypass tenant resolution |
| `ReservedSlugs` | Slugs that cannot be used for tenant registration |
| `ControllerViewPaths` | Maps controller names to Razor view folders |
| `PartialViewSearchPaths` | Swap.Htmx partial view search paths |

## Hooks

| Method | When | Purpose |
|--------|------|---------|
| `RegisterServices()` | Startup | Register DI services |
| `RegisterMiddleware()` | Startup | Register module middleware |
| `ConfigureMvc()` | Startup | Configure MVC options |
| `SeedTenantAsync()` | Tenant provisioning | Seed module data into new tenant DB |
| `SeedDemoDataAsync()` | Dev startup (if enabled) | Seed demo/sample data |

## Adding a New Module

1. Create folder: `Modules/MyModule/`
2. Create `MyModuleModule.cs` implementing `IModule`
3. Add entities in `Modules/MyModule/Entities/`
4. Add EF configs in `Modules/MyModule/Data/` implementing the appropriate marker interface
5. Add services in `Modules/MyModule/Services/`
6. Add views in `Modules/MyModule/Views/MyModule/`
7. Register in `Program.cs` module array
8. Add tests in `tests/Modules/MyModule/`

## Current Modules

| Module | Features | Permissions | Description |
|--------|----------|-------------|-------------|
| Tenancy | — | — | Tenant entity, roles, reserved slugs, route prefixes |
| Notes | notes | 4 (CRUD) | Example tenant module with full CRUD |
| TenantAdmin | custom_roles | 10 (users/roles/settings) | User, role, and settings management |
| Audit | audit_log | — | Change tracking via EF interceptor |
| Auth | sso | — | Magic link auth, Identity, cookie schemes |
| Billing | — | — | Paystack integration, plans, subscriptions |
| Marketing | — | — | Public marketing pages |
| SuperAdmin | — | — | Platform administration |
| Registration | — | — | Tenant signup flow |
| Dashboard | — | — | Tenant dashboard |
| FeatureFlags | — | — | Feature flag service |
| Backup | — | — | Litestream config sync |
