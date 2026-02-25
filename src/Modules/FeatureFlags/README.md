# Feature Flags Module

Plan-gated feature flags using Microsoft.FeatureManagement with a custom `TenantPlanFeatureFilter`. Features are declared by modules, stored in the database, and evaluated per-tenant based on their plan tier — with optional per-tenant overrides.

## Structure

```
FeatureFlags/
├── FeatureFlagsModule.cs
├── Entities/
│   ├── Feature.cs                           # Core DB: feature key, name, module, isGlobal
│   ├── PlanFeature.cs                       # Core DB: plan ↔ feature mapping (with optional ConfigJson)
│   └── TenantFeatureOverride.cs             # Core DB: per-tenant override (enable/disable, optional expiry)
├── Data/
│   ├── FeatureCoreConfiguration.cs          # ICoreEntityConfiguration for all 3 entities
│   └── PlanFeatureCoreConfiguration.cs
└── Services/
    ├── FeatureService.cs                    # IFeatureService implementation → wraps IFeatureManager
    ├── DatabaseFeatureDefinitionProvider.cs  # IFeatureDefinitionProvider → loads features from DB, caches in memory
    ├── TenantPlanFeatureFilter.cs           # IFeatureFilter → evaluates per-tenant overrides, then plan-based access
    └── FeatureCacheInvalidator.cs           # Invalidates feature definition + tenant-specific caches
```

## How Feature Evaluation Works

```
IsEnabledAsync("notes")
    │
    ▼
Microsoft.FeatureManagement → DatabaseFeatureDefinitionProvider
    │
    ├─ Feature.IsEnabled == false? → DISABLED (killed globally)
    ├─ Feature.IsGlobal == true?   → ENABLED (available to all)
    │
    ▼
TenantPlanFeatureFilter.EvaluateAsync()
    │
    ├─ TenantFeatureOverride exists for this tenant?
    │     ├─ Not expired? → use override (ENABLED or DISABLED)
    │     └─ Expired? → fall through to plan check
    │
    ├─ PlanFeature mapping exists for tenant's current plan?
    │     └─ YES → ENABLED
    │
    └─ No mapping → DISABLED
```

### Plan-Feature Matrix (Seeded by `CoreDataSeeder`)

Features declare a `MinPlanSlug` (e.g. `"starter"`). The seeder assigns each feature to all plans with `SortOrder >=` the minimum plan's `SortOrder`:

```
Feature: "notes" (MinPlanSlug: "starter", SortOrder: 1)
  → Assigned to: Starter (1), Professional (2), Enterprise (3)
  → NOT assigned to: Free (0)

Feature: "audit_log" (MinPlanSlug: "professional", SortOrder: 2)
  → Assigned to: Professional (2), Enterprise (3)
  → NOT assigned to: Free (0), Starter (1)
```

## Declaring Features in Your Module

In your module class, declare features via the `Features` property:

```csharp
public class MyModule : IModule
{
    public string Name => "MyModule";

    public IReadOnlyList<ModuleFeature> Features =>
    [
        new("my_feature", "My Feature", Description: "Does something cool", MinPlanSlug: "starter"),
        new("premium_reports", "Premium Reports", MinPlanSlug: "professional"),
        new("global_thing", "Global Thing", IsGlobal: true)  // available to all plans
    ];
}
```

Features are seeded to the database on first startup and incrementally synced on subsequent boots (new features added, existing ones preserved).

## Using Feature Flags

### In Controllers — `[RequireFeature]` attribute

```csharp
[RequireFeature("notes")]
[HttpGet]
public IActionResult Index() => SwapView();
```

Returns 404 if the feature is not enabled for the current tenant.

### In Services — `IFeatureService`

```csharp
public class MyService
{
    private readonly IFeatureService _features;

    public async Task DoSomethingAsync()
    {
        if (await _features.IsEnabledAsync("premium_reports"))
        {
            // premium path
        }
    }
}
```

### In Views — `<feature>` tag helper

```html
<feature name="premium_reports">
    <div>Premium content visible only to enabled tenants</div>
</feature>
```

## Per-Tenant Overrides

Super admins can override feature access for individual tenants via the SuperAdmin dashboard:

- **Enable** a feature for a tenant that wouldn't normally have it (e.g. trial of a premium feature)
- **Disable** a feature for a specific tenant (e.g. abuse prevention)
- **Set an expiry** — override automatically expires and falls back to plan-based evaluation

Overrides are managed through `SuperAdminService.SaveTenantFeatureOverrideAsync()`.

## Caching

Feature definitions and tenant overrides are cached in `IMemoryCache`:

| Cache Key | TTL | Content |
|-----------|-----|---------|
| `feature-definitions` | `Caching:TTL:FeatureDefinitionsMinutes` (default 5) | All features + their plan mappings |
| `tenant-overrides-{tenantId}` | `Caching:TTL:TenantOverridesMinutes` (default 5) | Per-tenant override list |
| `tenant-plan-{tenantId}` | `Caching:TTL:TenantPlanMinutes` (default 10) | Tenant's current plan ID |

Call `FeatureCacheInvalidator.Invalidate()` after changing features/plan mappings, or `InvalidateTenant(tenantId)` after changing a tenant's plan or overrides.

## Services Reference

| Service | Interface | Purpose |
|---------|-----------|---------|
| `FeatureService` | `IFeatureService` | `IsEnabledAsync(key)`, `GetEnabledFeaturesAsync()` |
| `DatabaseFeatureDefinitionProvider` | `IFeatureDefinitionProvider` | Loads features from Core DB, caches definitions |
| `TenantPlanFeatureFilter` | `IFeatureFilter` | Evaluates tenant override → plan mapping chain |
| `FeatureCacheInvalidator` | — | `Invalidate()`, `InvalidateTenant(tenantId)` |

## Configuration

No dedicated config section. Uses cache TTL settings from `Caching:TTL` in `appsettings.json`:

```json
{
  "Caching": {
    "TTL": {
      "FeatureDefinitionsMinutes": 5,
      "TenantOverridesMinutes": 5,
      "TenantPlanMinutes": 10
    }
  }
}
```
