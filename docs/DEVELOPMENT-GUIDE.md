# Development Guide

Practical guide for building on this starter kit — adding modules, entities, jobs, events, health checks, and email templates.

---

## Quick Start

```bash
# Clone and run
dotnet restore
dotnet run --project src

# Default URLs
# App:        https://localhost:5001
# SuperAdmin: https://localhost:5001/super-admin
```

Development mode (`appsettings.Development.json`) auto-enables:
- `DevSeed` — creates a demo tenant at `/demo` with admin/member users
- `Mock` billing, `Console` email, `Local` storage
- All feature flags enabled globally

---

## Module System

Every feature is a self-contained module implementing `IModule`. Each module lives in `src/Modules/{ModuleName}/` with this structure:

```
src/Modules/YourModule/
├── YourModuleModule.cs        # IModule implementation
├── Controllers/
│   └── YourController.cs
├── Data/
│   └── YourEntityConfiguration.cs
├── Entities/
│   └── YourEntity.cs
├── Events/
│   ├── YourModuleEvents.cs
│   └── YourModuleEventConfig.cs
├── Services/
│   ├── IYourService.cs
│   └── YourService.cs
└── Views/
    ├── _ViewImports.cshtml
    ├── _ViewStart.cshtml
    └── YourController/
        └── Index.cshtml
```

### The IModule Interface

```csharp
public interface IModule
{
    string Name { get; }
    
    // View resolution — maps controller name → module folder
    IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>();
    IReadOnlyList<string> PartialViewSearchPaths => [];
    
    // Feature flags — seeded to core DB
    IReadOnlyList<ModuleFeature> Features => [];
    
    // Permissions and roles — applied during tenant provisioning
    IReadOnlyList<ModulePermission> Permissions => [];
    IReadOnlyList<RoleDefinition> DefaultRoles => [];
    IReadOnlyList<RolePermissionMapping> DefaultRolePermissions => [];
    
    // Routes that bypass tenant resolution
    IReadOnlyList<string> PublicRoutePrefixes => [];
    IReadOnlyList<string> ReservedSlugs => [];
    
    // Lifecycle hooks
    Task SeedTenantAsync(IServiceProvider scopedServices) => Task.CompletedTask;
    Task SeedDemoDataAsync(IServiceProvider scopedServices) => Task.CompletedTask;
    
    // DI registration
    void RegisterServices(IServiceCollection services, IConfiguration configuration);
    void RegisterMiddleware(IApplicationBuilder app) { }
}
```

### Registering a Module

Add your module to the array in `Program.cs`:

```csharp
var modules = new IModule[]
{
    // --- Framework modules ---
    new TenancyModule(), new AuthModule(), new RegistrationModule(),
    new BillingModule(), new SuperAdminModule(), new FeatureFlagsModule(),
    new DashboardModule(), new TenantAdminModule(), new AuditModule(),
    new NotificationsModule(), new MarketingModule(), new LitestreamModule(),
    // --- App modules ---
    new NotesModule(),
    new YourModule(),  // <-- add here
};
```

The startup loop calls `RegisterServices` on each module, collects view paths, and wires everything up automatically.

---

## Adding a New Module (Step by Step)

Use the Notes module (`src/Modules/Notes/`) as a complete reference implementation.

### 1. Create the Module Class

```csharp
namespace saas.Modules.YourModule;

public class YourModuleModule : IModule
{
    public string Name => "YourModule";

    public IReadOnlyDictionary<string, string> ControllerViewPaths =>
        new Dictionary<string, string> { ["YourController"] = "YourModule" };
    
    public IReadOnlyList<string> PartialViewSearchPaths =>
        ["/Modules/YourModule/Views/Shared"];

    public IReadOnlyList<ModuleFeature> Features =>
    [
        new("your_feature", "Your Feature", "Description", "starter")
    ];

    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new("yourmodule.read", "YourModule", "View items"),
        new("yourmodule.create", "YourModule", "Create items"),
        new("yourmodule.edit", "YourModule", "Edit items"),
        new("yourmodule.delete", "YourModule", "Delete items"),
    ];

    public IReadOnlyList<RolePermissionMapping> DefaultRolePermissions =>
    [
        new("Member", ["yourmodule.read", "yourmodule.create", "yourmodule.edit"]),
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IYourService, YourService>();
    }
}
```

Key points:
- `Features` — the 4th parameter is the minimum plan slug; `null` means available on all plans
- `Permissions` — the Admin role gets all permissions automatically; use `DefaultRolePermissions` for Member or custom roles
- `ControllerViewPaths` — maps the controller class name (without "Controller") to the module folder name

### 2. Create the Entity

```csharp
namespace saas.Modules.YourModule.Entities;

public class YourEntity : IAuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    // IAuditableEntity (auto-set by SaveChanges)
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
```

### 3. Create the EF Configuration

For **tenant-scoped** entities (most common):

```csharp
namespace saas.Modules.YourModule.Data;

public class YourEntityConfiguration : IEntityTypeConfiguration<YourEntity>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<YourEntity> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(e => e.CreatedAt);
    }
}
```

For **core** entities (shared across all tenants): use `ICoreEntityConfiguration` instead.

The marker interface (`ITenantEntityConfiguration` / `ICoreEntityConfiguration`) tells EF which DbContext picks up the configuration — no manual registration needed.

### 4. Add the DbSet

**Tenant entity** → add to `TenantDbContext`:
```csharp
public DbSet<YourEntity> YourEntities => Set<YourEntity>();
```

**Core entity** → add to `CoreDbContext`:
```csharp
public DbSet<YourEntity> YourEntities => Set<YourEntity>();
```

### 5. Create and Run EF Migrations

For tenant DB changes:
```bash
dotnet ef migrations add AddYourEntity --context TenantDbContext --output-dir Data/Tenant/Migrations
```

For core DB changes:
```bash
dotnet ef migrations add AddYourEntity --context CoreDbContext --output-dir Data/Core/Migrations
```

Migrations run automatically on startup. Tenant migrations also run during provisioning of new tenants.

### 6. Create the Service

```csharp
namespace saas.Modules.YourModule.Services;

public interface IYourService
{
    Task<PaginatedList<YourEntity>> GetAllAsync(int page = 1, int pageSize = 20);
    Task<YourEntity?> GetByIdAsync(int id);
    Task<YourEntity> CreateAsync(string name);
}

public class YourService(TenantDbContext db) : IYourService
{
    public async Task<PaginatedList<YourEntity>> GetAllAsync(int page = 1, int pageSize = 20)
        => await db.YourEntities
            .OrderByDescending(e => e.CreatedAt)
            .ToPaginatedListAsync(page, pageSize);

    public async Task<YourEntity?> GetByIdAsync(int id)
        => await db.YourEntities.FindAsync(id);

    public async Task<YourEntity> CreateAsync(string name)
    {
        var entity = new YourEntity { Name = name };
        db.YourEntities.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }
}
```

### 7. Create the Controller

```csharp
namespace saas.Modules.YourModule.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature("your_feature")]
public class YourController(IYourService service) : SwapController
{
    [HttpGet("/{tenant}/your-module")]
    [HasPermission("yourmodule.read")]
    public async Task<IActionResult> Index(string tenant, int page = 1)
    {
        var items = await service.GetAllAsync(page);
        return SwapView(items);
    }

    [HttpPost("/{tenant}/your-module")]
    [HasPermission("yourmodule.create")]
    public async Task<IActionResult> Create(string tenant, string name)
    {
        await service.CreateAsync(name);
        return SwapResponse()
            .Toast("Item created!", "success")
            .Trigger(YourModuleEvents.Items.ListChanged)
            .Build();
    }
}
```

Key patterns:
- `[Authorize(Policy = "TenantUser")]` — requires authenticated tenant user
- `[RequireFeature("your_feature")]` — gated by feature flag from plan
- `[HasPermission("yourmodule.read")]` — requires specific permission
- `SwapController` — base class providing `SwapView()` and `SwapResponse()` builders
- Routes always start with `/{tenant}/` for tenant-scoped pages

### 8. Create Views

**`Views/_ViewStart.cshtml`:**
```cshtml
@{ Layout = "_TenantLayout"; }
```

**`Views/_ViewImports.cshtml`:**
```cshtml
@using saas.Modules.YourModule.Entities
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
@addTagHelper *, saas
```

**`Views/YourController/Index.cshtml`:**
```cshtml
@model PaginatedList<YourEntity>

<div hx-trigger="@YourModuleEvents.Items.ListChanged from:body"
     hx-get hx-target="#items-list" hx-select="#items-list">
     
    <div id="items-list">
        @foreach (var item in Model)
        {
            <div class="card bg-base-100 shadow">
                <div class="card-body">
                    <h3>@item.Name</h3>
                </div>
            </div>
        }
    </div>
</div>
```

### 9. Register and Run

Add to `Program.cs` modules array, run the app. The module's features, permissions, and roles are auto-seeded.

---

## Adding a Scheduled Job

Jobs use Hangfire. Create a job class and register it in `SchedulingExtensions.cs`.

### 1. Create the Job

```csharp
namespace saas.Infrastructure.Jobs;

public class YourCleanupJob(CoreDbContext db, ILogger<YourCleanupJob> logger)
{
    public async Task ExecuteAsync()
    {
        logger.LogInformation("Running cleanup job...");
        // Your logic here
        await db.SaveChangesAsync();
    }
}
```

### 2. Register as Recurring

In `SchedulingExtensions.cs`, add to `RegisterRecurringJobs`:

```csharp
RecurringJob.AddOrUpdate<YourCleanupJob>(
    "your-cleanup",
    job => job.ExecuteAsync(),
    Cron.Daily(4, 0),  // 4:00 AM
    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
```

### Existing Job Schedule

| Job | Schedule | Queue |
|-----|----------|-------|
| `BillingReconciliationJob` | Daily 2 AM | default |
| `StaleSessionCleanupJob` | Daily 3:30 AM | maintenance |
| `ExpiredTrialJob` | Daily 6 AM | default |
| `TenantDeletionJob` | Daily 3 AM | maintenance |
| `DunningJob` | Hourly | default |
| `UsageBillingJob` | Daily 1 AM | default |
| `DiscountExpiryJob` | Daily 4 AM | default |

Three Hangfire queues: `default`, `emails`, `maintenance`.

---

## Adding a Health Check

### 1. Create the Check

```csharp
namespace saas.Infrastructure.HealthChecks;

public class YourHealthCheck(IYourDependency? dep = null) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        if (dep is null)
            return HealthCheckResult.Healthy("Not configured");
        
        try
        {
            await dep.PingAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Connection failed", ex);
        }
    }
}
```

### 2. Register in Program.cs

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<YourHealthCheck>("your-service", tags: ["infrastructure"]);
```

Existing health checks: `core-database`, `tenant-directory`, `litestream-readiness`, `redis`, `rabbitmq`, `seq`, `disk-space`, `hangfire`.

---

## Adding Domain Events

Domain events use MassTransit with in-memory (dev) or RabbitMQ (prod) transport.

### 1. Define the Event

```csharp
namespace saas.Modules.YourModule.Events;

public record YourItemCreatedEvent(int ItemId, string TenantSlug, DateTime CreatedAt);
```

### 2. Publish the Event

```csharp
public class YourService(TenantDbContext db, IPublishEndpoint bus) : IYourService
{
    public async Task<YourEntity> CreateAsync(string name, string tenantSlug)
    {
        var entity = new YourEntity { Name = name };
        db.YourEntities.Add(entity);
        await db.SaveChangesAsync();
        
        await bus.Publish(new YourItemCreatedEvent(entity.Id, tenantSlug, DateTime.UtcNow));
        return entity;
    }
}
```

### 3. Create a Consumer

```csharp
namespace saas.Infrastructure.Messaging.Consumers;

public class YourItemCreatedConsumer(ILogger<YourItemCreatedConsumer> logger) 
    : IConsumer<YourItemCreatedEvent>
{
    public Task Consume(ConsumeContext<YourItemCreatedEvent> context)
    {
        logger.LogInformation("Item {Id} created in {Tenant}", 
            context.Message.ItemId, context.Message.TenantSlug);
        return Task.CompletedTask;
    }
}
```

Consumers are auto-discovered by MassTransit (assembly scanning). Retry policy: 1s, 5s, 15s, 30s.

---

## Adding HTMX Events (Swap.Htmx)

Use source-generated type-safe event constants for HTMX triggers:

### 1. Define Events

```csharp
namespace saas.Modules.YourModule.Events;

[SwapEventSource]
public static partial class YourModuleEvents
{
    public static partial class Items
    {
        [SwapEvent("yourmodule.items.listChanged")]
        public static partial string ListChanged { get; }
    }
}
```

### 2. Use in Controller

```csharp
return SwapResponse()
    .Toast("Created!", "success")
    .Trigger(YourModuleEvents.Items.ListChanged)
    .Build();
```

### 3. Use in Views

```html
<div hx-trigger="@YourModuleEvents.Items.ListChanged from:body"
     hx-get="/{tenant}/your-module/list"
     hx-target="this">
</div>
```

---

## Adding Email Templates

Email templates use Mustache-style `{{placeholder}}` syntax and are rendered by `IEmailTemplateRenderer`.

### 1. Create the Template

Add HTML file to `src/EmailTemplates/YourTemplate.html`:

```html
<h2>Hello {{UserName}}!</h2>
<p>Your item <strong>{{ItemName}}</strong> has been processed.</p>
<a href="{{ActionUrl}}" class="btn">View Item</a>
```

Templates are injected into `_Layout.html` which provides the email wrapper (header, footer, responsive container).

### 2. Send the Email

```csharp
var placeholders = new Dictionary<string, string>
{
    ["UserName"] = user.DisplayName,
    ["ItemName"] = item.Name,
    ["ActionUrl"] = $"{siteSettings.BaseUrl}/{tenant}/items/{item.Id}"
};

var html = await templateRenderer.RenderAsync("YourTemplate", placeholders);
await emailService.SendAsync(user.Email, "Item Processed", html);
```

---

## Adding Audit Ignoring

Entity changes are automatically tracked in the audit database. To exclude sensitive fields:

```csharp
public class YourEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    
    [AuditIgnore]
    public string SensitiveToken { get; set; } = "";
}
```

---

## UI Conventions

### DaisyUI 5 + Tailwind CSS v4

All UI uses DaisyUI components with Tailwind utility classes:

```html
<!-- Card -->
<div class="card bg-base-100 shadow">
    <div class="card-body">...</div>
</div>

<!-- Modal (DaisyUI dialog) -->
<dialog id="my-modal" class="modal">
    <div class="modal-box">...</div>
</dialog>

<!-- Button -->
<button class="btn btn-primary">Save</button>
```

### Tag Helpers

```html
<!-- Permission gating -->
<has-permission name="yourmodule.create">
    <button class="btn btn-primary">Create</button>
</has-permission>

<!-- Feature gating -->
<feature name="your_feature">
    <div>Premium content</div>
</feature>
```

### SwapController Response Builders

```csharp
// Full page view
return SwapView(model);

// Partial with toast + trigger
return SwapResponse()
    .Toast("Saved!", "success")
    .Trigger(YourEvents.ListChanged)
    .View("_ItemPartial", model);

// Close modal + refresh
return SwapResponse()
    .CloseModal()
    .Trigger(YourEvents.ListChanged)
    .Build();
```

---

## Database Architecture

### Three-Database Model

| Database | Location | DbContext | Content |
|----------|----------|-----------|---------|
| Core | `db/core.db` | `CoreDbContext` | Tenants, plans, subscriptions, billing, features, super admins |
| Audit | `db/audit.db` | `AuditDbContext` | Entity change history (auto-tracked) |
| Per-tenant | `db/tenants/{slug}.db` | `TenantDbContext` | Identity, app data, notifications |

### Entity Configuration Markers

- `ICoreEntityConfiguration` → picked up by `CoreDbContext`
- `ITenantEntityConfiguration` → picked up by `TenantDbContext`

No manual `DbSet` registration beyond adding the property and config class — EF auto-discovers configurations by marker interface.

### Migration Commands

```bash
# Core DB
dotnet ef migrations add MigrationName --context CoreDbContext --output-dir Data/Core/Migrations

# Tenant DB
dotnet ef migrations add MigrationName --context TenantDbContext --output-dir Data/Tenant/Migrations

# Audit DB
dotnet ef migrations add MigrationName --context AuditDbContext --output-dir Data/Audit/Migrations
```

All migrations run automatically on application startup. Tenant migrations also execute during tenant provisioning.

---

## Testing

See `tests/README.md` for full conventions. Quick reference:

```bash
# Run all unit tests
dotnet test tests/saas.UnitTests

# Run specific test class
dotnet test tests/saas.UnitTests --filter "FullyQualifiedName~BillingServiceTests"
```

Test patterns:
- In-memory SQLite with `new DbContextOptionsBuilder<T>().UseSqlite(connection)`
- Private nested stubs implementing service interfaces with behavioral flags
- `IAsyncDisposable` or `IAsyncLifetime` for test lifecycle
- Services tested against real DB, controllers tested via service stubs

---

## Docker

```bash
# Local development with dependent services
docker compose -f docker-compose.local.yml up

# Production build
docker compose up -d
```

The Dockerfile uses a multi-stage build with the Litestream sidecar wrapper (`litestream-wrapper.sh`).
