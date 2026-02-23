# Modules

Self-contained vertical-slice feature modules. Each module owns its entities, services, views, permissions, and features.

## Framework vs App Modules

Modules are split into two categories in `Program.cs`:

### Framework Modules (SaaS engine — do not modify)

These form the reusable SaaS platform. When pulling upstream updates, these should merge cleanly because you haven't touched them.

| Module | Description |
|--------|-------------|
| **Tenancy** | Tenant entity, resolution middleware, reserved slugs, route prefixes |
| **Auth** | Magic link auth, Identity, cookie schemes, 2FA (tenant + SuperAdmin) |
| **Registration** | Tenant signup flow, email verification |
| **Billing** | Paystack integration, plans, subscriptions, invoices, credits |
| **SuperAdmin** | Platform administration dashboard |
| **FeatureFlags** | Plan-gated feature flags |
| **Dashboard** | Tenant dashboard |
| **TenantAdmin** | User, role, and settings management |
| **Audit** | Change tracking via EF interceptor |
| **Notifications** | In-app notification system |
| **Marketing** | Public marketing pages (landing, pricing, contact) |
| **Litestream** | SQLite backup/restore with Litestream |

### App Modules (your project — customize freely)

These are project-specific. Replace or extend them for your own domain.

| Module | Description |
|--------|-------------|
| **Notes** | Example module — demonstrates CRUD, features, permissions, role mappings, events |

> **When starting a new project:** Delete the Notes module, create your own modules in `Modules/YourModule/`, and register them in the "App modules" section of `Program.cs`.

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

See [Framework vs App Modules](#framework-vs-app-modules) above.
