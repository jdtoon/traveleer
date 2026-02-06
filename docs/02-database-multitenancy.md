# 02 — Database & Multi-Tenancy

> Defines the three database context types, their schemas, EF Core migration strategy, tenant resolution middleware, and tenant database provisioning.

**Prerequisites**: [01 — Architecture](01-architecture.md)

---

## 1. Database Context Overview

Three `DbContext` classes, each with its own migration folder and connection management:

| Context | Database File | Lifetime | Migration Folder |
|---------|--------------|----------|-----------------|
| `CoreDbContext` | `data/core.db` | Singleton connection string | `Data/Core/Migrations/` |
| `AuditDbContext` | `data/audit.db` | Singleton connection string | `Data/Audit/Migrations/` |
| `TenantDbContext` | `data/tenants/{slug}.db` | Dynamic per-request | `Data/Tenant/Migrations/` |

### Context Isolation Rules

- Each context is registered **independently** in DI
- Each context has its own `OnModelCreating` — no shared entity configurations
- Contexts never reference each other's entities
- `TenantDbContext` connection string is set **per-request** by the tenant resolution middleware
- All contexts enforce `PRAGMA journal_mode=WAL` on connection open

---

## 2. CoreDbContext — SaaS Platform Data

The core database is the **single source of truth** for all SaaS platform operations. It stores tenant records, plans, feature definitions, billing, and super admin accounts.

### File Location

```
src/Data/Core/
├── CoreDbContext.cs
├── Configurations/
│   ├── TenantConfiguration.cs
│   ├── PlanConfiguration.cs
│   ├── FeatureConfiguration.cs
│   ├── PlanFeatureConfiguration.cs
│   ├── SubscriptionConfiguration.cs
│   ├── InvoiceConfiguration.cs
│   ├── PaymentConfiguration.cs
│   └── SuperAdminConfiguration.cs
├── Migrations/
│   └── (EF Core auto-generated)
└── Seeding/
    └── CoreDataSeeder.cs
```

### Entity Schema

#### Tenant

The central entity representing a customer of the SaaS application.

```csharp
public class Tenant : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;          // Display name
    public string Slug { get; set; } = string.Empty;          // URL identifier (unique, immutable)
    public string ContactEmail { get; set; } = string.Empty;
    public TenantStatus Status { get; set; }                  // Active, Suspended, PendingSetup, Cancelled
    public string? DatabaseName { get; set; }                 // Defaults to "{slug}.db"
    public Guid PlanId { get; set; }
    public Plan Plan { get; set; } = null!;
    public Subscription? ActiveSubscription { get; set; }
    public ICollection<Invoice> Invoices { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public enum TenantStatus
{
    PendingSetup,   // Registration started, DB not yet provisioned
    Active,         // Fully operational
    Suspended,      // Payment failed or admin action
    Cancelled       // Tenant requested cancellation
}
```

#### Plan

Defines a subscription tier. Plans are linked to features.

```csharp
public class Plan : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;          // "Starter", "Professional", "Enterprise"
    public string Slug { get; set; } = string.Empty;          // "starter", "professional", "enterprise"
    public string? Description { get; set; }
    public decimal MonthlyPrice { get; set; }
    public decimal? AnnualPrice { get; set; }                 // null = no annual option
    public string? Currency { get; set; } = "ZAR";            // Default to ZAR for Paystack
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public int? MaxUsers { get; set; }                        // null = unlimited
    public string? PaystackPlanCode { get; set; }             // Paystack plan identifier

    public ICollection<PlanFeature> PlanFeatures { get; set; } = [];
    public ICollection<Tenant> Tenants { get; set; } = [];

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
```

#### Feature

A feature that can be toggled on/off and linked to plans.

```csharp
public class Feature : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;           // "notes", "api_access", "advanced_reports"
    public string Name { get; set; } = string.Empty;          // "Notes Module", "API Access"
    public string? Description { get; set; }
    public string? Module { get; set; }                       // Which module this belongs to
    public bool IsGlobal { get; set; }                        // true = always on regardless of plan
    public bool IsEnabled { get; set; } = true;               // Master kill switch (super admin)

    public ICollection<PlanFeature> PlanFeatures { get; set; } = [];

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
```

#### PlanFeature

Join table linking plans to features with optional per-plan configuration.

```csharp
public class PlanFeature
{
    public Guid PlanId { get; set; }
    public Plan Plan { get; set; } = null!;
    public Guid FeatureId { get; set; }
    public Feature Feature { get; set; } = null!;
    public string? ConfigJson { get; set; }                   // Optional per-plan config (e.g., limits)
}
```

#### Subscription

Tracks a tenant's active subscription.

```csharp
public class Subscription : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid PlanId { get; set; }
    public Plan Plan { get; set; } = null!;
    public SubscriptionStatus Status { get; set; }
    public BillingCycle BillingCycle { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? PaystackSubscriptionCode { get; set; }
    public string? PaystackCustomerCode { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public enum SubscriptionStatus
{
    Active,
    PastDue,
    Cancelled,
    Expired,
    Trialing
}

public enum BillingCycle
{
    Monthly,
    Annual
}
```

#### Invoice

Billing record for a subscription period.

```csharp
public class Invoice : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid? SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty; // "INV-2026-0001"
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public InvoiceStatus Status { get; set; }
    public DateTime IssuedDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? PaidDate { get; set; }
    public string? PaystackReference { get; set; }
    public string? Description { get; set; }

    public Payment? Payment { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public enum InvoiceStatus
{
    Draft,
    Issued,
    Paid,
    Overdue,
    Cancelled,
    Refunded
}
```

#### Payment

Records individual payment transactions.

```csharp
public class Payment : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public PaymentStatus Status { get; set; }
    public string? PaystackReference { get; set; }
    public string? PaystackTransactionId { get; set; }
    public string? GatewayResponse { get; set; }              // Raw response for debugging
    public DateTime TransactionDate { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public enum PaymentStatus
{
    Pending,
    Success,
    Failed,
    Refunded
}
```

#### SuperAdmin

Super admin accounts (magic link auth — no password stored).

```csharp
public class SuperAdmin : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
```

#### MagicLinkToken

Stores issued magic link tokens for super admin (and optionally tenant) login.

```csharp
public class MagicLinkToken
{
    public Guid Id { get; set; }
    public string Token { get; set; } = string.Empty;         // Hashed token
    public string Email { get; set; } = string.Empty;
    public string? TenantSlug { get; set; }                   // null = super admin login
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UsedAt { get; set; }
}
```

### CoreDbContext Registration

```csharp
public class CoreDbContext : DbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Feature> Features => Set<Feature>();
    public DbSet<PlanFeature> PlanFeatures => Set<PlanFeature>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<SuperAdmin> SuperAdmins => Set<SuperAdmin>();
    public DbSet<MagicLinkToken> MagicLinkTokens => Set<MagicLinkToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(CoreDbContext).Assembly,
            t => t.Namespace?.Contains("Data.Core") == true
        );
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        ApplyAuditFields();
        return base.SaveChangesAsync(ct);
    }
}
```

### DI Registration

```csharp
// In ServiceCollectionExtensions.cs
services.AddDbContext<CoreDbContext>(options =>
    options.UseSqlite(
        config.GetConnectionString("Core"),
        sql => sql.MigrationsAssembly(typeof(CoreDbContext).Assembly.FullName)
    )
);
```

### Default Seed Data

```csharp
// CoreDataSeeder.cs — runs on startup
public static class CoreDataSeeder
{
    public static async Task SeedAsync(CoreDbContext db)
    {
        if (await db.Plans.AnyAsync()) return;

        var features = new[]
        {
            new Feature { Key = "notes", Name = "Notes", Module = "Notes", IsGlobal = false, IsEnabled = true },
            // Add more features per module as modules are built
        };

        var freePlan = new Plan
        {
            Name = "Free", Slug = "free",
            Description = "Get started for free",
            MonthlyPrice = 0, SortOrder = 0, MaxUsers = 3
        };

        var proPlan = new Plan
        {
            Name = "Professional", Slug = "professional",
            Description = "For growing teams",
            MonthlyPrice = 499, AnnualPrice = 4990, SortOrder = 1, MaxUsers = 25
        };

        var enterprisePlan = new Plan
        {
            Name = "Enterprise", Slug = "enterprise",
            Description = "For large organisations",
            MonthlyPrice = 1499, AnnualPrice = 14990, SortOrder = 2, MaxUsers = null
        };

        db.Features.AddRange(features);
        db.Plans.AddRange(freePlan, proPlan, enterprisePlan);
        await db.SaveChangesAsync();

        // Link features to plans
        db.PlanFeatures.AddRange(
            new PlanFeature { PlanId = proPlan.Id, FeatureId = features[0].Id },
            new PlanFeature { PlanId = enterprisePlan.Id, FeatureId = features[0].Id }
            // Free plan gets no features by default
        );
        await db.SaveChangesAsync();

        // Seed a default super admin (email from config)
        // Done in Program.cs using IConfiguration
    }
}
```

---

## 3. TenantDbContext — Per-Tenant Application Data

Each tenant gets an isolated SQLite database containing ASP.NET Identity tables plus all application domain entities. The **same DbContext class** is used for all tenant databases — only the connection string changes per request.

### File Location

```
src/Data/Tenant/
├── TenantDbContext.cs
├── Configurations/
│   ├── AppUserConfiguration.cs
│   ├── AppRoleConfiguration.cs
│   ├── PermissionConfiguration.cs
│   ├── RolePermissionConfiguration.cs
│   └── NoteConfiguration.cs          ◄── Module entities registered here
├── Migrations/
│   └── (EF Core auto-generated)
└── Seeding/
    └── TenantDataSeeder.cs
```

### Entity Schema

#### AppUser (extends IdentityUser)

```csharp
public class AppUser : IdentityUser, IAuditableEntity
{
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
```

#### AppRole (extends IdentityRole)

```csharp
public class AppRole : IdentityRole, IAuditableEntity
{
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }                    // true = cannot be deleted (Admin, Member)
    public ICollection<RolePermission> RolePermissions { get; set; } = [];

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
```

#### Permission

Permissions are string-based constants defined in code, stored in DB for RBAC assignment.

```csharp
public class Permission
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;           // "notes.create", "notes.delete", "users.manage"
    public string Name { get; set; } = string.Empty;          // "Create Notes"
    public string? Description { get; set; }
    public string Group { get; set; } = string.Empty;         // "Notes", "Users", "Settings"
    public int SortOrder { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}
```

#### RolePermission

Join table for RBAC.

```csharp
public class RolePermission
{
    public string RoleId { get; set; } = string.Empty;
    public AppRole Role { get; set; } = null!;
    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
}
```

#### Application Domain Entities

Module entities (like `Note`) also live in the tenant database. Each module registers its entity configurations in `TenantDbContext.OnModelCreating`:

```csharp
// Note entity (from Notes module) — configured in TenantDbContext
public class Note : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string Color { get; set; } = "gray";
    public bool IsPinned { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
```

### TenantDbContext Class

```csharp
public class TenantDbContext : IdentityDbContext<AppUser, AppRole, string>
{
    // RBAC
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    // Application domain entities (modules register theirs here)
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);  // Identity tables

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(TenantDbContext).Assembly,
            t => t.Namespace?.Contains("Data.Tenant") == true
                || t.Namespace?.Contains("Modules.") == true  // Module configs
        );
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        ApplyAuditFields();
        return base.SaveChangesAsync(ct);
    }
}
```

### Dynamic Connection String

The connection string for `TenantDbContext` is **not** set at DI registration. Instead, it's set per-request by the tenant resolution middleware:

```csharp
// DI Registration — uses a factory
services.AddDbContext<TenantDbContext>((serviceProvider, options) =>
{
    var tenantContext = serviceProvider.GetRequiredService<ITenantContext>();
    if (tenantContext.IsTenantRequest && tenantContext.Slug is not null)
    {
        var dbPath = Path.Combine(
            config["Tenancy:DatabasePath"] ?? "data/tenants",
            $"{tenantContext.Slug}.db"
        );
        options.UseSqlite($"Data Source={dbPath}");
    }
    // If not a tenant request, context won't be used — but we still need a valid state
    // The middleware ensures TenantDbContext is only resolved within tenant routes
});
```

### Default Seed Data (per new tenant)

When a new tenant database is provisioned, seed data is applied:

```csharp
public static class TenantDataSeeder
{
    public static async Task SeedAsync(TenantDbContext db, UserManager<AppUser> userManager,
        RoleManager<AppRole> roleManager, string adminEmail)
    {
        // 1. Create default roles
        var adminRole = new AppRole
        {
            Name = "Admin", NormalizedName = "ADMIN",
            Description = "Full access to all features",
            IsSystemRole = true
        };
        var memberRole = new AppRole
        {
            Name = "Member", NormalizedName = "MEMBER",
            Description = "Standard member access",
            IsSystemRole = true
        };
        await roleManager.CreateAsync(adminRole);
        await roleManager.CreateAsync(memberRole);

        // 2. Create default permissions
        var permissions = PermissionDefinitions.GetAll(); // Static list from code
        db.Permissions.AddRange(permissions);
        await db.SaveChangesAsync();

        // 3. Assign all permissions to Admin role
        var adminPermissions = permissions.Select(p => new RolePermission
        {
            RoleId = adminRole.Id,
            PermissionId = p.Id
        });
        db.RolePermissions.AddRange(adminPermissions);

        // 4. Assign basic permissions to Member role
        var memberPermissions = permissions
            .Where(p => p.Key.EndsWith(".read") || p.Key.EndsWith(".create"))
            .Select(p => new RolePermission
            {
                RoleId = memberRole.Id,
                PermissionId = p.Id
            });
        db.RolePermissions.AddRange(memberPermissions);
        await db.SaveChangesAsync();

        // 5. Create the tenant admin user
        var adminUser = new AppUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            DisplayName = "Admin",
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(adminUser);
        await userManager.AddToRoleAsync(adminUser, "Admin");
    }
}
```

### Permission Definitions (Static, Code-Defined)

```csharp
public static class PermissionDefinitions
{
    // Notes Module
    public const string NotesRead = "notes.read";
    public const string NotesCreate = "notes.create";
    public const string NotesEdit = "notes.edit";
    public const string NotesDelete = "notes.delete";

    // User Management
    public const string UsersRead = "users.read";
    public const string UsersCreate = "users.create";
    public const string UsersEdit = "users.edit";
    public const string UsersDelete = "users.delete";

    // Role Management
    public const string RolesRead = "roles.read";
    public const string RolesCreate = "roles.create";
    public const string RolesEdit = "roles.edit";
    public const string RolesDelete = "roles.delete";

    // Settings
    public const string SettingsRead = "settings.read";
    public const string SettingsEdit = "settings.edit";

    public static List<Permission> GetAll() => [
        new() { Key = NotesRead, Name = "View Notes", Group = "Notes", SortOrder = 0 },
        new() { Key = NotesCreate, Name = "Create Notes", Group = "Notes", SortOrder = 1 },
        new() { Key = NotesEdit, Name = "Edit Notes", Group = "Notes", SortOrder = 2 },
        new() { Key = NotesDelete, Name = "Delete Notes", Group = "Notes", SortOrder = 3 },
        new() { Key = UsersRead, Name = "View Users", Group = "Users", SortOrder = 0 },
        new() { Key = UsersCreate, Name = "Invite Users", Group = "Users", SortOrder = 1 },
        new() { Key = UsersEdit, Name = "Edit Users", Group = "Users", SortOrder = 2 },
        new() { Key = UsersDelete, Name = "Deactivate Users", Group = "Users", SortOrder = 3 },
        new() { Key = RolesRead, Name = "View Roles", Group = "Roles", SortOrder = 0 },
        new() { Key = RolesCreate, Name = "Create Roles", Group = "Roles", SortOrder = 1 },
        new() { Key = RolesEdit, Name = "Edit Roles", Group = "Roles", SortOrder = 2 },
        new() { Key = RolesDelete, Name = "Delete Roles", Group = "Roles", SortOrder = 3 },
        new() { Key = SettingsRead, Name = "View Settings", Group = "Settings", SortOrder = 0 },
        new() { Key = SettingsEdit, Name = "Edit Settings", Group = "Settings", SortOrder = 1 },
    ];
}
```

---

## 4. AuditDbContext — Global Audit Trail

A single database that records every significant transaction across all tenants and the super admin. Enabled/disabled via `Audit:Enabled` appsetting.

### File Location

```
src/Data/Audit/
├── AuditDbContext.cs
├── Configurations/
│   └── AuditEntryConfiguration.cs
├── Migrations/
│   └── (EF Core auto-generated)
└── Seeding/
    └── (none — no seed data needed)
```

### Entity Schema

#### AuditEntry

```csharp
public class AuditEntry
{
    public long Id { get; set; }                              // Auto-increment (high volume, use long)
    public string? TenantSlug { get; set; }                   // null = super admin / system action
    public string EntityType { get; set; } = string.Empty;    // "Note", "AppUser", "Tenant"
    public string EntityId { get; set; } = string.Empty;      // Primary key of the affected entity
    public string Action { get; set; } = string.Empty;        // "Created", "Updated", "Deleted"
    public string? UserId { get; set; }                       // Who performed the action
    public string? UserEmail { get; set; }                    // Denormalized for easy querying
    public string? OldValues { get; set; }                    // JSON snapshot before change
    public string? NewValues { get; set; }                    // JSON snapshot after change
    public string? AffectedColumns { get; set; }              // Comma-separated changed columns
    public DateTime Timestamp { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
```

### AuditDbContext Class

```csharp
public class AuditDbContext : DbContext
{
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AuditDbContext).Assembly,
            t => t.Namespace?.Contains("Data.Audit") == true
        );
    }
}
```

### How Audit Writing Works

Audit entries are written by a **background channel** to avoid impacting request latency:

```csharp
// Shared/IAuditWriter.cs
public interface IAuditWriter
{
    ValueTask WriteAsync(AuditEntry entry);
}

// Modules/Audit/Services/AuditWriter.cs
public class AuditWriter : IAuditWriter
{
    private readonly Channel<AuditEntry> _channel;

    public AuditWriter(Channel<AuditEntry> channel) => _channel = channel;

    public ValueTask WriteAsync(AuditEntry entry)
        => _channel.Writer.WriteAsync(entry);
}

// Modules/Audit/Services/AuditBackgroundService.cs
public class AuditBackgroundService : BackgroundService
{
    // Reads from channel, batch-writes to AuditDbContext
    // Batches every 100 entries or every 5 seconds, whichever comes first
}
```

### DI Registration

```csharp
services.AddDbContext<AuditDbContext>(options =>
    options.UseSqlite(config.GetConnectionString("Audit"))
);
```

### Configuration

```json
{
  "Audit": {
    "Enabled": true,
    "BatchSize": 100,
    "FlushIntervalSeconds": 5
  }
}
```

When `Audit:Enabled` is `false`, a `NullAuditWriter` is registered that discards entries silently.

---

## 5. Tenant Resolution Middleware

The middleware that extracts the tenant slug from the URL and establishes the tenant context for the request.

### Implementation Outline

```csharp
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TenancyOptions _options;

    public TenantResolutionMiddleware(RequestDelegate next, IOptions<TenancyOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context, CoreDbContext coreDb, ITenantContext tenantContext)
    {
        var slug = ExtractSlug(context);

        if (slug is not null)
        {
            // Look up tenant in core database
            var tenant = await coreDb.Tenants
                .AsNoTracking()
                .Include(t => t.Plan)
                .FirstOrDefaultAsync(t => t.Slug == slug && t.Status == TenantStatus.Active);

            if (tenant is null)
            {
                context.Response.StatusCode = 404;
                return; // Tenant not found or not active
            }

            // Populate the tenant context for this request
            ((TenantContext)tenantContext).Set(
                slug: tenant.Slug,
                tenantId: tenant.Id,
                planSlug: tenant.Plan.Slug,
                tenantName: tenant.Name
            );
        }

        await _next(context);
    }

    private string? ExtractSlug(HttpContext context)
    {
        if (_options.Mode == TenancyMode.Subdomain)
        {
            var host = context.Request.Host.Host;
            var baseDomain = _options.BaseDomain;
            if (host.EndsWith($".{baseDomain}"))
            {
                return host[..^(baseDomain.Length + 1)];
            }
            return null;
        }

        // Slug mode (default)
        var path = context.Request.Path.Value;
        if (path is null || path == "/") return null;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;

        var firstSegment = segments[0].ToLowerInvariant();

        // Skip known non-tenant routes
        if (IsReservedRoute(firstSegment)) return null;

        return firstSegment;
    }

    private static bool IsReservedRoute(string segment) => segment switch
    {
        "health" or "login" or "register" or "super-admin"
        or "pricing" or "about" or "api" or "css" or "js"
        or "lib" or "favicon.ico" => true,
        _ => false
    };
}
```

### TenancyOptions

```csharp
public class TenancyOptions
{
    public TenancyMode Mode { get; set; } = TenancyMode.Slug;
    public string BaseDomain { get; set; } = "localhost";
    public string DatabasePath { get; set; } = "data/tenants";
}

public enum TenancyMode
{
    Slug,
    Subdomain
}
```

### ITenantContext (Shared Interface)

```csharp
public interface ITenantContext
{
    string? Slug { get; }
    Guid? TenantId { get; }
    string? PlanSlug { get; }
    string? TenantName { get; }
    bool IsTenantRequest { get; }
}
```

### Registration

```csharp
// Services
services.AddScoped<ITenantContext, TenantContext>();
services.Configure<TenancyOptions>(config.GetSection("Tenancy"));

// Middleware (order matters!)
app.UseMiddleware<TenantResolutionMiddleware>(); // After routing, before auth
```

---

## 6. Tenant Database Provisioning

When a new tenant registers, their database must be created and initialized.

### Provisioning Flow

```
Registration Form Submitted
    │
    ▼
1. Validate tenant name & slug uniqueness (against CoreDbContext)
2. Create Tenant record in core.db (status = PendingSetup)
3. Initialize Paystack subscription (see 06-billing-paystack.md)
4. ── PROVISION TENANT DB ──
   │  a. Create SQLite file: data/tenants/{slug}.db
   │  b. Apply all TenantDbContext migrations
   │  c. Set PRAGMA journal_mode=WAL
   │  d. Run TenantDataSeeder (roles, permissions, admin user)
5. Update Tenant status to Active in core.db
6. Register new DB with Litestream (see 07-infrastructure.md)
7. Redirect to tenant login page
```

### ITenantProvisioner Service

```csharp
public interface ITenantProvisioner
{
    Task<TenantProvisionResult> ProvisionAsync(TenantProvisionRequest request);
}

public record TenantProvisionRequest(
    string TenantName,
    string Slug,
    string AdminEmail,
    Guid PlanId
);

public record TenantProvisionResult(
    bool Success,
    string? Error = null,
    Guid? TenantId = null
);
```

### Implementation Outline

```csharp
public class TenantProvisioner : ITenantProvisioner
{
    private readonly CoreDbContext _coreDb;
    private readonly IConfiguration _config;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TenantProvisioner> _logger;

    public async Task<TenantProvisionResult> ProvisionAsync(TenantProvisionRequest request)
    {
        var dbPath = Path.Combine(
            _config["Tenancy:DatabasePath"] ?? "data/tenants",
            $"{request.Slug}.db"
        );

        // 1. Create tenant record
        var tenant = new Tenant
        {
            Name = request.TenantName,
            Slug = request.Slug,
            ContactEmail = request.AdminEmail,
            PlanId = request.PlanId,
            Status = TenantStatus.PendingSetup,
            DatabaseName = $"{request.Slug}.db"
        };
        _coreDb.Tenants.Add(tenant);
        await _coreDb.SaveChangesAsync();

        try
        {
            // 2. Create and migrate tenant database
            var optionsBuilder = new DbContextOptionsBuilder<TenantDbContext>();
            optionsBuilder.UseSqlite($"Data Source={dbPath}");

            await using var tenantDb = new TenantDbContext(optionsBuilder.Options);
            await tenantDb.Database.MigrateAsync();

            // 3. Enforce WAL mode
            await tenantDb.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

            // 4. Seed default data
            using var scope = _serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
            await TenantDataSeeder.SeedAsync(tenantDb, userManager, roleManager, request.AdminEmail);

            // 5. Activate tenant
            tenant.Status = TenantStatus.Active;
            await _coreDb.SaveChangesAsync();

            return new TenantProvisionResult(true, TenantId: tenant.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision tenant {Slug}", request.Slug);
            tenant.Status = TenantStatus.PendingSetup; // Leave in pending for retry
            await _coreDb.SaveChangesAsync();
            return new TenantProvisionResult(false, Error: ex.Message);
        }
    }
}
```

---

## 7. Tenant Migration Runner

When the application is updated with new `TenantDbContext` migrations, **all existing tenant databases** must be migrated. This runs on application startup as a background task.

### Implementation Outline

```csharp
public class TenantMigrationRunner : BackgroundService
{
    private readonly CoreDbContext _coreDb;
    private readonly IConfiguration _config;
    private readonly ILogger<TenantMigrationRunner> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tenants = await _coreDb.Tenants
            .Where(t => t.Status != TenantStatus.Cancelled)
            .Select(t => t.Slug)
            .ToListAsync(stoppingToken);

        var dbPath = _config["Tenancy:DatabasePath"] ?? "data/tenants";

        foreach (var slug in tenants)
        {
            try
            {
                var optionsBuilder = new DbContextOptionsBuilder<TenantDbContext>();
                optionsBuilder.UseSqlite($"Data Source={Path.Combine(dbPath, $"{slug}.db")}");

                await using var tenantDb = new TenantDbContext(optionsBuilder.Options);
                var pending = await tenantDb.Database.GetPendingMigrationsAsync(stoppingToken);

                if (pending.Any())
                {
                    _logger.LogInformation("Applying {Count} migrations to tenant {Slug}",
                        pending.Count(), slug);
                    await tenantDb.Database.MigrateAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to migrate tenant {Slug}", slug);
                // Continue with other tenants — don't let one failure block all
            }
        }
    }
}
```

---

## 8. EF Core Migration Commands

Each context requires separate migration commands. Use these from the `src/` directory:

### Core Database

```bash
dotnet ef migrations add InitialCore \
    --context CoreDbContext \
    --output-dir Data/Core/Migrations

dotnet ef database update --context CoreDbContext
```

### Tenant Database (design-time)

A design-time factory is needed since `TenantDbContext` requires a dynamic connection string:

```csharp
// Data/Tenant/TenantDbContextFactory.cs
public class TenantDbContextFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TenantDbContext>();
        optionsBuilder.UseSqlite("Data Source=data/tenants/_design.db");
        return new TenantDbContext(optionsBuilder.Options);
    }
}
```

```bash
dotnet ef migrations add InitialTenant \
    --context TenantDbContext \
    --output-dir Data/Tenant/Migrations

# Design-time DB only — real tenant DBs are created by TenantProvisioner
```

### Audit Database

```bash
dotnet ef migrations add InitialAudit \
    --context AuditDbContext \
    --output-dir Data/Audit/Migrations

dotnet ef database update --context AuditDbContext
```

---

## 9. WAL Mode Enforcement

All databases use WAL mode for concurrent read performance and Litestream compatibility. This is enforced at connection open:

```csharp
// Shared connection interceptor registered for all contexts
public class WalModeInterceptor : DbConnectionInterceptor
{
    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        // WAL mode is set per-database, so it persists.
        // We set it every time to ensure new databases get it immediately.
        return result;
    }

    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
```

Registration:

```csharp
services.AddSingleton<WalModeInterceptor>();

services.AddDbContext<CoreDbContext>((sp, options) =>
    options.UseSqlite(config.GetConnectionString("Core"))
           .AddInterceptors(sp.GetRequiredService<WalModeInterceptor>())
);

// Same for AuditDbContext and TenantDbContext
```

---

## 10. Database Summary Diagram

```
┌──────────────────────────────────────────────────────────────────────┐
│                         data/ (Docker volume)                        │
│                                                                      │
│  ┌─── core.db ──────────────────────────────────────────────────┐   │
│  │  Tenants · Plans · Features · PlanFeatures                    │   │
│  │  Subscriptions · Invoices · Payments                          │   │
│  │  SuperAdmins · MagicLinkTokens                                │   │
│  └───────────────────────────────────────────────────────────────┘   │
│                                                                      │
│  ┌─── audit.db ─────────────────────────────────────────────────┐   │
│  │  AuditEntries (TenantSlug nullable — null = system/admin)     │   │
│  └───────────────────────────────────────────────────────────────┘   │
│                                                                      │
│  ┌─── tenants/ ─────────────────────────────────────────────────┐   │
│  │                                                                │   │
│  │  ┌─ acme.db ──────────────────────────────────────────────┐  │   │
│  │  │  Identity: AspNetUsers · AspNetRoles · AspNetUserRoles  │  │   │
│  │  │  RBAC: Permissions · RolePermissions                    │  │   │
│  │  │  App: Notes · (future module entities)                  │  │   │
│  │  └────────────────────────────────────────────────────────┘  │   │
│  │                                                                │   │
│  │  ┌─ globex.db ────────────────────────────────────────────┐  │   │
│  │  │  (same schema as acme.db — isolated data)               │  │   │
│  │  └────────────────────────────────────────────────────────┘  │   │
│  │                                                                │   │
│  │  ┌─ ... (N tenant databases) ─────────────────────────────┐  │   │
│  │  └────────────────────────────────────────────────────────┘  │   │
│  └────────────────────────────────────────────────────────────────┘   │
│                                                                      │
│  ┌─── keys/ ────────────────────────────────────────────────────┐   │
│  │  Data protection key XML files                                │   │
│  └───────────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────┘
```

---

## Next Steps

→ [03 — Modules](03-modules.md) for the module contract, registration pattern, and complete module inventory.
