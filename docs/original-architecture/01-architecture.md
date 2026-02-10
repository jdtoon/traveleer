# 01 — Architecture

> Foundation document. Read this first — every other doc builds on the decisions made here.

---

## 1. System Overview

This is a **modular monolith** SaaS starter kit. A single .NET 10 application serves all concerns — marketing site, super admin backend, tenant applications, registration, billing — as isolated **modules** within one deployable unit.

```
┌─────────────────────────────────────────────────────────────┐
│                      .NET 10 Application                     │
│                                                               │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌───────────────┐   │
│  │Marketing │ │   Auth   │ │  Billing │ │  SuperAdmin   │   │
│  │ Module   │ │  Module  │ │  Module  │ │    Module     │   │
│  └──────────┘ └──────────┘ └──────────┘ └───────────────┘   │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌───────────────┐   │
│  │Register  │ │ Feature  │ │  Audit   │ │  TenantAdmin  │   │
│  │ Module   │ │  Flags   │ │  Module  │ │    Module     │   │
│  └──────────┘ └──────────┘ └──────────┘ └───────────────┘   │
│  ┌──────────┐ ┌──────────┐                                   │
│  │  Backup  │ │   App    │  ◄── Your business logic modules  │
│  │  Module  │ │ Modules  │                                   │
│  └──────────┘ └──────────┘                                   │
│                                                               │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │                    Shared Interfaces                     │ │
│  │  ITenantContext · ICurrentUser · IFeatureService · ...   │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
         │              │              │
    ┌────┴────┐   ┌────┴────┐   ┌────┴─────────────────┐
    │ core.db │   │audit.db │   │ tenants/{slug}.db ×N │
    └─────────┘   └─────────┘   └──────────────────────┘
```

### Why Modular Monolith?

- **Simplicity**: One deployment, one process, one Docker container
- **Module isolation**: Each module is self-contained (controllers, entities, services, views, middleware, events)
- **Transferability**: Any module can be lifted and dropped into another project
- **Performance**: In-process communication, no network overhead between modules
- **Developer UX**: Clone → run → working SaaS in minutes

---

## 2. Project Layout

A single .NET project with modules as folders. No multi-project solution overhead — modules are organizational, not project-level.

```
saas/
├── docker-compose.yml              # Production deployment
├── docker-compose.override.yml     # Local Docker overrides
├── saas.sln
├── docs/                           # This documentation
│   ├── README.md
│   ├── 01-architecture.md
│   ├── ...
│   └── swap.htmx.llms.txt
├── src/
│   ├── Program.cs                  # Application entry point
│   ├── saas.csproj
│   ├── Dockerfile
│   ├── appsettings.json            # Production defaults
│   ├── appsettings.Development.json # Local overrides (zero-config)
│   ├── libman.json                 # Client-side libraries
│   │
│   ├── Shared/                     # Cross-cutting INTERFACES only
│   │   ├── ITenantContext.cs       # Current tenant resolution
│   │   ├── ICurrentUser.cs         # Current authenticated user
│   │   ├── IFeatureService.cs      # Feature flag checks
│   │   ├── IEmailService.cs        # Email abstraction
│   │   ├── IAuditWriter.cs         # Audit log abstraction
│   │   ├── IBotProtection.cs       # Turnstile validation
│   │   └── IBackupService.cs       # Backup trigger abstraction
│   │
│   ├── Infrastructure/             # App-level plumbing
│   │   ├── ServiceCollectionExtensions.cs
│   │   ├── ApplicationBuilderExtensions.cs
│   │   ├── ModuleViewLocationExpander.cs
│   │   ├── MvcExtensions.cs
│   │   ├── WebOptimizerExtensions.cs
│   │   └── InvariantDecimalModelBinder.cs
│   │
│   ├── Data/                       # Database contexts & migrations
│   │   ├── Core/
│   │   │   ├── CoreDbContext.cs
│   │   │   └── Migrations/         # EF Core migrations for core.db
│   │   ├── Tenant/
│   │   │   ├── TenantDbContext.cs
│   │   │   └── Migrations/         # EF Core migrations for tenant DBs
│   │   ├── Audit/
│   │   │   ├── AuditDbContext.cs
│   │   │   └── Migrations/         # EF Core migrations for audit.db
│   │   ├── IAuditableEntity.cs
│   │   └── PaginatedList.cs
│   │
│   ├── Modules/
│   │   ├── Marketing/              # Public-facing marketing site
│   │   │   ├── README.md
│   │   │   ├── MarketingModule.cs
│   │   │   ├── Controllers/
│   │   │   ├── Views/
│   │   │   └── ...
│   │   ├── Auth/                   # Authentication (magic link)
│   │   │   ├── README.md
│   │   │   ├── AuthModule.cs
│   │   │   ├── Controllers/
│   │   │   ├── Services/
│   │   │   ├── Middleware/
│   │   │   ├── Views/
│   │   │   └── ...
│   │   ├── Registration/           # Tenant signup + provisioning
│   │   │   ├── README.md
│   │   │   ├── RegistrationModule.cs
│   │   │   ├── Controllers/
│   │   │   ├── Services/
│   │   │   ├── Views/
│   │   │   └── ...
│   │   ├── SuperAdmin/             # Super admin backend
│   │   │   ├── README.md
│   │   │   ├── SuperAdminModule.cs
│   │   │   ├── Controllers/
│   │   │   ├── Views/
│   │   │   └── ...
│   │   ├── TenantAdmin/            # Tenant user/role management
│   │   │   ├── README.md
│   │   │   ├── TenantAdminModule.cs
│   │   │   ├── Controllers/
│   │   │   ├── Views/
│   │   │   └── ...
│   │   ├── Billing/                # Paystack, subscriptions, invoices
│   │   │   ├── README.md
│   │   │   ├── BillingModule.cs
│   │   │   ├── Controllers/
│   │   │   ├── Services/
│   │   │   ├── DTOs/
│   │   │   ├── Views/
│   │   │   └── ...
│   │   ├── FeatureFlags/           # Feature flag management
│   │   │   ├── README.md
│   │   │   ├── FeatureFlagsModule.cs
│   │   │   ├── Services/
│   │   │   ├── TagHelpers/
│   │   │   └── ...
│   │   ├── Audit/                  # Global audit trail
│   │   │   ├── README.md
│   │   │   ├── AuditModule.cs
│   │   │   ├── Services/
│   │   │   ├── Middleware/
│   │   │   └── ...
│   │   ├── Backup/                 # Litestream management
│   │   │   ├── README.md
│   │   │   ├── BackupModule.cs
│   │   │   ├── Services/
│   │   │   └── ...
│   │   └── Notes/                  # Example app module (reference)
│   │       ├── README.md
│   │       ├── NotesModule.cs
│   │       ├── Controllers/
│   │       ├── Entities/
│   │       ├── Services/
│   │       ├── Events/
│   │       └── Views/
│   │
│   ├── Controllers/
│   │   └── HomeController.cs       # Root redirect logic
│   ├── Views/
│   │   ├── Shared/
│   │   │   └── _Layout.cshtml
│   │   ├── _ViewImports.cshtml
│   │   └── _ViewStart.cshtml
│   ├── Properties/
│   │   └── launchSettings.json
│   └── wwwroot/
│       ├── css/
│       ├── js/
│       └── lib/
│
├── tests/
│   ├── saas.Tests.csproj
│   └── ...
│
└── data/                           # LOCAL development databases
    ├── core.db                     # SaaS core (tenants, plans, billing)
    ├── audit.db                    # Global audit trail
    ├── tenants/                    # One DB per tenant
    │   ├── acme.db
    │   ├── globex.db
    │   └── ...
    └── keys/                       # Data protection keys
```

---

## 3. Database Strategy

### Three Types of SQLite Databases

SQLite is chosen for **clear separation** — each database is a self-contained file, trivially backed up, and perfectly suited for a single-server modular monolith.

| Database | File | DbContext | Purpose |
|----------|------|-----------|---------|
| **Core** | `data/core.db` | `CoreDbContext` | SaaS platform data: tenants, plans, features, feature flags, plan-feature links, subscriptions, invoices, payments, super admin accounts |
| **Audit** | `data/audit.db` | `AuditDbContext` | Global audit trail across all tenants and super admin. Optional (controlled via `Audit:Enabled` appsetting). Background job writer. |
| **Tenant** (×N) | `data/tenants/{slug}.db` | `TenantDbContext` | Per-tenant isolated data: ASP.NET Identity (users, roles), permissions, RBAC, plus all app-domain tables (e.g., Notes). Each tenant gets a fresh DB provisioned from the latest migrations. |

### Key Principles

1. **WAL Mode everywhere** — All databases use `PRAGMA journal_mode=WAL` for concurrent read performance and Litestream compatibility
2. **One DbContext per database type** — Not per database instance. `TenantDbContext` is one class, but its connection string changes based on the resolved tenant
3. **Migrations are per-context** — Each context has its own migrations folder. Tenant migrations apply to ALL tenant databases (run on provisioning + background migration runner for updates)
4. **Database files in one directory** — All `.db` files live under `data/` (locally) or `/app/data/` (Docker), mapped as a single Docker volume for easy backup

### Volume Layout (Docker)

```yaml
volumes:
  - sqlite-data:/app/data    # All databases + data protection keys
```

```
/app/data/
├── core.db
├── core.db-wal
├── core.db-shm
├── audit.db
├── audit.db-wal
├── audit.db-shm
├── tenants/
│   ├── acme.db
│   ├── acme.db-wal
│   ├── acme.db-shm
│   ├── globex.db
│   └── ...
└── keys/
    └── key-{guid}.xml
```

---

## 4. Tenant Resolution

The application supports two modes of tenant resolution, configured via `Tenancy:Mode` appsetting:

| Mode | URL Pattern | Example | Default |
|------|-------------|---------|---------|
| **Slug** | `domain.com/{tenant}/...` | `myapp.com/acme/dashboard` | ✅ Yes |
| **Subdomain** | `{tenant}.domain.com/...` | `acme.myapp.com/dashboard` | No |

**Slug mode is the default** because it requires zero DNS configuration — critical for local development and simple deployments.

### Resolution Flow

```
HTTP Request
    │
    ▼
┌─────────────────────┐
│ TenantMiddleware     │  ◄── Runs early in pipeline
│                       │
│ 1. Extract slug from │
│    URL or subdomain   │
│ 2. Look up tenant in │
│    CoreDbContext       │
│ 3. Set ITenantContext │
│ 4. Set DB connection  │
│    → data/tenants/    │
│      {slug}.db        │
└─────────────────────┘
    │
    ▼
┌─────────────────────┐
│ Feature Middleware    │  ◄── Loads tenant's plan → active features
└─────────────────────┘
    │
    ▼
┌─────────────────────┐
│ Auth Middleware       │  ◄── Cookie auth against tenant's Identity DB
└─────────────────────┘
    │
    ▼
┌─────────────────────┐
│ Module Controllers   │  ◄── TenantDbContext uses resolved connection
└─────────────────────┘
```

### Routes Without a Tenant

Some routes do **not** belong to a tenant and bypass tenant resolution:

| Route Pattern | Module | Tenant Resolution |
|---------------|--------|-------------------|
| `/` , `/pricing`, `/about` | Marketing | ❌ No tenant |
| `/register`, `/register/*` | Registration | ❌ No tenant |
| `/super-admin/*` | SuperAdmin | ❌ No tenant (super admin auth) |
| `/login`, `/magic-link/*` | Auth | ❌ No tenant (for super admin login) |
| `/{tenant}/login`, `/{tenant}/magic-link/*` | Auth | ✅ Tenant resolved (for tenant user login) |
| `/{tenant}/*` | All tenant modules | ✅ Tenant resolved |
| `/health` | Infrastructure | ❌ No tenant |

---

## 5. Environment & Configuration Layering

### Philosophy: Local-First, Zero-Config

The application MUST work with `dotnet run` immediately after cloning. No environment variables, no external services, no configuration needed for local development.

### Layering Strategy

```
appsettings.json                    ◄── Base defaults (safe for all environments)
  └── appsettings.Development.json  ◄── Local overrides (console logging, mock services)
        └── Environment Variables   ◄── Production overrides (secrets, real services)
              └── Docker Compose    ◄── Container-specific settings
```

### Configuration Sections

| Section | Purpose | Local Default | Production Override |
|---------|---------|---------------|---------------------|
| `ConnectionStrings:Core` | Core database path | `Data Source=data/core.db` | `Data Source=/app/data/core.db` |
| `ConnectionStrings:Audit` | Audit database path | `Data Source=data/audit.db` | `Data Source=/app/data/audit.db` |
| `Tenancy:Mode` | Slug or Subdomain | `Slug` | `Slug` or `Subdomain` |
| `Tenancy:BaseDomain` | Base domain for subdomain mode | `localhost` | `myapp.com` |
| `Tenancy:DatabasePath` | Tenant DB directory | `data/tenants` | `/app/data/tenants` |
| `Audit:Enabled` | Enable audit logging | `true` | `true` |
| `Auth:MagicLink:DeliveryMode` | How magic links are sent | `Console` | `Email` |
| `Auth:MagicLink:TokenLifetimeMinutes` | Magic link expiry | `15` | `15` |
| `Auth:SuperAdmin:Email` | Super admin email | `admin@localhost` | `admin@myapp.com` |
| `FeatureFlags:AllEnabledLocally` | Override all features on | `true` | `false` |
| `Billing:Provider` | Payment provider | `Mock` | `Paystack` |
| `Billing:Paystack:SecretKey` | Paystack API key | _(empty)_ | `sk_live_...` |
| `Billing:Paystack:PublicKey` | Paystack public key | _(empty)_ | `pk_live_...` |
| `Billing:Paystack:WebhookSecret` | Webhook validation | _(empty)_ | `whsec_...` |
| `Email:Provider` | Email service | `Console` | `AwsSes` |
| `Email:AwsSes:Region` | AWS region | _(empty)_ | `eu-west-1` |
| `Email:AwsSes:FromAddress` | Sender email | `noreply@localhost` | `noreply@myapp.com` |
| `BotProtection:Provider` | Bot protection | `None` | `Turnstile` |
| `BotProtection:Turnstile:SiteKey` | Turnstile site key | _(empty)_ | `0x...` |
| `BotProtection:Turnstile:SecretKey` | Turnstile secret | _(empty)_ | `0x...` |
| `DataProtection:KeyPath` | Key storage path | `data/keys` | `/app/data/keys` |
| `RateLimiting:Enabled` | Rate limiting | `false` | `true` |

---

## 6. Request Lifecycle

A complete picture of how a request flows through the system:

```
1. HTTP Request arrives
2. Compression middleware (Brotli/Gzip)
3. Exception handler
4. HTTPS redirection (production)
5. Static files (wwwroot)
6. Routing
7. Rate limiting (if enabled)
8. Bot protection validation (registration/login routes)
9. ── TENANT RESOLUTION ──
   │  Extract tenant slug from URL
   │  Look up tenant in core.db
   │  Set ITenantContext (slug, plan, status)
   │  Configure TenantDbContext connection string
10. ── FEATURE FLAG LOADING ──
    │  Load tenant's plan features from core.db
    │  Populate IFeatureManager context
11. ── AUTHENTICATION ──
    │  Cookie auth against tenant's Identity DB
    │  (or super admin cookie for /super-admin routes)
12. ── AUTHORIZATION ──
    │  Feature gate check (is feature enabled for this tenant?)
    │  RBAC check (does user have required permission?)
13. Swap.Htmx middleware
14. Controller action executes
15. ── AUDIT (if enabled) ──
    │  Background job writes audit entry to audit.db
16. Response returned
```

---

## 7. Design Principles

### Module Self-Containment

Every module MUST be self-contained. A module folder includes **everything** it needs:

- `{Module}Module.cs` — Registration entry point
- `Controllers/` — Swap.Htmx controllers (inherit `SwapController`)
- `Entities/` — EF Core entity classes (registered via the appropriate DbContext)
- `Services/` — Business logic (interfaces + implementations)
- `Views/` — Razor views (`.cshtml`)
- `DTOs/` — Data transfer objects (if needed)
- `Events/` — Swap.Htmx event definitions
- `Middleware/` — Module-specific middleware (if needed)
- `TagHelpers/` — Module-specific tag helpers (if needed)
- `README.md` — Module documentation

### Shared Interfaces, Module Implementations

The `Shared/` folder contains **only interfaces** — never implementations. This allows modules to communicate without coupling:

```csharp
// Shared/ITenantContext.cs — Interface
public interface ITenantContext
{
    string? Slug { get; }
    bool IsTenantRequest { get; }
    Guid? TenantId { get; }
    string? PlanSlug { get; }
}

// Modules/Auth/Services/TenantContext.cs — Implementation
public class TenantContext : ITenantContext { ... }
```

### Swap.Htmx First

ALL UI interactions use Swap.Htmx patterns:
- Controllers inherit `SwapController`
- Actions return `SwapView(...)` not `View(...)`
- Multi-target updates use `.AlsoUpdate(...)`
- State management uses `<swap-state>` + `[FromSwapState]`
- Events use `[SwapEventConfig]` + source-generated event keys
- Custom JavaScript is a **last resort** — always try server-driven HTMX first

### No Magic Strings

Source generators provide compile-time safety:
- `SwapViews.*` — auto-generated from `.cshtml` files
- `SwapElements.*` — auto-generated from `swap-id` attributes
- Event keys — auto-generated from `[SwapEventConfig]` classes

---

## 8. Deployment Model

### Single Server, Docker Compose

The production deployment is intentionally simple — one server, one `docker-compose.yml`:

```
┌──────────────────────────────────────────────┐
│                 Linux Server                   │
│                                                │
│  ┌──────────────────────────────────────────┐ │
│  │           Docker Compose                  │ │
│  │                                            │ │
│  │  ┌─────────────┐  ┌──────────────────┐   │ │
│  │  │   saas-app   │  │   litestream     │   │ │
│  │  │   .NET 10    │  │   sidecar        │   │ │
│  │  │   Port 8080  │  │   Replicates to  │   │ │
│  │  │              │  │   Cloudflare R2  │   │ │
│  │  └──────┬───────┘  └────────┬─────────┘   │ │
│  │         │                    │              │ │
│  │         └──────┬─────────────┘              │ │
│  │                │                            │ │
│  │         ┌──────┴──────┐                    │ │
│  │         │ Docker Volume│                    │ │
│  │         │  /app/data/  │                    │ │
│  │         │  ├─core.db   │                    │ │
│  │         │  ├─audit.db  │                    │ │
│  │         │  ├─tenants/  │                    │ │
│  │         │  │ ├─acme.db │                    │ │
│  │         │  │ └─...     │                    │ │
│  │         │  └─keys/     │                    │ │
│  │         └──────────────┘                    │ │
│  └──────────────────────────────────────────┘ │
│                                                │
│  Reverse Proxy (Caddy / Nginx / Cloudflare)    │
└──────────────────────────────────────────────┘
```

### Why This Works

- **SQLite scales surprisingly well** for single-server SaaS (handles thousands of concurrent reads via WAL mode)
- **Litestream provides continuous backup** — not a cron job, but real-time WAL shipping to R2
- **Database-per-tenant provides isolation** — a misbehaving tenant can't impact others' data
- **Docker Compose is simple** — one file, `docker compose up`, done
- **Single volume for all data** — easy backup, easy restore, easy migration

---

## Next Steps

→ [02 — Database & Multi-Tenancy](02-database-multitenancy.md) for detailed schema design and tenant resolution implementation.
