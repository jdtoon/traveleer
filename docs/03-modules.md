# 03 — Modules

> Defines the module contract, registration pattern, shared interface conventions, and a complete inventory of every module in the SaaS starter kit.

**Prerequisites**: [01 — Architecture](01-architecture.md), [02 — Database & Multi-Tenancy](02-database-multitenancy.md)

---

## 1. What Is a Module?

A module is a **self-contained vertical slice** of functionality. It owns everything it needs — controllers, entities, services, views, events, middleware, tag helpers, DTOs — and exposes nothing except through **shared interfaces** or its registered routes.

### Core Rules

1. **Self-contained** — A module folder contains ALL files for that feature. No scattering across top-level folders.
2. **Own README** — Every module has a `README.md` describing its purpose, dependencies, configuration, and routes.
3. **No cross-module direct references** — Modules communicate via shared interfaces in `Shared/`. Never reference another module's concrete class.
4. **One registration entry point** — `{Name}Module.cs` implements `IModule` and is the single place the module registers its services and middleware.
5. **Swap.Htmx first** — All controllers inherit `SwapController`. All views use Swap.Htmx patterns. No standard MVC returns.
6. **Transferable** — Any module can be copied to another Swap.Htmx project and registered there with minimal wiring.

---

## 2. Module Contract

### IModule Interface

Every module implements this interface:

```csharp
public interface IModule
{
    /// <summary>
    /// Human-readable module name for logging and diagnostics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Register services (DI), entity configurations, and options for this module.
    /// Called during ConfigureServices.
    /// </summary>
    void RegisterServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// Register middleware specific to this module (if any).
    /// Called during Configure, after global middleware is registered.
    /// </summary>
    void RegisterMiddleware(IApplicationBuilder app) { }

    /// <summary>
    /// Register MVC-related configuration: partial view paths, view location mappings.
    /// Called during AddControllersWithViews configuration.
    /// </summary>
    void RegisterMvc(MvcOptions mvcOptions, IMvcBuilder mvcBuilder) { }
}
```

### Folder Structure Convention

Every module follows this structure (include only what's needed — omit empty folders):

```
Modules/{ModuleName}/
├── README.md                    # Module documentation
├── {ModuleName}Module.cs        # IModule implementation
├── Controllers/                 # SwapController subclasses
│   └── {Name}Controller.cs
├── Entities/                    # EF Core entity classes
│   └── {Entity}.cs
├── Services/                    # Business logic
│   ├── I{Name}Service.cs       # Interface (can also go in Shared/ if cross-module)
│   └── {Name}Service.cs        # Implementation
├── DTOs/                        # Request/response models
│   └── {Name}Dto.cs
├── Events/                      # Swap.Htmx event definitions
│   ├── {Name}EventConfig.cs    # [SwapEventConfig] class
│   └── {Name}Events.cs         # ISwapEventChainConfig implementation
├── Middleware/                   # Module-specific middleware
│   └── {Name}Middleware.cs
├── TagHelpers/                  # Module-specific tag helpers
│   └── {Name}TagHelper.cs
└── Views/                       # Razor views
    ├── _ViewImports.cshtml
    ├── Index.cshtml
    └── _{Partial}.cshtml
```

### Module Registration in Program.cs

```csharp
// Discover and register all modules
var modules = new IModule[]
{
    // Platform modules (order matters for middleware)
    new AuditModule(),
    new FeatureFlagsModule(),
    new AuthModule(),
    new BillingModule(),
    new RegistrationModule(),
    new MarketingModule(),
    new SuperAdminModule(),
    new TenantAdminModule(),
    new BackupModule(),

    // Application modules
    new NotesModule(),
    // new YourNewModule(),  ◄── Add new modules here
};

// Register services
foreach (var module in modules)
{
    module.RegisterServices(builder.Services, builder.Configuration);
    logger.LogInformation("Registered module: {Module}", module.Name);
}

// Configure MVC with module view locations
builder.Services.AddControllersWithViews(options =>
{
    foreach (var module in modules)
        module.RegisterMvc(options, mvcBuilder);
})
.AddSwapHtmx();

// Build app
var app = builder.Build();

// Register module middleware
foreach (var module in modules)
    module.RegisterMiddleware(app);
```

---

## 3. Shared Interfaces

The `Shared/` folder contains **interfaces only** — never implementations. These interfaces allow cross-module communication without coupling.

### File Location

```
src/Shared/
├── ITenantContext.cs          # Current request's tenant info
├── ICurrentUser.cs            # Current authenticated user info
├── IFeatureService.cs         # Feature flag checks
├── IEmailService.cs           # Send emails abstraction
├── IAuditWriter.cs            # Write audit entries
├── IBotProtection.cs          # Bot validation (Turnstile)
├── IBackupService.cs          # Trigger backup operations
├── ITenantProvisioner.cs      # Provision new tenant databases
├── IBillingService.cs         # Billing operations abstraction
└── IModule.cs                 # Module contract itself
```

### Key Interface Definitions

```csharp
// ITenantContext — Set by TenantResolutionMiddleware, consumed everywhere
public interface ITenantContext
{
    string? Slug { get; }
    Guid? TenantId { get; }
    string? PlanSlug { get; }
    string? TenantName { get; }
    bool IsTenantRequest { get; }
}

// ICurrentUser — Set by auth middleware, consumed by controllers/services
public interface ICurrentUser
{
    string? UserId { get; }
    string? Email { get; }
    string? DisplayName { get; }
    bool IsAuthenticated { get; }
    bool IsSuperAdmin { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<string> Permissions { get; }
    bool HasPermission(string permission);
}

// IFeatureService — Wraps Microsoft.FeatureManagement with tenant context
public interface IFeatureService
{
    Task<bool> IsEnabledAsync(string featureKey);
    Task<IReadOnlyList<string>> GetEnabledFeaturesAsync();
}

// IEmailService — Abstraction over email delivery
public interface IEmailService
{
    Task SendAsync(string to, string subject, string htmlBody);
    Task SendMagicLinkAsync(string to, string magicLinkUrl);
}

// IAuditWriter — Fire-and-forget audit trail
public interface IAuditWriter
{
    ValueTask WriteAsync(AuditEntry entry);
}

// IBotProtection — Validate Turnstile token
public interface IBotProtection
{
    Task<bool> ValidateAsync(string? token);
}
```

### Dependency Flow

```
Shared Interfaces ◄── Module A reads interface
       │
       └── Module B provides implementation
```

Example:
- `IEmailService` is defined in `Shared/`
- `AuthModule` depends on `IEmailService` to send magic links
- `Infrastructure` registers either `ConsoleEmailService` or `AwsSesEmailService` based on config
- `AuthModule` never knows or cares which implementation is active

---

## 4. Module Inventory

### 4.1 Marketing Module

| | |
|---|---|
| **Purpose** | Public-facing marketing website (landing, pricing, about) |
| **Database** | Reads from `CoreDbContext` (plans for pricing page) |
| **Auth Required** | ❌ No — fully public |
| **Tenant Scoped** | ❌ No |
| **Detail Doc** | [09 — Marketing](09-marketing.md) |

**Folder**: `Modules/Marketing/`

**Routes**:
| Route | Action | Description |
|-------|--------|-------------|
| `GET /` | `Index` | Landing page / homepage |
| `GET /pricing` | `Pricing` | Plan comparison with prices from DB |
| `GET /about` | `About` | About page |

**Key Files**:
```
Marketing/
├── README.md
├── MarketingModule.cs
├── Controllers/
│   └── MarketingController.cs
└── Views/
    ├── _ViewImports.cshtml
    ├── Index.cshtml         # Landing page
    ├── Pricing.cshtml       # Plan cards from CoreDbContext
    └── About.cshtml
```

**Dependencies**: `CoreDbContext` (read-only, for plan data)

---

### 4.2 Auth Module

| | |
|---|---|
| **Purpose** | Authentication for super admins and tenant users via magic link |
| **Database** | `CoreDbContext` (magic link tokens, super admin lookup), `TenantDbContext` (Identity for tenant users) |
| **Auth Required** | ❌ No — this IS the auth system |
| **Tenant Scoped** | Both — super admin routes are non-tenant, tenant login routes are tenant-scoped |
| **Detail Doc** | [04 — Authentication & Authorization](04-auth.md) |

**Folder**: `Modules/Auth/`

**Routes**:
| Route | Action | Description |
|-------|--------|-------------|
| `GET /login` | `SuperAdminLogin` | Super admin login page |
| `POST /login` | `SendSuperAdminMagicLink` | Send magic link email |
| `GET /magic-link/{token}` | `VerifySuperAdminMagicLink` | Verify and authenticate super admin |
| `POST /logout` | `Logout` | Logout (any context) |
| `GET /{tenant}/login` | `TenantLogin` | Tenant user login page |
| `POST /{tenant}/login` | `SendTenantMagicLink` | Send magic link to tenant user |
| `GET /{tenant}/magic-link/{token}` | `VerifyTenantMagicLink` | Verify and authenticate tenant user |

**Key Files**:
```
Auth/
├── README.md
├── AuthModule.cs
├── Controllers/
│   ├── SuperAdminAuthController.cs
│   └── TenantAuthController.cs
├── Services/
│   ├── MagicLinkService.cs          # Generate, store, verify tokens
│   └── AuthCookieService.cs         # Issue/validate auth cookies
├── Middleware/
│   └── CurrentUserMiddleware.cs     # Populates ICurrentUser from cookie
├── Views/
│   ├── _ViewImports.cshtml
│   ├── SuperAdminLogin.cshtml
│   ├── TenantLogin.cshtml
│   ├── MagicLinkSent.cshtml         # "Check your email" confirmation
│   └── MagicLinkExpired.cshtml      # Expired/invalid token page
└── TagHelpers/
    ├── HasPermissionTagHelper.cs    # <has-permission name="notes.create">
    └── IsSuperAdminTagHelper.cs     # <is-super-admin>
```

**Dependencies**: `IEmailService`, `CoreDbContext`, `TenantDbContext`, `ITenantContext`

---

### 4.3 Registration Module

| | |
|---|---|
| **Purpose** | New tenant signup — collect info, select plan, process payment, provision tenant DB |
| **Database** | `CoreDbContext` (create tenant, subscription, billing records) |
| **Auth Required** | ❌ No — public registration |
| **Tenant Scoped** | ❌ No — creates a tenant |
| **Detail Doc** | [06 — Billing & Paystack](06-billing-paystack.md) |

**Folder**: `Modules/Registration/`

**Routes**:
| Route | Action | Description |
|-------|--------|-------------|
| `GET /register` | `Index` | Registration form (name, email, slug, plan selection) |
| `POST /register` | `Register` | Validate, create tenant, init billing, provision DB |
| `GET /register/verify/{token}` | `VerifyEmail` | Email verification step |
| `GET /register/success` | `Success` | Welcome page after successful registration |

**Key Files**:
```
Registration/
├── README.md
├── RegistrationModule.cs
├── Controllers/
│   └── RegistrationController.cs
├── Services/
│   └── RegistrationService.cs       # Orchestrates the full signup flow
├── DTOs/
│   └── RegisterRequest.cs
├── Views/
│   ├── _ViewImports.cshtml
│   ├── Index.cshtml                  # Multi-step registration form
│   ├── _PlanSelector.cshtml          # Plan selection partial
│   ├── _SlugValidator.cshtml         # Real-time slug availability check
│   ├── VerifyEmail.cshtml
│   └── Success.cshtml
└── Events/
    ├── RegistrationEventConfig.cs
    └── RegistrationEvents.cs
```

**Dependencies**: `IBillingService`, `ITenantProvisioner`, `IBotProtection`, `IEmailService`, `CoreDbContext`

---

### 4.4 SuperAdmin Module

| | |
|---|---|
| **Purpose** | Back-office for the SaaS owner to manage tenants, plans, features, billing, system health |
| **Database** | `CoreDbContext` (full CRUD), can read any `TenantDbContext` for support |
| **Auth Required** | ✅ Super admin only |
| **Tenant Scoped** | ❌ No — operates at platform level |
| **Detail Doc** | — (covered across multiple docs) |

**Folder**: `Modules/SuperAdmin/`

**Routes**:
| Route | Action | Description |
|-------|--------|-------------|
| `GET /super-admin` | `Dashboard` | Overview: tenant count, revenue, active subs |
| `GET /super-admin/tenants` | `Tenants` | List all tenants with status, plan, actions |
| `GET /super-admin/tenants/{id}` | `TenantDetail` | Tenant detail with billing history, support actions |
| `POST /super-admin/tenants/{id}/suspend` | `SuspendTenant` | Suspend a tenant |
| `POST /super-admin/tenants/{id}/activate` | `ActivateTenant` | Reactivate a tenant |
| `GET /super-admin/plans` | `Plans` | List/manage plans |
| `GET /super-admin/plans/create` | `CreatePlan` | Create plan form |
| `POST /super-admin/plans` | `SavePlan` | Save new/updated plan |
| `GET /super-admin/features` | `Features` | List/manage features |
| `POST /super-admin/features/{id}/toggle` | `ToggleFeature` | Enable/disable a feature globally |
| `GET /super-admin/billing` | `BillingOverview` | Revenue dashboard, recent payments |
| `GET /super-admin/audit` | `AuditLog` | Search and browse audit entries |

**Key Files**:
```
SuperAdmin/
├── README.md
├── SuperAdminModule.cs
├── Controllers/
│   ├── SuperAdminDashboardController.cs
│   ├── SuperAdminTenantsController.cs
│   ├── SuperAdminPlansController.cs
│   ├── SuperAdminFeaturesController.cs
│   ├── SuperAdminBillingController.cs
│   └── SuperAdminAuditController.cs
├── Services/
│   ├── TenantManagementService.cs
│   └── DashboardService.cs
├── DTOs/
│   ├── TenantListDto.cs
│   ├── PlanFormDto.cs
│   └── FeatureFormDto.cs
├── Views/
│   ├── _ViewImports.cshtml
│   ├── _SuperAdminLayout.cshtml     # Dedicated layout with admin nav
│   ├── Dashboard.cshtml
│   ├── Tenants/
│   │   ├── Index.cshtml
│   │   ├── _TenantList.cshtml
│   │   ├── _TenantDetail.cshtml
│   │   └── _TenantActions.cshtml
│   ├── Plans/
│   │   ├── Index.cshtml
│   │   ├── _PlanList.cshtml
│   │   └── _PlanForm.cshtml
│   ├── Features/
│   │   ├── Index.cshtml
│   │   └── _FeatureList.cshtml
│   ├── Billing/
│   │   └── Index.cshtml
│   └── Audit/
│       ├── Index.cshtml
│       └── _AuditEntryList.cshtml
└── Events/
    ├── SuperAdminEventConfig.cs
    └── SuperAdminEvents.cs
```

**Dependencies**: `CoreDbContext`, `AuditDbContext`, `ICurrentUser` (super admin check)

---

### 4.5 TenantAdmin Module

| | |
|---|---|
| **Purpose** | Tenant-level admin panel — user management, role/permission CRUD, tenant settings |
| **Database** | `TenantDbContext` |
| **Auth Required** | ✅ Tenant user with admin permissions |
| **Tenant Scoped** | ✅ Yes |
| **Detail Doc** | [04 — Authentication & Authorization](04-auth.md) |

**Folder**: `Modules/TenantAdmin/`

**Routes** (all prefixed with `/{tenant}`):
| Route | Action | Description |
|-------|--------|-------------|
| `GET /{tenant}/admin` | `Dashboard` | Tenant admin overview |
| `GET /{tenant}/admin/users` | `Users` | List users |
| `POST /{tenant}/admin/users/invite` | `InviteUser` | Send invite magic link |
| `GET /{tenant}/admin/users/{id}` | `UserDetail` | Edit user, assign roles |
| `POST /{tenant}/admin/users/{id}/deactivate` | `DeactivateUser` | Deactivate user |
| `GET /{tenant}/admin/roles` | `Roles` | List roles |
| `GET /{tenant}/admin/roles/create` | `CreateRole` | Create role form |
| `POST /{tenant}/admin/roles` | `SaveRole` | Save new/updated role |
| `GET /{tenant}/admin/roles/{id}` | `RoleDetail` | Edit role permissions |
| `POST /{tenant}/admin/roles/{id}/permissions` | `UpdatePermissions` | Toggle permissions on role |
| `GET /{tenant}/admin/settings` | `Settings` | Tenant settings |

**Key Files**:
```
TenantAdmin/
├── README.md
├── TenantAdminModule.cs
├── Controllers/
│   ├── TenantAdminDashboardController.cs
│   ├── UserManagementController.cs
│   ├── RoleManagementController.cs
│   └── TenantSettingsController.cs
├── Services/
│   ├── UserService.cs
│   └── RoleService.cs
├── DTOs/
│   ├── UserListDto.cs
│   ├── InviteUserDto.cs
│   ├── RoleFormDto.cs
│   └── PermissionGroupDto.cs
├── Views/
│   ├── _ViewImports.cshtml
│   ├── Dashboard.cshtml
│   ├── Users/
│   │   ├── Index.cshtml
│   │   ├── _UserList.cshtml
│   │   ├── _InviteModal.cshtml
│   │   └── _UserDetail.cshtml
│   ├── Roles/
│   │   ├── Index.cshtml
│   │   ├── _RoleList.cshtml
│   │   ├── _RoleForm.cshtml
│   │   └── _PermissionMatrix.cshtml  # Checkbox grid of permissions
│   └── Settings/
│       └── Index.cshtml
└── Events/
    ├── TenantAdminEventConfig.cs
    └── TenantAdminEvents.cs
```

**Dependencies**: `TenantDbContext`, `ICurrentUser`, `IFeatureService`, `IEmailService`, `ITenantContext`

---

### 4.6 Billing Module

| | |
|---|---|
| **Purpose** | Paystack integration, subscription management, invoices, payments, webhooks |
| **Database** | `CoreDbContext` (subscriptions, invoices, payments) |
| **Auth Required** | Varies — webhooks are unauthenticated (signature-verified) |
| **Tenant Scoped** | Mixed — webhook endpoint is global, tenant billing pages are tenant-scoped |
| **Detail Doc** | [06 — Billing & Paystack](06-billing-paystack.md) |

**Folder**: `Modules/Billing/`

**Routes**:
| Route | Action | Description |
|-------|--------|-------------|
| `POST /api/webhooks/paystack` | `HandleWebhook` | Paystack webhook receiver |
| `GET /{tenant}/billing` | `Index` | Tenant's billing dashboard |
| `GET /{tenant}/billing/invoices` | `Invoices` | Invoice history |
| `GET /{tenant}/billing/subscription` | `Subscription` | Current subscription details |
| `POST /{tenant}/billing/subscription/change` | `ChangePlan` | Upgrade/downgrade plan |
| `POST /{tenant}/billing/subscription/cancel` | `CancelSubscription` | Cancel subscription |

**Key Files**:
```
Billing/
├── README.md
├── BillingModule.cs
├── Controllers/
│   ├── PaystackWebhookController.cs
│   └── TenantBillingController.cs
├── Services/
│   ├── IBillingService.cs           # Also in Shared/ as interface
│   ├── PaystackBillingService.cs    # Real Paystack implementation
│   ├── MockBillingService.cs        # Local dev — auto-succeeds
│   ├── PaystackClient.cs            # HTTP client for Paystack API
│   ├── WebhookSignatureValidator.cs
│   └── InvoiceGenerator.cs
├── DTOs/
│   ├── PaystackWebhookPayload.cs
│   ├── PaystackInitializeRequest.cs
│   ├── SubscriptionDto.cs
│   └── InvoiceDto.cs
├── Views/
│   ├── _ViewImports.cshtml
│   ├── Index.cshtml
│   ├── _SubscriptionCard.cshtml
│   ├── _InvoiceList.cshtml
│   ├── _ChangePlanModal.cshtml
│   └── _CancelConfirmModal.cshtml
└── Events/
    ├── BillingEventConfig.cs
    └── BillingEvents.cs
```

**Dependencies**: `CoreDbContext`, `ITenantContext`, `IEmailService`, `IAuditWriter`

---

### 4.7 FeatureFlags Module

| | |
|---|---|
| **Purpose** | Database-backed feature flag management linked to plans, with Microsoft.FeatureManagement integration |
| **Database** | `CoreDbContext` (features, plan-feature links) |
| **Auth Required** | Feature management UI → super admin only |
| **Tenant Scoped** | Feature resolution is tenant-scoped (based on plan) |
| **Detail Doc** | [05 — Feature Flags](05-feature-flags.md) |

**Folder**: `Modules/FeatureFlags/`

**Routes**: Feature management UI is within SuperAdmin module. This module provides the **engine**, not the UI.

**Key Files**:
```
FeatureFlags/
├── README.md
├── FeatureFlagsModule.cs
├── Services/
│   ├── DatabaseFeatureDefinitionProvider.cs  # IFeatureDefinitionProvider
│   ├── TenantFeatureFilter.cs               # IFeatureFilter for plan checks
│   └── FeatureService.cs                    # IFeatureService implementation
└── TagHelpers/
    └── FeatureTagHelper.cs                  # <feature name="notes">
```

**Dependencies**: `CoreDbContext`, `ITenantContext`

---

### 4.8 Audit Module

| | |
|---|---|
| **Purpose** | Global audit trail — captures all entity changes across all databases |
| **Database** | `AuditDbContext` |
| **Auth Required** | Audit UI → super admin only |
| **Tenant Scoped** | ❌ No — audit is global (tenant slug recorded per entry) |
| **Detail Doc** | [02 — Database & Multi-Tenancy](02-database-multitenancy.md) §4 |

**Folder**: `Modules/Audit/`

**Routes**: Audit UI is within SuperAdmin module. This module provides the **engine**.

**Key Files**:
```
Audit/
├── README.md
├── AuditModule.cs
├── Services/
│   ├── AuditWriter.cs                # IAuditWriter → Channel<AuditEntry>
│   ├── AuditBackgroundService.cs     # Reads from channel, batch-writes to DB
│   └── NullAuditWriter.cs            # No-op when Audit:Enabled = false
└── Middleware/
    └── AuditMiddleware.cs            # Intercepts SaveChanges to capture changes
```

**Dependencies**: `AuditDbContext`, `ITenantContext`, `ICurrentUser`

---

### 4.9 Backup Module

| | |
|---|---|
| **Purpose** | Litestream configuration management, health monitoring, data protection key backup |
| **Database** | None directly — manages database files |
| **Auth Required** | Backup status UI → super admin only |
| **Tenant Scoped** | ❌ No |
| **Detail Doc** | [07 — Infrastructure & DevOps](07-infrastructure.md) |

**Folder**: `Modules/Backup/`

**Key Files**:
```
Backup/
├── README.md
├── BackupModule.cs
├── Services/
│   ├── LitestreamConfigGenerator.cs  # Generates litestream.yml for all tenant DBs
│   ├── LitestreamHealthCheck.cs      # IHealthCheck for Litestream status
│   └── BackupService.cs              # IBackupService implementation
└── (no views — UI is in SuperAdmin)
```

**Dependencies**: `CoreDbContext` (reads tenant list to build Litestream config), `IConfiguration`

---

### 4.10 Notes Module (Reference App Module)

| | |
|---|---|
| **Purpose** | Example application module demonstrating the full module pattern |
| **Database** | `TenantDbContext` |
| **Auth Required** | ✅ Tenant user |
| **Tenant Scoped** | ✅ Yes |
| **Feature Flag** | `notes` — must be enabled on tenant's plan |

**Folder**: `Modules/Notes/`

This module already exists in the codebase and serves as the reference implementation for building new application modules. It demonstrates:

- `SwapController` inheritance
- `SwapView()` returns
- `.AlsoUpdate()` multi-target patterns
- `[SwapEventConfig]` source-generated events
- Modal dialog patterns (create, edit, delete confirm)
- List with filtering and pagination
- `INotesService` + `NotesService` pattern
- Entity with `IAuditableEntity`

**Routes** (all prefixed with `/{tenant}`):
| Route | Action | Description |
|-------|--------|-------------|
| `GET /{tenant}/notes` | `Index` | Notes list page |
| `GET /{tenant}/notes/list` | `List` | Notes list partial (for HTMX refresh) |
| `GET /{tenant}/notes/create` | `Create` | Create modal |
| `POST /{tenant}/notes/create` | `Create` | Save new note |
| `GET /{tenant}/notes/{id}/edit` | `Edit` | Edit modal |
| `POST /{tenant}/notes/{id}/edit` | `Edit` | Save edited note |
| `GET /{tenant}/notes/{id}/delete` | `ConfirmDelete` | Delete confirmation modal |
| `POST /{tenant}/notes/{id}/delete` | `Delete` | Delete note |
| `POST /{tenant}/notes/{id}/pin` | `TogglePin` | Toggle pin status |

---

## 5. Module Registration Order

The order modules are registered matters for middleware. Services are generally order-independent, but middleware forms a pipeline.

```csharp
// Program.cs — Registration order
var modules = new IModule[]
{
    // 1. Infrastructure modules (provide services other modules depend on)
    new AuditModule(),           // IAuditWriter must be available early
    new FeatureFlagsModule(),    // IFeatureService used by all tenant modules
    new BackupModule(),          // IBackupService, health checks

    // 2. Auth module (depends on ITenantContext from middleware)
    new AuthModule(),            // ICurrentUser, magic link services

    // 3. Platform modules
    new BillingModule(),         // IBillingService, webhook handling
    new RegistrationModule(),    // Uses billing + tenant provisioning

    // 4. UI modules
    new MarketingModule(),       // Public site
    new SuperAdminModule(),      // Admin backend
    new TenantAdminModule(),     // Tenant admin panel

    // 5. Application modules
    new NotesModule(),           // Reference module
    // Add your modules here
};
```

### Middleware Pipeline Order

```
Request
  │
  ├── Global middleware (compression, exception handler, static files, etc.)
  ├── TenantResolutionMiddleware (from infrastructure — not a module)
  ├── AuditMiddleware (captures SaveChanges for audit trail)
  ├── FeatureFlags middleware (populates feature context from plan)
  ├── Auth middleware (populates ICurrentUser from cookie)
  │     └── CurrentUserMiddleware (from AuthModule)
  ├── Swap.Htmx middleware
  └── MVC routing → Controller actions
```

---

## 6. Adding a New Application Module

Step-by-step guide for adding a new business feature module (e.g., "Projects"):

### Step 1: Create the folder structure

```
Modules/Projects/
├── README.md
├── ProjectsModule.cs
├── Controllers/
│   └── ProjectsController.cs
├── Entities/
│   └── Project.cs
├── Services/
│   ├── IProjectsService.cs
│   └── ProjectsService.cs
├── Events/
│   ├── ProjectsEventConfig.cs
│   └── ProjectsEvents.cs
└── Views/
    ├── _ViewImports.cshtml
    ├── Index.cshtml
    └── _ProjectList.cshtml
```

### Step 2: Define the entity

```csharp
// Entities/Project.cs
public class Project : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    // ... other fields
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
```

### Step 3: Register entity in TenantDbContext

```csharp
// In TenantDbContext.cs — add DbSet
public DbSet<Project> Projects => Set<Project>();

// Create configuration
// Data/Tenant/Configurations/ProjectConfiguration.cs
```

### Step 4: Implement the module

```csharp
// ProjectsModule.cs
public class ProjectsModule : IModule
{
    public string Name => "Projects";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IProjectsService, ProjectsService>();
    }

    public void RegisterMvc(MvcOptions mvcOptions, IMvcBuilder mvcBuilder)
    {
        // Register view location: Modules/Projects/Views/
        // Register partial paths if needed
    }
}
```

### Step 5: Add the feature flag

```csharp
// In CoreDataSeeder — add feature
new Feature { Key = "projects", Name = "Projects", Module = "Projects", IsEnabled = true }
```

### Step 6: Create the controller

```csharp
// Controllers/ProjectsController.cs
[FeatureGate("projects")]
public class ProjectsController : SwapController
{
    // Standard Swap.Htmx controller pattern
}
```

### Step 7: Register in Program.cs

```csharp
var modules = new IModule[]
{
    // ... existing modules ...
    new ProjectsModule(),
};
```

### Step 8: Add permissions

```csharp
// In PermissionDefinitions.cs — add permission constants
public const string ProjectsRead = "projects.read";
public const string ProjectsCreate = "projects.create";
public const string ProjectsEdit = "projects.edit";
public const string ProjectsDelete = "projects.delete";

// Add to GetAll() list
```

### Step 9: Create migration

```bash
dotnet ef migrations add AddProjects --context TenantDbContext --output-dir Data/Tenant/Migrations
```

### Step 10: Add module README

Document the module's purpose, routes, entities, dependencies, and configuration.

---

## 7. Module Dependency Matrix

Shows which shared interfaces each module **provides** (P) or **consumes** (C):

| Module | ITenantContext | ICurrentUser | IFeatureService | IEmailService | IAuditWriter | IBotProtection | IBillingService | ITenantProvisioner |
|--------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **Auth** | C | **P** | C | C | C | — | — | — |
| **FeatureFlags** | C | — | **P** | — | — | — | — | — |
| **Audit** | C | C | — | — | **P** | — | — | — |
| **Billing** | C | — | — | C | C | — | **P** | — |
| **Registration** | — | — | — | C | C | C | C | C |
| **Backup** | — | — | — | — | — | — | — | — |
| **Marketing** | — | — | — | — | — | — | — | — |
| **SuperAdmin** | — | C | — | — | — | — | — | — |
| **TenantAdmin** | C | C | C | C | C | — | — | — |
| **Notes** | C | C | C | — | C | — | — | — |

> `ITenantContext` is **provided** by the infrastructure layer (middleware), not a module.
> `ITenantProvisioner` is **provided** by the Registration module's service layer.

---

## Next Steps

→ [04 — Authentication & Authorization](04-auth.md) for magic link auth, RBAC, and permission tag helpers.
