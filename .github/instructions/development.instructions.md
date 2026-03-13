---
applyTo: "**"
---

# Development Conventions - traveleer

Stack: ASP.NET Core .NET 10 · Swap.Htmx · DaisyUI 5 · Tailwind CSS v4 · SQLite core/audit plus per-tenant SQLite · EF Core.

These are hard conventions for product work in Traveleer. New domain features, migrations from AgencyCardPro, and redesign work must follow this structure.

---

## Completion Gate

Before ending any turn that changes application code, run:

```powershell
dotnet build src/saas.csproj
dotnet test tests/saas.UnitTests
dotnet test tests/saas.IntegrationTests
```

All three must exit with 0 errors and 0 failures. If any fail, fix them before finishing.

---

## Quality Gate

Every product feature and migrated workflow must pass this pipeline:

1. **UX Design First** - Define the user flow before coding. Plan full-page layout, HTMX interactions, empty states, loading states, error states, responsive behavior, and DaisyUI component usage.
2. **Unit Tests** - Add service-level tests for calculations, validation, numbering, filtering, imports, and domain state transitions.
3. **Integration Tests** - Cover the page shell, HTMX partials, end-to-end user flow, and database state verification for every user-facing feature. Every write operation must assert persisted state by opening the tenant SQLite database directly.
4. **Manual Browser QA** - Use Playwright browser automation to verify UX quality, not just server correctness.
5. **Regression Verification** - Recheck adjacent flows after the feature works.

Do not consider a feature complete until all five steps are satisfied.

---

## Architecture Direction

Traveleer keeps the SaaS starter architecture and absorbs AgencyCardPro behavior into it.

- **Retain** Traveleer framework modules: `Tenancy`, `Auth`, `Registration`, `Billing`, `SuperAdmin`, `FeatureFlags`, `Dashboard`, `TenantAdmin`, `Audit`, `Notifications`, `Marketing`, `Litestream`.
- **Add** domain modules in the `// App modules` section of `src/Program.cs`.
- **Port logic, rebuild UI** when AgencyCardPro patterns conflict with DaisyUI, Tailwind, HTMX, or multi-tenant boundaries.
- **Do not** collapse Traveleer into AgencyCardPro's single-tenant architecture.

---

## Module Structure

Every app feature lives in its own module under `src/Modules/{Module}/`.

```text
src/Modules/{Module}/
|- {Module}Module.cs
|- Controllers/
|  |- {Entity}Controller.cs
|- Data/
|  |- {Entity}Configuration.cs
|- DTOs/
|  |- {Entity}Dto.cs
|- Entities/
|  |- {Entity}.cs
|- Events/
|  |- {Module}Events.cs
|- Services/
|  |- {Feature}Service.cs
|- Views/
|  |- {Entity}/
|     |- Index.cshtml
|     |- _List.cshtml
|     |- _Form.cshtml
|     |- _DeleteConfirm.cshtml
```

### IModule Implementation

Use Traveleer's actual `IModule` contract from `src/Shared/IModule.cs`.

```csharp
public class {Module}Module : IModule
{
    public string Name => "{Module}";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["{Entity}"] = "{Module}"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["{Entity}"];

    public IReadOnlyList<ModuleFeature> Features =>
    [
        new("{module}.{feature}", "{Feature Display Name}")
    ];

    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new("{module}.{feature}.read", "View {Feature}", "{Module}", 0),
        new("{module}.{feature}.create", "Create {Feature}", "{Module}", 1),
        new("{module}.{feature}.edit", "Edit {Feature}", "{Module}", 2),
        new("{module}.{feature}.delete", "Delete {Feature}", "{Module}", 3),
    ];

    public IReadOnlyList<RolePermissionMapping> DefaultRolePermissions =>
    [
        new("Member", "{module}.{feature}.read")
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<I{Feature}Service, {Feature}Service>();
    }
}
```

### Program Registration

After creating a new module, register it in the app-modules section of `src/Program.cs`.

```csharp
var modules = new IModule[]
{
    // Framework modules
    // ...

    // App modules
    new {Module}Module(),
};
```

---

## Controllers

Always inherit `SwapController`. Never inherit `Controller` or `ControllerBase` for user-facing modules.

```csharp
[Authorize(Policy = "TenantUser")]
public class {Entity}Controller : SwapController
{
    private readonly I{Feature}Service _service;

    public {Entity}Controller(I{Feature}Service service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var model = await _service.GetListAsync();
        return SwapView(model);
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var model = await _service.GetListAsync();
        return PartialView("_List", model);
    }

    [HttpGet]
    public IActionResult New() => PartialView("_Form", new {Entity}Dto());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromForm] {Entity}Dto dto)
    {
        if (!ModelState.IsValid)
        {
            return SwapResponse()
                .WithErrorToast("Please fix the errors below.")
                .WithView("_Form", dto)
                .Build();
        }

        await _service.CreateAsync(dto);
        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("{Entity} created.")
            .WithTrigger("{module}.{feature}.refresh")
            .Build();
    }
}
```

### Controller Rules

- Use `SwapView()` for full pages.
- Use `PartialView()` for HTMX partials.
- Use `SwapResponse().Build()` for modal closes, toasts, OOB swaps, and refresh triggers.
- Prefer `[ValidateAntiForgeryToken]` on form posts.
- Keep controllers thin. Push filtering, mapping, calculations, and side effects into services.

---

## Services

Service interfaces and implementations should live in the same file unless there is a strong reason to split them.

```csharp
public interface I{Feature}Service
{
    Task<List<{Entity}Dto>> GetListAsync();
    Task<{Entity}Dto?> GetAsync(Guid id);
    Task CreateAsync({Entity}Dto dto);
    Task UpdateAsync(Guid id, {Entity}Dto dto);
    Task DeleteAsync(Guid id);
}

public class {Feature}Service : I{Feature}Service
{
    private readonly TenantDbContext _db;

    public {Feature}Service(TenantDbContext db)
    {
        _db = db;
    }
}
```

Service rules:

- Keep business rules, numbering, calculations, import logic, and workflow transitions in services.
- Keep HTTP concerns out of services.
- Unit-test service behavior directly.
- If logic belongs to core/platform data, use `CoreDbContext`; if it belongs to a tenant, use `TenantDbContext`.

---

## Database Boundaries

Traveleer uses multiple SQLite databases. Never mix them casually.

| Data belongs to | Context | Notes |
|-----------------|---------|-------|
| SaaS platform data | `CoreDbContext` | Tenants, plans, subscriptions, feature flags, registration state |
| Tenant data | `TenantDbContext` | Bookings, quotes, clients, inventory, settings, workflows |
| Audit trail | `AuditDbContext` | Change history, written via audit infrastructure |

### Entity Registration

1. Add entity class in the module.
2. Add `DbSet<T>` to the correct context.
3. Add an EF configuration class in the module's `Data/` folder.
4. Use the correct marker interface so EF auto-discovers it.

```csharp
public class {Entity}Configuration : IEntityTypeConfiguration<{Entity}>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<{Entity}> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
    }
}
```

---

## Events And HTMX Refreshes

Use explicit event constants for module refreshes and UI coordination.

```csharp
public static class {Module}Events
{
    public const string Refresh = "{module}.{feature}.refresh";
    public const string Created = "{module}.{feature}.created";
    public const string Updated = "{module}.{feature}.updated";
    public const string Deleted = "{module}.{feature}.deleted";
}
```

Prefer shared constants over scattered inline strings once a feature emits more than one event.

---

## Views And UX Patterns

Views must align with DaisyUI + Tailwind patterns already used by Traveleer.

### Full Page

```html
@{
    ViewBag.Title = "{Module}";
}

<div class="flex items-center justify-between mb-6">
    <h1 class="text-2xl font-bold">@ViewBag.Title</h1>
    <button class="btn btn-primary btn-sm"
            hx-get="@Url.Action("New")"
            hx-target="#modal-container">
        + New
    </button>
</div>

<div id="entity-list"
     hx-get="@Url.Action("List")"
     hx-trigger="load, {module}.{feature}.refresh from:body"
     hx-swap="innerHTML show:none">
    <span class="loading loading-spinner loading-lg"></span>
</div>
```

### List Partial

Use DaisyUI tables, cards, filters, badges, and empty states. Prefer readable density over cramped grids.

### Form Partial

Use DaisyUI dialog modals rendered into `#modal-container`.

```html
<dialog class="modal modal-open modal-middle">
    <div class="modal-box max-w-lg">
        <form hx-post="@action" hx-target="#modal-container">
            <div class="space-y-4">
                <div class="form-control w-full">
                    <label class="label" for="Name">
                        <span class="label-text">Name</span>
                    </label>
                    <input id="Name" name="Name" class="input input-bordered w-full" />
                    <span asp-validation-for="Name" class="text-error text-sm mt-1"></span>
                </div>
            </div>

            <div class="modal-action">
                <button type="button" class="btn btn-ghost">Cancel</button>
                <button type="submit" class="btn btn-primary">Save</button>
            </div>
        </form>
    </div>
</dialog>
```

### UX Rules

- Design full desktop and mobile behavior up front.
- Empty states must explain what the user can do next.
- Invalid forms must show inline field errors next to the offending fields.
- Success states must visibly confirm completion through toasts, modal close, refreshed lists, or updated detail panes.
- HTMX interactions must not leave orphaned spinners, stale modals, or broken focus flow.
- Rebuild AgencyCardPro screens to fit the DaisyUI system instead of copying custom CSS verbatim.

---

## What Is Not Allowed

- No `View()` calls for user-facing pages where `SwapView()` should be used.
- No `Controller` or `ControllerBase` inheritance for tenant-facing HTMX modules.
- No inline styles in migrated UI.
- No jQuery or frontend frameworks added to bypass HTMX patterns.
- No cross-context data access that mixes tenant and core concerns inside one workflow without clear orchestration.
- No feature shipping without UX design, tests, and manual QA.
- No direct copy of AgencyCardPro custom CSS as the destination design system.
- No undocumented migration of a complex workflow. High-risk flows need a spec first.

---

## Migration-Specific Rules

- Preserve Traveleer's public registration, tenant resolution, and billing architecture unless an approved ADR says otherwise.
- Treat AgencyCardPro branding as a concept to redesign for multi-tenancy, not a module to copy line-for-line.
- Separate platform marketing branding from tenant in-app branding unless an ADR explicitly merges them.
- When porting a module, document source path, target path, data ownership, UI redesign notes, and required tests before writing the code.
