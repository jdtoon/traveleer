# 05 — Feature Flags

> Defines the database-backed feature flag system using Microsoft.FeatureManagement, plan-linked feature resolution, super admin overrides, tag helpers, controller gating, and local development defaults.

**Prerequisites**: [01 — Architecture](01-architecture.md), [02 — Database & Multi-Tenancy](02-database-multitenancy.md), [03 — Modules](03-modules.md)

---

## 1. Why Feature Flags?

Feature flags are **the single most important gating mechanism** in a SaaS application. They control:

1. **Plan-based access** — Which features a tenant gets based on their subscription plan
2. **Progressive rollout** — Enable features for specific tenants before general availability
3. **Kill switches** — Instantly disable a broken feature without deployment
4. **Module visibility** — Hide entire modules from the UI when not available

### Precedence Chain

```
Feature Flag (Is this feature available?)
    │
    ├── Disabled globally (super admin kill switch) → BLOCKED
    ├── Not in tenant's plan → BLOCKED
    ├── Overridden per-tenant by super admin → ALLOWED/BLOCKED
    └── Enabled in plan → ALLOWED
            │
            ▼
      RBAC Permission Check (Does the user have access?)
            │
            └── [HasPermission] filter → ALLOWED/DENIED
```

**Feature flags take absolute precedence over RBAC.** If a feature is disabled, no role or permission can grant access to it.

---

## 2. Microsoft.FeatureManagement Integration

We use [Microsoft.FeatureManagement.AspNetCore](https://github.com/microsoft/FeatureManagement-Dotnet) — the official library — but replace the default configuration-file-based provider with our own **database-backed provider** that reads from `CoreDbContext`.

### NuGet Package

```xml
<PackageReference Include="Microsoft.FeatureManagement.AspNetCore" Version="4.*" />
```

### What We Use from the Library

| Feature | How We Use It |
|---------|---------------|
| `IFeatureManager` | Core interface for checking feature state |
| `IFeatureDefinitionProvider` | **Custom implementation** reading from `CoreDbContext` |
| `IFeatureFilter` | **Custom filter** that checks tenant's plan |
| `[FeatureGate]` attribute | Controller/action gating |
| `<feature>` tag helper | View-level gating |
| `IVariantFeatureManagerSnapshot` | Consistent state within a request |
| Feature middleware | Request-level gating for entire routes |

### What We Do NOT Use

- `appsettings.json`-based feature definitions — we use the database instead
- Azure App Configuration — we're self-hosted with SQLite
- Built-in percentage/time-window filters — our gating is plan-based and tenant-based

---

## 3. Database Schema (Recap)

From [02 — Database & Multi-Tenancy](02-database-multitenancy.md), the relevant entities in `CoreDbContext`:

```csharp
// Feature — defines a toggleable capability
public class Feature : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Key { get; set; }           // "notes", "api_access", "advanced_reports"
    public string Name { get; set; }          // "Notes Module", "API Access"
    public string? Description { get; set; }
    public string? Module { get; set; }       // Which module this feature belongs to
    public bool IsGlobal { get; set; }        // true = always on regardless of plan
    public bool IsEnabled { get; set; }       // Master kill switch (super admin)
    // ... audit fields
}

// PlanFeature — links features to plans
public class PlanFeature
{
    public Guid PlanId { get; set; }
    public Plan Plan { get; set; }
    public Guid FeatureId { get; set; }
    public Feature Feature { get; set; }
    public string? ConfigJson { get; set; }   // Optional per-plan limits/config
}
```

### Additional Entity: TenantFeatureOverride

For super admin per-tenant overrides (e.g., granting a free-plan tenant temporary access to a pro feature):

```csharp
public class TenantFeatureOverride : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid FeatureId { get; set; }
    public Feature Feature { get; set; } = null!;
    public bool IsEnabled { get; set; }           // true = force-enable, false = force-disable
    public string? Reason { get; set; }           // "Trial extension", "Support escalation"
    public DateTime? ExpiresAt { get; set; }      // null = permanent override

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
```

Add to `CoreDbContext`:

```csharp
public DbSet<TenantFeatureOverride> TenantFeatureOverrides => Set<TenantFeatureOverride>();
```

---

## 4. Custom Feature Definition Provider

This replaces the default `IFeatureDefinitionProvider` that reads from `appsettings.json`. Instead, it loads feature definitions from the database.

```csharp
public class DatabaseFeatureDefinitionProvider : IFeatureDefinitionProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DatabaseFeatureDefinitionProvider> _logger;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public DatabaseFeatureDefinitionProvider(
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        ILogger<DatabaseFeatureDefinitionProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    public async IAsyncEnumerable<FeatureDefinition> GetAllFeatureDefinitionsAsync()
    {
        var features = await GetFeaturesFromCacheAsync();

        foreach (var feature in features)
        {
            yield return CreateDefinition(feature);
        }
    }

    public async Task<FeatureDefinition?> GetFeatureDefinitionAsync(string featureName)
    {
        var features = await GetFeaturesFromCacheAsync();
        var feature = features.FirstOrDefault(f => f.Key == featureName);

        return feature is null ? null : CreateDefinition(feature);
    }

    private FeatureDefinition CreateDefinition(FeatureCacheEntry feature)
    {
        var definition = new FeatureDefinition
        {
            Name = feature.Key,
        };

        if (!feature.IsEnabled)
        {
            // Kill switch is off — feature is disabled for everyone
            definition.EnabledFor = [];
        }
        else if (feature.IsGlobal)
        {
            // Global features are always on
            definition.EnabledFor =
            [
                new FeatureFilterConfiguration { Name = "AlwaysOn" }
            ];
        }
        else
        {
            // Plan-based features use our custom TenantPlanFilter
            definition.EnabledFor =
            [
                new FeatureFilterConfiguration
                {
                    Name = "TenantPlan",
                    Parameters = new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["FeatureId"] = feature.Id.ToString(),
                            ["PlanIds"] = string.Join(",", feature.EnabledPlanIds),
                        })
                        .Build()
                }
            ];
        }

        return definition;
    }

    private async Task<List<FeatureCacheEntry>> GetFeaturesFromCacheAsync()
    {
        return await _cache.GetOrCreateAsync("feature-definitions", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

            return await db.Features
                .AsNoTracking()
                .Include(f => f.PlanFeatures)
                .Select(f => new FeatureCacheEntry
                {
                    Id = f.Id,
                    Key = f.Key,
                    IsEnabled = f.IsEnabled,
                    IsGlobal = f.IsGlobal,
                    EnabledPlanIds = f.PlanFeatures.Select(pf => pf.PlanId).ToList()
                })
                .ToListAsync();
        }) ?? [];
    }
}

internal class FeatureCacheEntry
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsGlobal { get; set; }
    public List<Guid> EnabledPlanIds { get; set; } = [];
}
```

### Cache Invalidation

When the super admin changes feature settings (toggle, plan assignment), the cache must be cleared:

```csharp
public class FeatureCacheInvalidator
{
    private readonly IMemoryCache _cache;

    public void Invalidate()
    {
        _cache.Remove("feature-definitions");
        _cache.Remove("tenant-feature-overrides"); // Also clear overrides cache
    }
}
```

---

## 5. Custom Tenant Plan Filter

This `IFeatureFilter` checks whether the current tenant's plan includes the feature:

```csharp
[FilterAlias("TenantPlan")]
public class TenantPlanFeatureFilter : IFeatureFilter
{
    private readonly ITenantContext _tenantContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;

    public TenantPlanFeatureFilter(
        ITenantContext tenantContext,
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache)
    {
        _tenantContext = tenantContext;
        _scopeFactory = scopeFactory;
        _cache = cache;
    }

    public async Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context)
    {
        // Non-tenant requests (marketing, super admin) — features are available
        if (!_tenantContext.IsTenantRequest || _tenantContext.TenantId is null)
            return true;

        var featureId = Guid.Parse(context.Parameters["FeatureId"]);
        var planIds = context.Parameters["PlanIds"]
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(Guid.Parse)
            .ToHashSet();

        // 1. Check per-tenant override first
        var overrideResult = await CheckTenantOverrideAsync(featureId);
        if (overrideResult.HasValue)
            return overrideResult.Value;

        // 2. Check if tenant's plan includes this feature
        var tenantPlanId = await GetTenantPlanIdAsync();
        return tenantPlanId.HasValue && planIds.Contains(tenantPlanId.Value);
    }

    private async Task<bool?> CheckTenantOverrideAsync(Guid featureId)
    {
        var overrides = await GetTenantOverridesAsync();
        var match = overrides.FirstOrDefault(o => o.FeatureId == featureId);

        if (match is null)
            return null; // No override — fall through to plan check

        // Check expiry
        if (match.ExpiresAt.HasValue && match.ExpiresAt.Value < DateTime.UtcNow)
            return null; // Expired override — ignore it

        return match.IsEnabled;
    }

    private async Task<List<TenantOverrideCacheEntry>> GetTenantOverridesAsync()
    {
        var cacheKey = $"tenant-overrides-{_tenantContext.TenantId}";

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

            return await db.TenantFeatureOverrides
                .AsNoTracking()
                .Where(o => o.TenantId == _tenantContext.TenantId!.Value)
                .Select(o => new TenantOverrideCacheEntry
                {
                    FeatureId = o.FeatureId,
                    IsEnabled = o.IsEnabled,
                    ExpiresAt = o.ExpiresAt
                })
                .ToListAsync();
        }) ?? [];
    }

    private async Task<Guid?> GetTenantPlanIdAsync()
    {
        var cacheKey = $"tenant-plan-{_tenantContext.TenantId}";

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

            return await db.Tenants
                .Where(t => t.Id == _tenantContext.TenantId!.Value)
                .Select(t => (Guid?)t.PlanId)
                .FirstOrDefaultAsync();
        });
    }
}

internal class TenantOverrideCacheEntry
{
    public Guid FeatureId { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
```

---

## 6. IFeatureService — Shared Interface

The `IFeatureService` wraps `IFeatureManager` to provide a simpler API and add local development overrides:

```csharp
// Shared/IFeatureService.cs
public interface IFeatureService
{
    /// <summary>
    /// Check if a feature is enabled for the current tenant.
    /// </summary>
    Task<bool> IsEnabledAsync(string featureKey);

    /// <summary>
    /// Get all enabled feature keys for the current tenant.
    /// </summary>
    Task<IReadOnlyList<string>> GetEnabledFeaturesAsync();

    /// <summary>
    /// Get feature configuration for the current tenant's plan (e.g., limits).
    /// Returns null if the feature is not enabled or has no config.
    /// </summary>
    Task<T?> GetConfigAsync<T>(string featureKey) where T : class;
}
```

### Implementation

```csharp
// Modules/FeatureFlags/Services/FeatureService.cs
public class FeatureService : IFeatureService
{
    private readonly IFeatureManager _featureManager;
    private readonly IConfiguration _configuration;

    public FeatureService(IFeatureManager featureManager, IConfiguration configuration)
    {
        _featureManager = featureManager;
        _configuration = configuration;
    }

    public async Task<bool> IsEnabledAsync(string featureKey)
    {
        // Local dev override: all features enabled
        if (_configuration.GetValue<bool>("FeatureFlags:AllEnabledLocally"))
            return true;

        return await _featureManager.IsEnabledAsync(featureKey);
    }

    public async Task<IReadOnlyList<string>> GetEnabledFeaturesAsync()
    {
        var enabled = new List<string>();
        await foreach (var name in _featureManager.GetFeatureNamesAsync())
        {
            if (await IsEnabledAsync(name))
                enabled.Add(name);
        }
        return enabled;
    }

    public async Task<T?> GetConfigAsync<T>(string featureKey) where T : class
    {
        // TODO: Read from PlanFeature.ConfigJson for the current tenant's plan
        // This enables per-plan feature limits (e.g., max 10 notes on free, unlimited on pro)
        return null;
    }
}
```

---

## 7. Feature Registration

### FeatureFlagsModule

```csharp
public class FeatureFlagsModule : IModule
{
    public string Name => "FeatureFlags";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Register our custom feature definition provider
        services.AddSingleton<IFeatureDefinitionProvider, DatabaseFeatureDefinitionProvider>();

        // Register the tenant plan filter
        services.AddScoped<TenantPlanFeatureFilter>();

        // Register Microsoft Feature Management
        services.AddFeatureManagement()
            .AddFeatureFilter<TenantPlanFeatureFilter>();

        // Register our IFeatureService wrapper
        services.AddScoped<IFeatureService, FeatureService>();

        // Cache invalidation helper
        services.AddSingleton<FeatureCacheInvalidator>();
    }
}
```

### Tag Helper Registration

Microsoft.FeatureManagement ships with a `<feature>` tag helper. It's registered via:

```html
<!-- In _ViewImports.cshtml -->
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
@addTagHelper *, Swap.Htmx
@addTagHelper *, Microsoft.FeatureManagement.AspNetCore
@addTagHelper *, saas
```

---

## 8. Usage Patterns

### 8.1 Controller Gating with [FeatureGate]

The entire controller (or specific actions) can be gated behind a feature flag:

```csharp
[FeatureGate("notes")]                          // Feature must be enabled
[Authorize(Policy = "TenantUser")]              // User must be authenticated
public class NotesController : SwapController
{
    [HasPermission(PermissionDefinitions.NotesRead)]  // RBAC check
    public async Task<IActionResult> Index() { ... }
}
```

When a feature is disabled, `[FeatureGate]` returns a **404** by default. We customize this to show a friendly "feature not available" page:

```csharp
// In FeatureFlagsModule.RegisterServices()
services.Configure<FeatureManagementOptions>(options =>
{
    // Custom handler when a gated feature is disabled
    options.UseDisabledFeaturesHandler = true;
});

// Custom disabled feature handler
services.AddScoped<IDisabledFeaturesHandler, FeatureDisabledHandler>();
```

```csharp
public class FeatureDisabledHandler : IDisabledFeaturesHandler
{
    public Task HandleDisabledFeatures(IEnumerable<string> features, ActionExecutingContext context)
    {
        // Return a friendly "upgrade your plan" partial for HTMX requests,
        // or a full page for browser requests
        context.Result = new ViewResult
        {
            ViewName = "Shared/_FeatureDisabled",
            // Pass the feature names so the view can show what plan is needed
        };
        return Task.CompletedTask;
    }
}
```

### 8.2 View Gating with `<feature>` Tag Helper

Microsoft.FeatureManagement provides a built-in tag helper:

```html
<!-- Show only when "notes" feature is enabled -->
<feature name="notes">
    <li>
        <a swap-nav href="/@tenantSlug/notes">
            <svg>...</svg> Notes
        </a>
    </li>
</feature>

<!-- Show upgrade prompt when feature is NOT available -->
<feature name="advanced_reports" negate="true">
    <li class="opacity-50">
        <a href="/@tenantSlug/billing" class="tooltip" data-tip="Upgrade to unlock">
            <svg>...</svg> Advanced Reports
            <span class="badge badge-sm badge-warning">PRO</span>
        </a>
    </li>
</feature>
```

### 8.3 Service-Level Checks

For business logic that needs to check feature availability:

```csharp
public class NotesService : INotesService
{
    private readonly IFeatureService _features;
    private readonly TenantDbContext _db;

    public async Task<Note> CreateAsync(CreateNoteDto dto)
    {
        // Check feature limit (e.g., free plan = max 50 notes)
        var config = await _features.GetConfigAsync<NotesFeatureConfig>("notes");
        if (config?.MaxNotes is not null)
        {
            var count = await _db.Notes.CountAsync();
            if (count >= config.MaxNotes)
                throw new FeatureLimitExceededException("notes", "Maximum notes reached for your plan.");
        }

        // ... create note
    }
}

public class NotesFeatureConfig
{
    public int? MaxNotes { get; set; }  // null = unlimited
}
```

### 8.4 Navigation Generation

The sidebar/navigation dynamically shows only features available to the tenant:

```html
<!-- In _Layout.cshtml or a shared navigation partial -->
<ul class="menu">
    <!-- Always visible for authenticated tenant users -->
    <li><a swap-nav href="/@tenantSlug">Dashboard</a></li>

    <!-- Feature-gated navigation items -->
    <feature name="notes">
        <li><a swap-nav href="/@tenantSlug/notes">Notes</a></li>
    </feature>

    <feature name="projects">
        <li><a swap-nav href="/@tenantSlug/projects">Projects</a></li>
    </feature>

    <feature name="advanced_reports">
        <li><a swap-nav href="/@tenantSlug/reports">Reports</a></li>
    </feature>

    <!-- Always visible for admins -->
    <has-permission name="settings.read">
        <li><a swap-nav href="/@tenantSlug/admin">Admin</a></li>
    </has-permission>
</ul>
```

---

## 9. Feature Definition Conventions

### Feature Key Naming

Feature keys follow a lowercase, underscore-separated convention:

```
notes               # Notes module
projects            # Projects module
api_access          # API access
advanced_reports    # Advanced reporting
custom_roles        # Custom role creation
audit_log           # Tenant-level audit log access
white_label         # White-label branding
sso                 # Single sign-on
export_data         # Data export functionality
```

### Seed Data — Default Features and Plan Assignments

```csharp
public static class FeatureDefinitions
{
    // Feature keys as constants for compile-time safety
    public const string Notes = "notes";
    public const string Projects = "projects";
    public const string ApiAccess = "api_access";
    public const string AdvancedReports = "advanced_reports";
    public const string CustomRoles = "custom_roles";
    public const string AuditLog = "audit_log";
    public const string WhiteLabel = "white_label";
    public const string Sso = "sso";
    public const string ExportData = "export_data";

    public static List<Feature> GetAll() =>
    [
        new() { Key = Notes, Name = "Notes", Module = "Notes", IsGlobal = false, IsEnabled = true },
        new() { Key = Projects, Name = "Projects", Module = "Projects", IsGlobal = false, IsEnabled = true },
        new() { Key = ApiAccess, Name = "API Access", IsGlobal = false, IsEnabled = true },
        new() { Key = AdvancedReports, Name = "Advanced Reports", Module = "Reports", IsGlobal = false, IsEnabled = true },
        new() { Key = CustomRoles, Name = "Custom Roles", Module = "TenantAdmin", IsGlobal = false, IsEnabled = true },
        new() { Key = AuditLog, Name = "Audit Log", Module = "Audit", IsGlobal = false, IsEnabled = true },
        new() { Key = WhiteLabel, Name = "White Label", IsGlobal = false, IsEnabled = true },
        new() { Key = Sso, Name = "Single Sign-On", Module = "Auth", IsGlobal = false, IsEnabled = true },
        new() { Key = ExportData, Name = "Data Export", IsGlobal = false, IsEnabled = true },
    ];
}
```

### Default Plan-Feature Matrix

| Feature | Free | Professional | Enterprise |
|---------|:----:|:------------:|:----------:|
| Notes | — | ✅ | ✅ |
| Projects | — | ✅ | ✅ |
| API Access | — | — | ✅ |
| Advanced Reports | — | — | ✅ |
| Custom Roles | — | ✅ | ✅ |
| Audit Log | — | — | ✅ |
| White Label | — | — | ✅ |
| SSO | — | — | ✅ |
| Data Export | — | ✅ | ✅ |

This matrix is seeded in `CoreDataSeeder` and fully configurable by the super admin at runtime.

---

## 10. Super Admin Feature Management UI

The super admin can manage features from `/super-admin/features` (implemented in the SuperAdmin module, using the FeatureFlags module's services):

### Available Actions

| Action | Effect |
|--------|--------|
| **Toggle feature globally** | Sets `Feature.IsEnabled` — master kill switch |
| **Assign feature to plan** | Creates/removes `PlanFeature` records |
| **Override per tenant** | Creates `TenantFeatureOverride` — temporary or permanent |
| **View feature usage** | See which plans and tenants have access |

### UI Wireframe

```
┌─────────────────────────────────────────────────────────┐
│ Feature Flags Management                                 │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  Feature          Status    Free   Pro   Enterprise     │
│  ─────────────────────────────────────────────────      │
│  🟢 Notes         Enabled    ☐     ☑       ☑           │
│  🟢 Projects      Enabled    ☐     ☑       ☑           │
│  🟢 API Access    Enabled    ☐     ☐       ☑           │
│  🔴 SSO           Disabled   ☐     ☐       ☑           │
│                                                          │
│  [Toggle] toggles the master switch (IsEnabled)         │
│  [Checkboxes] toggle plan assignments (PlanFeature)     │
│                                                          │
├─────────────────────────────────────────────────────────┤
│ Tenant Overrides                           [+ Override]  │
│  ─────────────────────────────────────────────────      │
│  Acme Corp    Notes    Force-Enabled    Expires: Never  │
│  Globex Inc   SSO      Force-Enabled    Expires: 30 Mar │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

---

## 11. Local Development

When developing locally, you typically want **all features enabled** so you can work on any module without plan restrictions.

### Configuration

```json
// appsettings.Development.json
{
  "FeatureFlags": {
    "AllEnabledLocally": true
  }
}
```

When `AllEnabledLocally` is `true`:
- `IFeatureService.IsEnabledAsync()` always returns `true`
- `<feature>` tag helpers still work (they go through `IFeatureManager`)
- `[FeatureGate]` still works (Microsoft.FeatureManagement respects the filter chain)

To test plan-based gating locally, set `AllEnabledLocally` to `false` and configure tenant plans in the seeded data.

---

## 12. Adding a Feature Flag for a New Module

When creating a new application module, follow this checklist:

### Step 1: Define the feature key

```csharp
// In FeatureDefinitions.cs
public const string MyNewFeature = "my_new_feature";
```

### Step 2: Add to seed data

```csharp
// In FeatureDefinitions.GetAll()
new() { Key = MyNewFeature, Name = "My New Feature", Module = "MyModule", IsGlobal = false, IsEnabled = true }
```

### Step 3: Assign to plans in seeder

```csharp
// In CoreDataSeeder
new PlanFeature { PlanId = proPlan.Id, FeatureId = myNewFeatureId },
new PlanFeature { PlanId = enterprisePlan.Id, FeatureId = myNewFeatureId },
```

### Step 4: Gate the controller

```csharp
[FeatureGate(FeatureDefinitions.MyNewFeature)]
[Authorize(Policy = "TenantUser")]
public class MyNewFeatureController : SwapController { ... }
```

### Step 5: Gate the navigation

```html
<feature name="my_new_feature">
    <li><a swap-nav href="/@tenantSlug/my-feature">My Feature</a></li>
</feature>
```

### Step 6: Create a migration (if this is the first run with new features)

New features are seeded on startup. For existing deployments, either:
- Add a data migration that inserts the new feature
- Or rely on the seeder which runs `if (!await db.Features.AnyAsync(f => f.Key == key))` for each feature

---

## 13. Feature Flags Module Files Summary

```
Modules/FeatureFlags/
├── README.md
├── FeatureFlagsModule.cs
├── Services/
│   ├── DatabaseFeatureDefinitionProvider.cs    # IFeatureDefinitionProvider (reads from CoreDbContext)
│   ├── TenantPlanFeatureFilter.cs              # IFeatureFilter (checks tenant plan + overrides)
│   ├── FeatureService.cs                       # IFeatureService (wraps IFeatureManager + local override)
│   ├── FeatureDisabledHandler.cs               # IDisabledFeaturesHandler (friendly "upgrade" page)
│   ├── FeatureCacheInvalidator.cs              # Clears in-memory cache on admin changes
│   └── FeatureDefinitions.cs                   # Static feature key constants + seed data
└── TagHelpers/
    └── (uses Microsoft.FeatureManagement built-in <feature> tag helper)
```

---

## Next Steps

→ [06 — Billing & Paystack](06-billing-paystack.md) for payment integration, subscription lifecycle, and the registration flow.
