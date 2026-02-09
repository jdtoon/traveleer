# Development Plan — SaaS Starter Kit

> **Tracking document.** Each phase is implemented following the cycle:
> **Code → Build → Fix → Build → Test → QA Handover.**
>
> Mark items `[x]` as they are completed. Phases are sequential — do not start a phase until the previous phase's QA is signed off.

---

## Workflow Per Phase

```
1. Implement the steps (Code)
2. dotnet build — fix all compile errors (Build → Fix → Build)
3. dotnet test — fix all test failures (Test)
4. Hand over to QA with specific checklist (QA Handover)
5. QA signs off or raises issues
6. Fix issues → repeat from step 2
7. QA confirms ✅ → move to next phase
```

---

## Status Key

- `[ ]` — Not started
- `[~]` — In progress
- `[x]` — Complete
- `[!]` — Blocked / has issues

---

## Phase 1 — Foundation (Contracts, DbContexts, Seeding)

**Goal**: Replace single `AppDbContext` with 3-context architecture, define all shared interfaces, introduce `IModule` pattern, seed master data.

> **Known impact**: The existing Notes module will stop working in this phase. It will be reconnected in Phase 5. This is expected.

### Steps

- [x] **1.1** Create `src/Shared/` folder with all interfaces:
  - [x] `IModule.cs`
  - [x] `ITenantContext.cs`
  - [x] `ICurrentUser.cs`
  - [x] `IFeatureService.cs`
  - [x] `IEmailService.cs` (+ `EmailMessage` record)
  - [x] `IAuditWriter.cs`
  - [x] `IBotProtection.cs`
  - [x] `IBillingService.cs` (+ request/result records)
  - [x] `ITenantProvisioner.cs` (+ request/result records)
- [x] **1.2** Add NuGet packages to `saas.csproj`:
  - [x] `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
  - [x] `Microsoft.FeatureManagement.AspNetCore`
- [x] **1.3** Create `src/Data/CoreDbContext.cs` with entities:
  - [x] `Tenant` (Id, Name, Slug, ContactEmail, Status enum, PlanId, DatabaseName)
  - [x] `Plan` (Id, Name, Slug, MonthlyPrice, AnnualPrice, Currency, MaxUsers, PaystackPlanCode, SortOrder, IsActive, Description)
  - [x] `Feature` (Id, Key, Name, Module, IsGlobal, IsEnabled)
  - [x] `PlanFeature` (PlanId, FeatureId composite key, ConfigJson)
  - [x] `TenantFeatureOverride` (Id, TenantId, FeatureId, IsEnabled, Reason, ExpiresAt)
  - [x] `Subscription` (Id, TenantId, PlanId, Status enum, BillingCycle enum, StartDate, EndDate, NextBillingDate, PaystackSubscriptionCode, PaystackCustomerCode)
  - [x] `Invoice` (Id, TenantId, SubscriptionId, InvoiceNumber, Amount, Currency, Status enum, IssuedDate, DueDate, PaidDate, PaystackReference)
  - [x] `Payment` (Id, TenantId, InvoiceId, Amount, Currency, Status enum, PaystackReference, PaystackTransactionId, GatewayResponse, TransactionDate)
  - [x] `SuperAdmin` (Id, Email, DisplayName, IsActive, LastLoginAt)
  - [x] `MagicLinkToken` (Id, Token, Email, TenantSlug, ExpiresAt, IsUsed, UsedAt)
  - [x] All enums: `TenantStatus`, `SubscriptionStatus`, `BillingCycle`, `InvoiceStatus`, `PaymentStatus`
  - [x] Entity configurations (unique indexes, relationships, value conversions)
  - [x] Connection string: `CoreDatabase`
- [x] **1.4** Create `src/Data/TenantDbContext.cs`:
  - [x] Extends `IdentityDbContext<AppUser, AppRole, string>`
  - [x] `AppUser` (extends IdentityUser + DisplayName, IsActive, LastLoginAt, IAuditableEntity)
  - [x] `AppRole` (extends IdentityRole + Description, IsSystemRole)
  - [x] `Permission` (Id, Key, Name, Group, SortOrder)
  - [x] `RolePermission` (RoleId, PermissionId composite key)
  - [x] `Note` entity (Id as Guid, Title, Content, Color, IsPinned, IAuditableEntity)
  - [x] `WalModeInterceptor` (sets PRAGMA journal_mode=WAL on connection open)
  - [x] Dynamic connection string from `ITenantContext.Slug`
- [x] **1.5** Create `src/Data/AuditDbContext.cs`:
  - [x] `AuditEntry` (Id as long auto-increment, TenantSlug, EntityType, EntityId, Action, UserId, UserEmail, OldValues, NewValues, AffectedColumns, Timestamp, IpAddress, UserAgent)
  - [x] Connection string: `AuditDatabase`
- [x] **1.6** Update `IAuditableEntity`:
  - [x] Rename `CreatedByUserId` → `CreatedBy`
  - [x] Rename `ModifiedByUserId` → `UpdatedBy`
  - [x] Update `SaveChangesAsync` override in TenantDbContext
- [x] **1.7** Create `src/Data/Seeding/MasterDataSeeder.cs`:
  - [x] Seed Plans: Free, Starter (R199/mo), Professional (R499/mo), Enterprise (R999/mo)
  - [x] Seed Features from `FeatureDefinitions` constants
  - [x] Seed PlanFeature mappings (matrix from doc 05 §10)
  - [x] Seed default SuperAdmin (email from config)
  - [x] Idempotent (skip if data exists)
- [x] **1.8** Create `src/Shared/PermissionDefinitions.cs`:
  - [x] Static class with all permission key constants grouped by module
  - [x] `GetAll()` method returning all permissions
- [x] **1.9** Create `src/Shared/FeatureDefinitions.cs`:
  - [x] Static class with all feature key constants
- [x] **1.10** Refactor `Program.cs`:
  - [x] Register `CoreDbContext` + `AuditDbContext` with fixed connection strings
  - [x] Configure `db/` directory structure (db/core.db, db/audit.db, db/tenants/, db/keys/)
  - [x] Run `EnsureCreated()` + WAL mode + `MasterDataSeeder` on startup
  - [x] Add `IModule` registration loop (empty array for now)
  - [x] Remove old `AppDbContext` registration
  - [x] Temporarily comment out Notes module registration (reconnected Phase 5)
- [x] **1.11** Delete or archive old `AppDbContext.cs` (keep `IAuditableEntity.cs`, `PaginatedList.cs`)

### Build & Test

- [x] `dotnet build` passes with zero errors
- [x] `dotnet test` — update/skip broken Notes tests (they will be rewritten in Phase 5)
- [x] New tests written and passing:
  - [x] `CoreDbContextTests` — in-memory create, all entities have correct PKs/FKs, enums stored correctly
  - [x] `MasterDataSeederTests` — seeds expected plans (4), features, plan-features, super admin; idempotent on second run
  - [x] `PermissionDefinitionsTests` — `GetAll()` non-empty, no duplicate keys
  - [x] `TenantDbContextTests` — temp SQLite file, Identity tables exist, Note table exists
  - [x] `AuditDbContextTests` — AuditEntry table exists, auto-increment Id works
  - [x] Integration: app starts, `/health` returns healthy

### QA Handover — Phase 1

```
When I hand this over, verify:

1. `dotnet run --project src` starts without errors
2. `db/core.db` exists — open with SQLite Viewer:
   - Plans table: 4 rows (Free, Starter, Professional, Enterprise)
   - Features table: populated with feature keys
   - PlanFeatures table: mappings exist
   - SuperAdmins table: 1 row with admin@localhost
3. `db/audit.db` exists — AuditEntries table present (empty)
4. `db/tenants/` directory exists (empty)
5. `db/keys/` directory exists
6. Browser: https://localhost:5001 loads (basic page, Notes nav missing — expected)
7. Browser: https://localhost:5001/health returns "Healthy"
8. Terminal: no EF Core or startup errors in logs
```

**QA Status**: [ ] Not started · [ ] Issues raised · [x] Signed off ✅

**QA Notes**:
> _(Write any issues or observations here)_

---

## Phase 2 — Infrastructure Services (Tenant Resolution, Email, Bot Protection, Audit)

**Goal**: Build all cross-cutting infrastructure. Every shared interface gets a mock/local implementation. Middleware pipeline matches doc 07 §13.

**Depends on**: Phase 1 ✅

### Steps

- [x] **2.1** Implement `TenantResolutionMiddleware` + `TenantContext`:
  - [x] Extract slug from URL path `/{slug}/...`
  - [x] Look up Tenant in `CoreDbContext` (cached per request)
  - [x] Populate scoped `ITenantContext`
  - [x] Pass through for non-tenant routes (`/`, `/pricing`, `/health`, `/super-admin/*`, `/register/*`)
  - [x] Return 404 for unknown tenant slugs
- [x] **2.2** Register `TenantDbContext` with dynamic connection string factory:
  - [x] Reads `ITenantContext.Slug` to build `db/tenants/{slug}.db`
  - [x] Uses `WalModeInterceptor`
  - [x] Only resolves connection when `ITenantContext.IsTenantRequest` is true
- [x] **2.3** Implement `ConsoleEmailService` (`IEmailService`):
  - [x] Logs all emails to terminal with `★ MAGIC LINK` marker for magic link emails
  - [x] Registered when `Email:Provider=Console` (default)
- [x] **2.4** Implement `MockBotProtection` (`IBotProtection`):
  - [x] Always returns `true`
  - [x] Logs `[MOCK BOT PROTECTION]` to terminal
  - [x] Registered when `Turnstile:Provider=Mock` (default)
- [x] **2.5** Implement `NullAuditWriter` (`IAuditWriter`):
  - [x] No-op implementation (temporary until Phase 5 Audit module)
  - [x] Registered as default
- [x] **2.6** Implement `MockBillingService` (`IBillingService`):
  - [x] Auto-approves `InitializeSubscriptionAsync` (creates active Subscription + mock Payment)
  - [x] Returns `Active` for `GetSubscriptionStatusAsync`
  - [x] Logs all operations with `[MOCK BILLING]`
  - [x] Registered when `Billing:Provider=Mock` (default)
- [x] **2.7** Add security headers middleware:
  - [x] `X-Content-Type-Options: nosniff`
  - [x] `X-Frame-Options: DENY`
  - [x] `Referrer-Policy: strict-origin-when-cross-origin`
  - [x] `Content-Security-Policy` (with Turnstile allowlist)
  - [x] `Permissions-Policy`
- [x] **2.8** Add rate limiting configuration:
  - [x] Global: 100 requests/min per IP
  - [x] `strict`: 5/min (auth endpoints)
  - [x] `registration`: 3/5min (signup)
  - [x] `webhook`: 50/min (Paystack)
  - [x] Custom 429 HTML response
- [x] **2.9** Add remaining infrastructure:
  - [x] Forwarded headers (for reverse proxy)
  - [x] Data protection (file-system keys in `db/keys/`)
  - [x] Response compression (Brotli + Gzip)
  - [x] Health checks (core DB + tenant directory)
- [x] **2.10** Update `Program.cs` middleware pipeline to full 13-step order:
  - [x] ResponseCompression → ForwardedHeaders → SecurityHeaders → ExceptionHandler → StaticFiles → Routing → RateLimiter → TenantResolution → Authentication → Authorization → CurrentUser → HealthChecks → MVC
  - [x] Authentication/Authorization registered but no schemes yet (Phase 3)
- [x] **2.11** Update config files:
  - [x] `appsettings.json` — full structure per doc 08 §5
  - [x] `appsettings.Development.json` — minimal overrides

### Build & Test

- [x] `dotnet build` passes with zero errors
- [x] `dotnet test` passes — new tests:
  - [x] `TenantResolutionMiddlewareTests` — sets slug for valid tenant, 404 for unknown, passthrough for `/`, `/pricing`, `/health`
  - [x] `ConsoleEmailServiceTests` — doesn't throw on valid args
  - [x] `MockBillingServiceTests` — returns Success=true, creates Subscription in CoreDbContext
  - [x] `MockBotProtectionTests` — returns true for any input including null
  - [x] Integration: app starts with full pipeline, `/health` healthy, security headers present

### QA Handover — Phase 2

```
When I hand this over, verify:

1. `dotnet run --project src` starts clean
2. Browser dev tools → Network tab → any response:
   - X-Content-Type-Options: nosniff
   - X-Frame-Options: DENY
   - Referrer-Policy: strict-origin-when-cross-origin
   - Content-Security-Policy header present
3. Navigate to /some-random-slug/ → 404 page
4. Navigate to / → loads normally (not treated as tenant route)
5. Navigate to /health → "Healthy"
6. appsettings.json has the full config structure (Billing, Email,
   Turnstile, FeatureFlags sections all present with mock defaults)
7. Terminal shows no errors on startup
```

**QA Status**: [ ] Not started · [ ] Issues raised · [x] Signed off ✅

**QA Notes**:
> _(Write any issues or observations here)_

---

## Phase 3 — Authentication (Magic Link, Super Admin Login, Current User)

**Goal**: Super admin can log in via magic link. Dual cookie auth schemes. `ICurrentUser` populated per request. First phase with real login.

**Depends on**: Phase 2 ✅

### Steps

- [x] **3.1** Configure dual cookie authentication schemes:
  - [x] `SuperAdmin` scheme — cookie `.SuperAdmin.Auth`, 24h sliding, login path `/super-admin/login`
  - [x] `Tenant` scheme — cookie `.Tenant.Auth`, 12h sliding, dynamic login path `/{slug}/login`
- [x] **3.2** Configure authorization policies:
  - [x] `SuperAdmin` — requires SuperAdmin scheme + `IsSuperAdmin=true` claim
  - [x] `TenantUser` — requires Tenant scheme + `TenantSlug` claim
  - [x] `TenantAdmin` — requires Tenant scheme + `TenantSlug` claim + Admin role
- [x] **3.3** Implement `MagicLinkService`:
  - [x] `GenerateTokenAsync(email, tenantSlug?)` — 32 random bytes → Base64Url → SHA256 hash stored in CoreDbContext MagicLinkTokens
  - [x] `VerifyTokenAsync(rawToken)` — re-hash → lookup → check expiry → check not used → mark used → return result with email + tenantSlug
  - [x] Token expiry: configurable (default 15 min)
- [x] **3.4** Create `AuthModule` implementing `IModule`:
  - [x] Register `MagicLinkService`
  - [x] Register `ICurrentUser` as scoped
  - [x] Register `MagicLinkCleanupService` (BackgroundService, purge tokens > 24h, runs every hour)
- [x] **3.5** Create `SuperAdminAuthController`:
  - [x] `GET /super-admin/login` — login form view
  - [x] `POST /super-admin/login` — validate email in SuperAdmins table → generate magic link → send via IEmailService → show "check email" message
  - [x] `GET /super-admin/verify?token=xxx` — verify token → issue SuperAdmin cookie (ClaimsPrincipal with IsSuperAdmin=true) → redirect to `/super-admin`
  - [x] `POST /super-admin/logout` — sign out, clear cookie
- [x] **3.6** Implement `CurrentUser` + `CurrentUserMiddleware`:
  - [x] `CurrentUser` class implementing `ICurrentUser`
  - [x] Middleware reads cookie claims → populates scoped `ICurrentUser`
  - [x] Cross-tenant isolation: cookie's TenantSlug claim must match URL slug
  - [x] For super admin: `IsSuperAdmin=true`, no tenant context needed
- [x] **3.7** Create `TenantAuthController`:
  - [x] `GET /{slug}/login` — login form (tenant-branded)
  - [x] `POST /{slug}/login` — validate email in TenantDbContext (Identity UserManager) → generate magic link → send via IEmailService
  - [x] `GET /{slug}/verify?token=xxx` — verify token → load user roles/permissions from TenantDbContext → issue Tenant cookie → redirect to `/{slug}/`
  - [x] `POST /{slug}/logout` — sign out
  - [x] Note: full QA requires a provisioned tenant (Phase 4), but controller is built now
- [x] **3.8** Create `HasPermissionAttribute` + `HasPermissionFilter`:
  - [x] Attribute takes permission key string
  - [x] Filter checks `ICurrentUser.HasPermission(key)` → 403 if missing
- [x] **3.9** Create tag helpers:
  - [x] `has-permission` — shows content only if user has specified permission
  - [x] `is-super-admin` — shows content only for super admin
  - [x] `is-authenticated` — shows content only for authenticated users
- [x] **3.10** Register `AuthModule` in module array in `Program.cs`
- [x] **3.11** Create super admin login view + "check your email" view + verify error view

### Build & Test

- [x] `dotnet build` passes
- [x] `dotnet test` passes — new tests:
  - [x] `MagicLinkServiceTests` — generate returns non-null, stores hashed version, hash ≠ raw, verify valid succeeds, verify expired fails, verify used fails, verify bogus fails
  - [x] `CurrentUserTests` — reads claims correctly (email, roles, permissions, tenant slug, isSuperAdmin)
  - [x] `HasPermissionFilterTests` — allows with permission, denies without
  - [x] Integration: `POST /super-admin/login` with valid email → 200 + magic link in logs; `GET /super-admin/verify?token=valid` → cookie set + redirect; invalid token → error

### QA Handover — Phase 3

```
When I hand this over, verify:

1. Navigate to https://localhost:5001/super-admin/login
2. Enter "admin@localhost" → submit
3. Terminal shows: ★ MAGIC LINK for admin@localhost: https://localhost:5001/super-admin/verify?token=...
4. Copy the URL from terminal → paste in browser
5. You are redirected (to /super-admin which will 404 — that's expected,
   the dashboard is Phase 6)
6. Browser dev tools → Application → Cookies:
   - `.SuperAdmin.Auth` cookie exists
   - HttpOnly, Secure flags set
7. Navigate to /super-admin/login again → should redirect (already logged in)
8. Test invalid token: /super-admin/verify?token=bogus123 → error page
9. Test expired scenario: wait 15+ minutes (or I'll provide a short-expiry
   test config) → token should fail
10. Logout: POST /super-admin/logout → cookie cleared
```

**QA Status**: [ ] Not started · [ ] Issues raised · [x] Signed off ✅

**QA Notes**:
> _(Write any issues or observations here)_

---

## Phase 4 — Registration, Tenant Provisioning & Feature Flags

**Goal**: New tenants can register. Tenant databases are created and migrated. Feature flags functional. Multi-tenancy is real.

**Depends on**: Phase 3 ✅

### Steps

- [x] **4.1** Implement `TenantProvisioner` (`ITenantProvisioner`):
  - [x] Creates `db/tenants/{slug}.db` file
  - [x] Applies `TenantDbContext.Database.EnsureCreated()` (migrations in Phase 8)
  - [x] Sets PRAGMA journal_mode=WAL
  - [x] Seeds default roles: Admin (system), Member (system)
  - [x] Seeds all permissions from `PermissionDefinitions.GetAll()`
  - [x] Assigns all permissions to Admin role
  - [x] Creates admin `AppUser` with email from registration
  - [x] Updates Tenant status to Active in CoreDbContext
- [x] **4.2** Create `RegistrationModule` implementing `IModule`:
  - [x] Register `TenantProvisioner`
  - [x] Register `ReservedSlugs`
- [x] **4.3** Create `ReservedSlugs` static class:
  - [x] Blocklist: admin, api, app, billing, register, super-admin, login, pricing, about, contact, legal, health, etc.
  - [x] `IsReserved(slug)` method
- [x] **4.4** Create `RegistrationController`:
  - [x] `GET /register` — form with plan selector (loads plans from CoreDbContext)
  - [x] `GET /register/check-slug?slug=xxx` — HTMX inline validation (available/taken/reserved/too-short)
  - [x] `POST /register` — validate Turnstile → validate slug → create Tenant (PendingSetup) → free plan: provision + create subscription + send welcome email → paid plan: MockBillingService auto-approves + provision
  - [x] `GET /register/callback?reference=xxx` — Paystack callback (stubbed for Phase 8)
  - [x] `RegisterRequest` DTO with validation attributes
- [x] **4.5** Create registration views:
  - [x] `Index.cshtml` — registration form
  - [x] `_PlanSelector.cshtml` — plan cards partial
  - [x] `_SlugValidator.cshtml` — HTMX inline response
  - [x] `Success.cshtml` — post-registration success page
  - [x] Create minimal `_MarketingLayout.cshtml` (navbar + footer, fleshed out Phase 7)
- [x] **4.6** Create `FeatureFlagsModule` implementing `IModule`:
  - [x] Register `DatabaseFeatureDefinitionProvider`
  - [x] Register `TenantPlanFeatureFilter`
  - [x] Register `FeatureService` as `IFeatureService`
  - [x] Register Microsoft.FeatureManagement services
- [x] **4.7** Implement `DatabaseFeatureDefinitionProvider`:
  - [x] Reads Features from CoreDbContext
  - [x] 5-minute in-memory cache
  - [x] Each feature registered with `TenantPlan` filter
- [x] **4.8** Implement `TenantPlanFeatureFilter`:
  - [x] Resolve current tenant's plan via ITenantContext
  - [x] Check per-tenant overrides first (with expiry)
  - [x] Fall through to plan membership (PlanFeature table)
- [x] **4.9** Implement `FeatureService` (`IFeatureService`):
  - [x] Wraps `IFeatureManager`
  - [x] `AllEnabledLocally` override for dev (returns true for everything)
- [x] **4.10** Create `FeatureDefinitions` constants class (if not done in Phase 1):
  - [x] All feature keys as string constants
- [x] **4.11** Register `RegistrationModule` + `FeatureFlagsModule` in module array
- [x] **4.12** Add tenant MVC route: `{slug}/{controller=Home}/{action=Index}/{id?}`

### Build & Test

- [x] `dotnet build` passes
- [x] `dotnet test` passes — new tests:
  - [x] `TenantProvisionerTests` — creates SQLite file, Identity tables exist, Admin + Member roles seeded, permissions seeded, admin user created, tenant status updated to Active
  - [ ] `ReservedSlugsTests` — reserved slugs validated inside TenantProvisionerService (integrated, not separate class)
  - [ ] `RegistrationControllerTests` — deferred: requires WebApplicationFactory setup (Phase 5+)
  - [ ] `DatabaseFeatureDefinitionProviderTests` — deferred: requires CoreDbContext seeding in test harness
  - [ ] `TenantPlanFeatureFilterTests` — deferred: requires full DI setup with ITenantContext
  - [x] `FeatureServiceTests` — returns true for all when AllEnabledLocally=true
  - [x] Integration: POST /register → tenant created, DB file exists, welcome email in terminal

### QA Handover — Phase 4

```
When I hand this over, verify:

1. Navigate to https://localhost:5001/register
2. Form shows: Organisation Name, Email, Slug, Plan selector (4 plans), Billing cycle
3. Type slug "admin" → inline shows "reserved"
4. Type slug "ab" → inline shows "minimum 3 characters"
5. Type slug "testcorp" → inline shows "Available ✓"
6. Fill in: Organisation = "Test Corp", Email = "test@test.com",
   Slug = "testcorp", select Free plan → submit
7. Redirected to success page
8. Verify db/tenants/testcorp.db exists:
   - Open with SQLite Viewer
   - AspNetUsers table: 1 row (test@test.com)
   - AspNetRoles table: 2 rows (Admin, Member)
   - Permissions table: populated
   - RolePermissions table: Admin role has all permissions
9. Verify db/core.db:
   - Tenants table: new row, slug=testcorp, status=Active
   - Subscriptions table: new row, status=Active (free plan)
10. Terminal shows ★ MAGIC LINK welcome email for test@test.com
11. Navigate to https://localhost:5001/testcorp/login
12. Enter test@test.com → submit → check terminal for magic link
13. Paste magic link URL → logged in as tenant admin
14. Cookie `.Tenant.Auth` exists
15. Register SECOND tenant (slug "other", different email)
16. Verify db/tenants/other.db exists (separate DB)
17. Try registering slug "testcorp" again → "Already taken"
```

**QA Status**: [ ] Not started · [ ] Issues raised · [x] Signed off ✅

**QA Notes**:
> `/register/callback` returns 404 — this is intentional (stubbed for Phase 8 Paystack integration). All other items verified and passing.

---

## Phase 5 — Refactor Notes Module + Audit Module

**Goal**: Notes module upgraded to full spec — `IModule`, `TenantDbContext`, `[Authorize]`, `[HasPermission]`, `[FeatureGate]`, tenant-scoped. Real audit trail.

**Depends on**: Phase 4 ✅

### Steps

- [x] **5.1** Refactor `NotesModule` to implement `IModule`:
  - [x] Replace static `AddNotesModule()` extension method
  - [x] Implement `RegisterServices`, `RegisterMiddleware`, `RegisterMvc`
- [x] **5.2** Change `Note.Id` from `int` to `Guid`:
  - [x] Update entity
  - [x] Update service interface + implementation
  - [x] Update controller (all action parameters)
  - [x] Update views (all id references)
- [x] **5.3** Move Note entity to `TenantDbContext`:
  - [x] Remove from old AppDbContext references
  - [x] Update `NotesService` to inject `TenantDbContext`
- [x] **5.4** Add auth attributes to `NotesController`:
  - [x] `[Authorize(Policy = "TenantUser")]` on controller
  - [x] `[HasPermission("notes.create")]` on Create actions
  - [x] `[HasPermission("notes.edit")]` on Edit actions
  - [x] `[HasPermission("notes.delete")]` on Delete actions
- [x] **5.5** Add `[RequireFeature(FeatureDefinitions.Notes)]` to `NotesController` (custom attribute using IFeatureService — respects AllEnabledLocally dev override)
- [x] **5.6** Update Notes views:
  - [x] `has-permission` tag helper on create/edit/delete buttons
  - [x] Tenant-scoped URLs (`/{slug}/notes/...`) — via existing tenant route in MapEndpoints
- [x] **5.7** Create `AuditModule` implementing `IModule`:
  - [x] Implement `ChannelAuditWriter` (`IAuditWriter`) using `Channel<AuditEntry>`
  - [x] Background consumer writes to `AuditDbContext`
  - [x] Replace `NullAuditWriter` registration
- [x] **5.8** Add audit calls to `NotesService`:
  - [x] Write entry on create (action: "Created", entity: "Note")
  - [x] Write entry on update (action: "Updated", include old/new values)
  - [x] Write entry on delete (action: "Deleted")
- [x] **5.9** Update Notes route to `/{slug}/notes/...`
- [x] **5.10** Register `NotesModule` + `AuditModule` in module array
- [x] **5.11** Rewrite `NotesControllerTests` for new tenant-scoped setup

### Build & Test

- [x] `dotnet build` passes
- [x] `dotnet test` passes — new/updated tests:
  - [x] `NotesServiceTests` — CRUD operations work against TenantDbContext, audit entries written
  - [x] `AuditWriterTests` — entries appear in AuditDbContext after background processing
  - [x] Integration: authenticated tenant user → CRUD notes at /{slug}/notes; unauthenticated → redirect to login
  - [x] Integration: audit entries in db/audit.db after note operations

### QA Handover — Phase 5

```
When I hand this over, verify:

1. Log in to testcorp tenant (from Phase 4): /testcorp/login → magic link
2. Navigate to /testcorp/notes → Notes page loads
3. Create a note (title, content, color) → appears in list
4. Edit the note → changes saved
5. Pin/unpin the note → state toggles
6. Delete the note → removed
7. Check db/tenants/testcorp.db → Notes table has your data
8. Check data/audit.db → AuditEntries table has rows:
   - Created, Updated, Deleted actions for Note entity
   - TenantSlug = "testcorp"
   - UserEmail = your email
9. Open incognito → /testcorp/notes → redirected to /testcorp/login
10. Log in to "other" tenant (from Phase 4) → /other/notes → EMPTY
    (no cross-tenant data leakage)
11. Create a note in "other" tenant → only visible in /other/notes
12. Check data/tenants/other.db → Notes table has only "other" tenant's notes
```

**QA Status**: [ ] Not started · [ ] Issues raised · [x] Signed off ✅

**QA Notes**:
> QA uncovered multiple runtime issues, all resolved:
> - Cross-tenant cookie access: CurrentUserMiddleware now signs out + redirects on slug mismatch
> - Missing layout on module views: added _ViewStart.cshtml to all module Views directories
> - Shared auth views (MagicLinkSent/Error): moved to Modules/Auth/Views/Shared/
> - Verify → dashboard redirect loop: HomeController uses explicit AuthenticateAsync(AuthSchemes.Tenant) + slug claim validation
> - Dashboard auth bypass: added slug claim comparison after AuthenticateAsync
> - Audit trail empty: refactored from TenantDbContext.SaveChangesAsync override to EF Core AuditSaveChangesInterceptor (modeled on clinicdiary). Root cause was IAuditWriter resolving to null via optional constructor param.
> - Test organization: 19 test files restructured into proper folder hierarchy matching source
> - AuditWriterTests SQLite concurrency: switched to temp file-based DB + ClearAllPools()
> - All 56 tests passing.

---

## Phase 6 — Super Admin & Tenant Admin Dashboards

**Goal**: Super admin manages tenants/plans/features. Tenant admins manage users/roles/billing. The management layer.

**Depends on**: Phase 5 ✅

### Steps

- [x] **6.1** Create `SuperAdminModule` implementing `IModule`
- [x] **6.2** Create `SuperAdminController` — `[Authorize(Policy = "SuperAdmin")]`:
  - [x] `GET /super-admin` — dashboard (tenant count, active subscriptions, recent registrations)
  - [x] `GET /super-admin/tenants` — tenant list with status badges + search
  - [x] `GET /super-admin/tenants/{id}` — tenant detail (info, subscription, user count)
  - [x] `POST /super-admin/tenants/{id}/suspend` — suspend tenant
  - [x] `POST /super-admin/tenants/{id}/activate` — reactivate tenant
  - [x] `GET /super-admin/plans` — plan list
  - [x] `POST /super-admin/plans` — create/edit plan
  - [x] `GET /super-admin/features` — feature list with plan matrix
  - [x] `POST /super-admin/features/{id}/toggle` — toggle feature for plan
  - [x] `POST /super-admin/features/override` — per-tenant override
- [x] **6.3** Create super admin views:
  - [x] Admin layout (sidebar: Dashboard, Tenants, Plans, Features)
  - [x] Dashboard with stat cards
  - [x] Tenant list + detail views
  - [x] Plan management views (CRUD modals)
  - [x] Feature matrix view (plan × feature grid with toggles)
- [x] **6.4** Implement `FeatureCacheInvalidator`:
  - [x] Clears `DatabaseFeatureDefinitionProvider` cache when features change
- [x] **6.5** Create `TenantAdminModule` implementing `IModule`
- [x] **6.6** Create `TenantAdminController` — `[Authorize(Policy = "TenantAdmin")]`:
  - [x] `GET /{slug}/admin/users` — user list
  - [x] `POST /{slug}/admin/users/invite` — invite user (create AppUser + send magic link)
  - [x] `POST /{slug}/admin/users/{id}/deactivate` — deactivate user
  - [x] `GET /{slug}/admin/roles` — role list with permissions
  - [x] `POST /{slug}/admin/users/{id}/roles` — assign role to user
- [x] **6.7** Create `TenantBillingController` — `[Authorize(Policy = "TenantAdmin")]`:
  - [x] `GET /{slug}/billing` — subscription status card + invoice list
  - [x] `POST /{slug}/billing/change-plan` — change plan (via IBillingService)
  - [x] `POST /{slug}/billing/cancel` — cancel subscription
- [x] **6.8** Create tenant admin views:
  - [x] User list, invite modal
  - [x] Role list, role-permission view
  - [x] Billing dashboard, invoice list, change plan modal, cancel confirm
- [x] **6.9** Update tenant `_Layout.cshtml` sidebar:
  - [x] Dashboard, Notes, Users (admin only), Billing (admin only)
  - [x] Use `has-permission` tag helpers
- [x] **6.10** Register `SuperAdminModule` + `TenantAdminModule` in module array

### Build & Test

- [x] `dotnet build` passes
- [x] `dotnet test` passes — 83 tests (56 existing + 27 new):
  - [x] `SuperAdminServiceTests` — dashboard stats, tenant CRUD, plan CRUD, feature matrix, toggle, overrides (15 tests)
  - [x] `TenantAdminServiceTests` — user list, invite + email, deactivate/activate, roles assign/remove (8 tests)
  - [x] `TenantBillingTests` — change plan calls IBillingService, cancel calls CancelSubscriptionAsync (3 tests)
  - [x] Integration: HomeControllerTests still passing (public page + health check)

### QA Handover — Phase 6

```
When I hand this over, verify:

SUPER ADMIN:
1. Log in as super admin: /super-admin/login → admin@localhost → magic link
2. /super-admin → dashboard with stats (tenant count, subscriptions)
3. /super-admin/tenants → list shows tenants from Phase 4
4. Click a tenant → detail page (name, slug, plan, status, user count)
5. Suspend a tenant → status changes to Suspended
6. Try accessing suspended tenant's /testcorp/login → should be blocked
7. Reactivate → access restored
8. /super-admin/plans → 4 plans listed
9. Edit Professional plan price to R599 → saved
10. /super-admin/features → feature matrix grid
11. Toggle a feature off for a plan → change persists

TENANT ADMIN:
12. Log in to testcorp: /testcorp/login → magic link
13. Sidebar shows: Dashboard, Notes, Users, Billing
14. /testcorp/admin/users → you listed as admin
15. Invite new user: enter email "user2@test.com" → terminal shows magic link
16. /testcorp/billing → shows plan name, status, next billing date
17. Change plan → MockBillingService approves → plan updated
18. Verify core.db: Subscription updated with new PlanId
```

**QA Status**: [ ] Not started · [ ] Issues raised · [ ] Signed off ✅

**QA Notes**:
> Phase 6 implementation complete:
> - SuperAdminModule expanded: ISuperAdminService/SuperAdminService with dashboard, tenant management, plan CRUD, feature matrix
> - SuperAdminController: dashboard stats, tenant list/detail/suspend/activate, plan edit modal, feature toggle, per-tenant overrides
> - Admin views: _AdminLayout with sidebar (DaisyUI business theme), dashboard stat cards, tenant table with search, plan table with edit modals, feature matrix with plan×feature toggle grid
> - FeatureCacheInvalidator wired into feature toggle + override actions
> - TenantAdminModule: TenantAdminController (user list, invite via magic link, deactivate/activate, role list, assign/remove role)
> - TenantBillingController: billing dashboard with plan info + invoice list, change plan modal, cancel subscription — all via IBillingService
> - Tenant sidebar updated: Dashboard, Notes, Users (admin), Billing (admin) with has-permission guards
> - All views use Swap.Htmx patterns: SwapController, SwapView(), SwapResponse().Build(), modals via hx-target="#modal-container"
> - 83/83 tests passing

---

## Phase 7 — Marketing Module

**Goal**: Public-facing website — landing page, pricing from DB, about, contact, legal pages, SEO.

**Depends on**: Phase 6 ✅

### Steps

- [x] **7.1** Create `MarketingModule` implementing `IModule`
- [x] **7.2** Create `_MarketingLayout.cshtml` (full version):
  - [x] Public navbar: Logo, Pricing, About, Contact, Get Started, Sign In
  - [x] Footer: About, Contact, Terms, Privacy, copyright
  - [x] Open Graph meta tags from ViewData
  - [x] Mobile responsive (DaisyUI dropdown menu)
- [x] **7.3** Create `MarketingController`:
  - [x] `GET /` — landing page
  - [x] `GET /pricing` — plans from CoreDbContext
  - [x] `GET /about` — static
  - [x] `GET /contact` — form
  - [x] `POST /contact` — rate-limited, Turnstile-protected, sends email
  - [x] `GET /legal/terms` — static
  - [x] `GET /legal/privacy` — static
  - [x] `GET /login-redirect` — slug input → redirect to `/{slug}/login`
  - [x] `GET /sitemap.xml` — dynamic, cached
  - [x] `GET /robots.txt` — static, cached
- [x] **7.4** Create landing page view:
  - [x] Hero section, feature cards, CTA
- [x] **7.5** Create pricing page view:
  - [x] Plan cards from DB, monthly/annual toggle, FAQ accordion
- [x] **7.6** Create sign-in modal:
  - [x] Slug input → redirects to `/{slug}/login`
- [x] **7.7** Create static pages: About, Contact, Terms, Privacy
- [x] **7.8** Set up route priority in `Program.cs`:
  - [x] Marketing routes (explicit) registered BEFORE tenant catch-all
- [x] **7.9** Replace old `HomeController` / `Home/Index.cshtml` with marketing landing page
- [x] **7.10** Register `MarketingModule` in module array

### Build & Test

- [x] `dotnet build` passes
- [x] `dotnet test` passes — new tests:
  - [x] Integration: `GET /` → 200 with marketing layout; `GET /pricing` → plan cards; `GET /sitemap.xml` → valid XML; `GET /robots.txt` → correct directives
  - [x] Integration: `POST /contact` rate limited (6th request → 429)
  - [x] Integration: `/pricing` → `/test/notes` — route priority correct

### QA Handover — Phase 7

```
When I hand this over, verify:

1. https://localhost:5001/ → landing page with hero, features, CTA
   - No sidebar (marketing layout, not app layout)
   - Navbar: Pricing, About, Contact, Get Started, Sign In
2. Click "Pricing" → /pricing:
   - 4 plan cards with correct prices from DB
   - Monthly/Annual toggle works
   - FAQ accordion opens/closes
   - "Get Started" on a plan → /register?plan={id}
3. Click "Sign In" → modal with slug input
   - Enter "testcorp" → redirected to /testcorp/login
4. /about → renders with marketing layout
5. /contact → form renders, submit → terminal shows email
6. /legal/terms and /legal/privacy → render
7. /sitemap.xml → valid XML with marketing URLs
8. /robots.txt → Disallow: /super-admin/
9. Route priority: /pricing shows marketing page, /testcorp/notes
   shows tenant notes (no conflict)
10. If super admin edited plan prices in Phase 6 → pricing page
    reflects the updated prices
```

**QA Status**: [ ] Not started · [ ] Issues raised · [x] Signed off ✅

**QA Notes**:
> _(Write any issues or observations here)_

---

## Phase 8 — Production Integrations & Docker

**Goal**: Real Paystack, SES, Turnstile. Dockerize. Litestream. Deployment-ready.

**Depends on**: Phase 7 ✅

### Steps

- [ ] **8.1** Implement `PaystackClient` (typed HttpClient):
  - [ ] CreatePlan, ListPlans
  - [ ] InitializeTransaction, VerifyTransaction
  - [ ] CreateSubscription, DisableSubscription
  - [ ] CreateCustomer
- [ ] **8.2** Implement `PaystackBillingService` (`IBillingService`):
  - [ ] Full `InitializeSubscriptionAsync` with Paystack API calls
  - [ ] `ProcessWebhookAsync` with HMAC-SHA512 signature verification
  - [ ] Webhook handlers: charge.success, subscription.create/not_renew/disable, invoice.create/payment_failed
  - [ ] Idempotency guards on payment processing
- [ ] **8.3** Create `PaystackWebhookController`:
  - [ ] `POST /api/webhooks/paystack` — raw body read, signature verify, process
  - [ ] Always returns 200 (Paystack retries on non-200)
- [ ] **8.4** Implement `PaystackPlanSyncService` (BackgroundService):
  - [ ] On startup: list Paystack plans, compare with DB, create missing, update PaystackPlanCode
- [ ] **8.5** Implement `InvoiceGenerator`:
  - [ ] Sequential `INV-{YEAR}-{NNNN}` numbering
- [ ] **8.6** Create all Paystack DTOs:
  - [ ] Request models (Initialize, CreatePlan, CreateSubscription, CreateCustomer)
  - [ ] Response models (ApiResponse<T>, PlanResponse, TransactionResponse, etc.)
  - [ ] Webhook models (WebhookEvent, WebhookData, WebhookSubscription)
- [ ] **8.7** Add NuGet: `AWSSDK.SimpleEmailV2`
- [ ] **8.8** Implement `SesEmailService`:
  - [ ] Real AWS SES v2 API calls
  - [ ] HTML email templates (magic link, welcome, payment receipt)
  - [ ] Registered when `Email:Provider=SES`
- [ ] **8.9** Implement `TurnstileBotProtection`:
  - [ ] HTTP POST to Cloudflare verification endpoint
  - [ ] Registered when `Turnstile:Provider=Cloudflare`
- [ ] **8.10** Update registration + login views:
  - [ ] Add Turnstile widget `<div class="cf-turnstile">` + hidden input
  - [ ] Works with both standard and HTMX forms
- [ ] **8.11** Implement `LitestreamConfigSyncService` (BackgroundService):
  - [ ] Discovers tenant .db files every 5 minutes
  - [ ] Regenerates Litestream YAML config
  - [ ] Writes reload sentinel file
- [ ] **8.12** Create `litestream-wrapper.sh`:
  - [ ] Watches sentinel, restarts Litestream on config change
- [ ] **8.13** Create `BackupModule` implementing `IModule`
- [ ] **8.14** Finalize `Dockerfile` (multi-stage build):
  - [ ] SDK stage: restore + publish
  - [ ] Runtime stage: aspnet image + curl for health check
  - [ ] Create data directories
- [ ] **8.15** Finalize `docker-compose.yml`:
  - [ ] App service with health check
  - [ ] Litestream sidecar with wrapper script
  - [ ] Shared volume for data/
  - [ ] env_file reference
- [ ] **8.16** Create `docker-compose.override.yml` for local Docker testing
- [ ] **8.17** Create `.env.example` with all production environment variables
- [ ] **8.18** Create `appsettings.Production.json`
- [ ] **8.19** Switch to EF Migrations (from EnsureCreated):
  - [ ] Generate initial migration for CoreDbContext
  - [ ] Generate initial migration for TenantDbContext
  - [ ] Generate initial migration for AuditDbContext
  - [ ] Update startup to use `Migrate()` instead of `EnsureCreated()`
- [ ] **8.20** Register `BackupModule` in module array

### Build & Test

- [ ] `dotnet build` passes
- [ ] `dotnet test` passes — new tests:
  - [ ] `PaystackClientTests` — mock HttpMessageHandler for all API calls
  - [ ] `PaystackBillingServiceTests` — valid signature accepted, invalid rejected; charge.success creates Payment (idempotent); payment_failed → PastDue
  - [ ] `InvoiceGeneratorTests` — sequential numbering correct across calls
  - [ ] `LitestreamConfigSyncTests` — generates correct YAML with discovered tenant files
  - [ ] `TurnstileBotProtectionTests` — mock HTTP success → true, failure → false
  - [ ] Integration: `docker compose up --build` → app starts, health check passes

### QA Handover — Phase 8

```
When I hand this over, verify:

LOCAL (mock services still work):
1. dotnet run --project src → everything from Phases 1-7 still works
2. Registration → mock billing → provisioning → login → notes → all good

DOCKER:
3. docker compose up --build → app starts on :8080
4. http://localhost:8080/health → Healthy
5. http://localhost:8080/ → landing page
6. Full registration → login → notes flow works in Docker
7. docker compose down → docker compose up → data persists (volume)

FILES:
8. .env.example exists with all production vars documented
9. Dockerfile builds without errors
10. docker-compose.yml + docker-compose.override.yml present
11. appsettings.Production.json switches to real providers

(If Paystack test keys available):
12. Set Billing:Provider=Paystack with test keys
13. Register with paid plan → redirected to Paystack sandbox
14. Complete test payment → webhook fires → tenant provisioned
```

**QA Status**: [ ] Not started · [ ] Issues raised · [ ] Signed off ✅

**QA Notes**:
> _(Write any issues or observations here)_

---

## Post-Phase Checklist

After all 8 phases are signed off:

- [ ] All tests pass: `dotnet test` — zero failures
- [ ] App starts clean: `dotnet run --project src` — no warnings/errors
- [ ] Docker works: `docker compose up --build` — healthy
- [ ] Full E2E flow verified:
  - [ ] Landing page → Register (free) → Login → Notes CRUD → Audit trail
  - [ ] Landing page → Register (paid/mock) → Login → Notes CRUD
  - [ ] Super admin login → Manage tenants/plans/features
  - [ ] Tenant admin → Manage users/roles → Billing dashboard
- [ ] Data isolation: two tenants, no cross-tenant data leakage
- [ ] Documentation matches implementation (review docs/ against code)

---

## Bug Fix Log

Track issues found during QA across all phases:

| # | Phase | Description | Status | Resolution |
|---|-------|-------------|--------|------------|
| 1 | | | | |
| 2 | | | | |
| 3 | | | | |
| 4 | | | | |
| 5 | | | | |
