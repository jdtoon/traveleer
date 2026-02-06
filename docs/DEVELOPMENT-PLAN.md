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

- [ ] **1.1** Create `src/Shared/` folder with all interfaces:
  - [ ] `IModule.cs`
  - [ ] `ITenantContext.cs`
  - [ ] `ICurrentUser.cs`
  - [ ] `IFeatureService.cs`
  - [ ] `IEmailService.cs` (+ `EmailMessage` record)
  - [ ] `IAuditWriter.cs`
  - [ ] `IBotProtection.cs`
  - [ ] `IBillingService.cs` (+ request/result records)
  - [ ] `ITenantProvisioner.cs` (+ request/result records)
- [ ] **1.2** Add NuGet packages to `saas.csproj`:
  - [ ] `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
  - [ ] `Microsoft.FeatureManagement.AspNetCore`
- [ ] **1.3** Create `src/Data/CoreDbContext.cs` with entities:
  - [ ] `Tenant` (Id, Name, Slug, ContactEmail, Status enum, PlanId, DatabaseName)
  - [ ] `Plan` (Id, Name, Slug, MonthlyPrice, AnnualPrice, Currency, MaxUsers, PaystackPlanCode, SortOrder, IsActive, Description)
  - [ ] `Feature` (Id, Key, Name, Module, IsGlobal, IsEnabled)
  - [ ] `PlanFeature` (PlanId, FeatureId composite key, ConfigJson)
  - [ ] `TenantFeatureOverride` (Id, TenantId, FeatureId, IsEnabled, Reason, ExpiresAt)
  - [ ] `Subscription` (Id, TenantId, PlanId, Status enum, BillingCycle enum, StartDate, EndDate, NextBillingDate, PaystackSubscriptionCode, PaystackCustomerCode)
  - [ ] `Invoice` (Id, TenantId, SubscriptionId, InvoiceNumber, Amount, Currency, Status enum, IssuedDate, DueDate, PaidDate, PaystackReference)
  - [ ] `Payment` (Id, TenantId, InvoiceId, Amount, Currency, Status enum, PaystackReference, PaystackTransactionId, GatewayResponse, TransactionDate)
  - [ ] `SuperAdmin` (Id, Email, DisplayName, IsActive, LastLoginAt)
  - [ ] `MagicLinkToken` (Id, Token, Email, TenantSlug, ExpiresAt, IsUsed, UsedAt)
  - [ ] All enums: `TenantStatus`, `SubscriptionStatus`, `BillingCycle`, `InvoiceStatus`, `PaymentStatus`
  - [ ] Entity configurations (unique indexes, relationships, value conversions)
  - [ ] Connection string: `CoreDatabase`
- [ ] **1.4** Create `src/Data/TenantDbContext.cs`:
  - [ ] Extends `IdentityDbContext<AppUser, AppRole, string>`
  - [ ] `AppUser` (extends IdentityUser + DisplayName, IsActive, LastLoginAt, IAuditableEntity)
  - [ ] `AppRole` (extends IdentityRole + Description, IsSystemRole)
  - [ ] `Permission` (Id, Key, Name, Group, SortOrder)
  - [ ] `RolePermission` (RoleId, PermissionId composite key)
  - [ ] `Note` entity (Id as Guid, Title, Content, Color, IsPinned, IAuditableEntity)
  - [ ] `WalModeInterceptor` (sets PRAGMA journal_mode=WAL on connection open)
  - [ ] Dynamic connection string from `ITenantContext.Slug`
- [ ] **1.5** Create `src/Data/AuditDbContext.cs`:
  - [ ] `AuditEntry` (Id as long auto-increment, TenantSlug, EntityType, EntityId, Action, UserId, UserEmail, OldValues, NewValues, AffectedColumns, Timestamp, IpAddress, UserAgent)
  - [ ] Connection string: `AuditDatabase`
- [ ] **1.6** Update `IAuditableEntity`:
  - [ ] Rename `CreatedByUserId` → `CreatedBy`
  - [ ] Rename `ModifiedByUserId` → `UpdatedBy`
  - [ ] Update `SaveChangesAsync` override in TenantDbContext
- [ ] **1.7** Create `src/Data/Seeding/MasterDataSeeder.cs`:
  - [ ] Seed Plans: Free, Starter (R199/mo), Professional (R499/mo), Enterprise (R999/mo)
  - [ ] Seed Features from `FeatureDefinitions` constants
  - [ ] Seed PlanFeature mappings (matrix from doc 05 §10)
  - [ ] Seed default SuperAdmin (email from config)
  - [ ] Idempotent (skip if data exists)
- [ ] **1.8** Create `src/Shared/PermissionDefinitions.cs`:
  - [ ] Static class with all permission key constants grouped by module
  - [ ] `GetAll()` method returning all permissions
- [ ] **1.9** Create `src/Shared/FeatureDefinitions.cs`:
  - [ ] Static class with all feature key constants
- [ ] **1.10** Refactor `Program.cs`:
  - [ ] Register `CoreDbContext` + `AuditDbContext` with fixed connection strings
  - [ ] Configure `data/` directory structure (data/core.db, data/audit.db, data/tenants/, data/keys/)
  - [ ] Run `EnsureCreated()` + WAL mode + `MasterDataSeeder` on startup
  - [ ] Add `IModule` registration loop (empty array for now)
  - [ ] Remove old `AppDbContext` registration
  - [ ] Temporarily comment out Notes module registration (reconnected Phase 5)
- [ ] **1.11** Delete or archive old `AppDbContext.cs` (keep `IAuditableEntity.cs`, `PaginatedList.cs`)

### Build & Test

- [ ] `dotnet build` passes with zero errors
- [ ] `dotnet test` — update/skip broken Notes tests (they will be rewritten in Phase 5)
- [ ] New tests written and passing:
  - [ ] `CoreDbContextTests` — in-memory create, all entities have correct PKs/FKs, enums stored correctly
  - [ ] `MasterDataSeederTests` — seeds expected plans (4), features, plan-features, super admin; idempotent on second run
  - [ ] `PermissionDefinitionsTests` — `GetAll()` non-empty, no duplicate keys
  - [ ] `TenantDbContextTests` — temp SQLite file, Identity tables exist, Note table exists
  - [ ] `AuditDbContextTests` — AuditEntry table exists, auto-increment Id works
  - [ ] Integration: app starts, `/health` returns healthy

### QA Handover — Phase 1

```
When I hand this over, verify:

1. `dotnet run --project src` starts without errors
2. `data/core.db` exists — open with SQLite Viewer:
   - Plans table: 4 rows (Free, Starter, Professional, Enterprise)
   - Features table: populated with feature keys
   - PlanFeatures table: mappings exist
   - SuperAdmins table: 1 row with admin@localhost
3. `data/audit.db` exists — AuditEntries table present (empty)
4. `data/tenants/` directory exists (empty)
5. `data/keys/` directory exists
6. Browser: https://localhost:5001 loads (basic page, Notes nav missing — expected)
7. Browser: https://localhost:5001/health returns "Healthy"
8. Terminal: no EF Core or startup errors in logs
```

**QA Status**: [ ] Not started · [ ] Issues raised · [ ] Signed off ✅

**QA Notes**:
> _(Write any issues or observations here)_

---

## Phase 2 — Infrastructure Services (Tenant Resolution, Email, Bot Protection, Audit)

**Goal**: Build all cross-cutting infrastructure. Every shared interface gets a mock/local implementation. Middleware pipeline matches doc 07 §13.

**Depends on**: Phase 1 ✅

### Steps

- [ ] **2.1** Implement `TenantResolutionMiddleware` + `TenantContext`:
  - [ ] Extract slug from URL path `/{slug}/...`
  - [ ] Look up Tenant in `CoreDbContext` (cached per request)
  - [ ] Populate scoped `ITenantContext`
  - [ ] Pass through for non-tenant routes (`/`, `/pricing`, `/health`, `/super-admin/*`, `/register/*`)
  - [ ] Return 404 for unknown tenant slugs
- [ ] **2.2** Register `TenantDbContext` with dynamic connection string factory:
  - [ ] Reads `ITenantContext.Slug` to build `data/tenants/{slug}.db`
  - [ ] Uses `WalModeInterceptor`
  - [ ] Only resolves connection when `ITenantContext.IsTenantRequest` is true
- [ ] **2.3** Implement `ConsoleEmailService` (`IEmailService`):
  - [ ] Logs all emails to terminal with `★ MAGIC LINK` marker for magic link emails
  - [ ] Registered when `Email:Provider=Console` (default)
- [ ] **2.4** Implement `MockBotProtection` (`IBotProtection`):
  - [ ] Always returns `true`
  - [ ] Logs `[MOCK BOT PROTECTION]` to terminal
  - [ ] Registered when `Turnstile:Provider=Mock` (default)
- [ ] **2.5** Implement `NullAuditWriter` (`IAuditWriter`):
  - [ ] No-op implementation (temporary until Phase 5 Audit module)
  - [ ] Registered as default
- [ ] **2.6** Implement `MockBillingService` (`IBillingService`):
  - [ ] Auto-approves `InitializeSubscriptionAsync` (creates active Subscription + mock Payment)
  - [ ] Returns `Active` for `GetSubscriptionStatusAsync`
  - [ ] Logs all operations with `[MOCK BILLING]`
  - [ ] Registered when `Billing:Provider=Mock` (default)
- [ ] **2.7** Add security headers middleware:
  - [ ] `X-Content-Type-Options: nosniff`
  - [ ] `X-Frame-Options: DENY`
  - [ ] `Referrer-Policy: strict-origin-when-cross-origin`
  - [ ] `Content-Security-Policy` (with Turnstile allowlist)
  - [ ] `Permissions-Policy`
- [ ] **2.8** Add rate limiting configuration:
  - [ ] Global: 100 requests/min per IP
  - [ ] `strict`: 5/min (auth endpoints)
  - [ ] `registration`: 3/5min (signup)
  - [ ] `webhook`: 50/min (Paystack)
  - [ ] Custom 429 HTML response
- [ ] **2.9** Add remaining infrastructure:
  - [ ] Forwarded headers (for reverse proxy)
  - [ ] Data protection (file-system keys in `data/keys/`)
  - [ ] Response compression (Brotli + Gzip)
  - [ ] Health checks (core DB + tenant directory)
- [ ] **2.10** Update `Program.cs` middleware pipeline to full 13-step order:
  - [ ] ResponseCompression → ForwardedHeaders → SecurityHeaders → ExceptionHandler → StaticFiles → Routing → RateLimiter → TenantResolution → Authentication → Authorization → CurrentUser → HealthChecks → MVC
  - [ ] Authentication/Authorization registered but no schemes yet (Phase 3)
- [ ] **2.11** Update config files:
  - [ ] `appsettings.json` — full structure per doc 08 §5
  - [ ] `appsettings.Development.json` — minimal overrides

### Build & Test

- [ ] `dotnet build` passes with zero errors
- [ ] `dotnet test` passes — new tests:
  - [ ] `TenantResolutionMiddlewareTests` — sets slug for valid tenant, 404 for unknown, passthrough for `/`, `/pricing`, `/health`
  - [ ] `ConsoleEmailServiceTests` — doesn't throw on valid args
  - [ ] `MockBillingServiceTests` — returns Success=true, creates Subscription in CoreDbContext
  - [ ] `MockBotProtectionTests` — returns true for any input including null
  - [ ] Integration: app starts with full pipeline, `/health` healthy, security headers present

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

**QA Status**: [ ] Not started · [ ] Issues raised · [ ] Signed off ✅

**QA Notes**:
> _(Write any issues or observations here)_

---

## Phase 3 — Authentication (Magic Link, Super Admin Login, Current User)

**Goal**: Super admin can log in via magic link. Dual cookie auth schemes. `ICurrentUser` populated per request. First phase with real login.

**Depends on**: Phase 2 ✅

### Steps

- [ ] **3.1** Configure dual cookie authentication schemes:
  - [ ] `SuperAdmin` scheme — cookie `.SuperAdmin.Auth`, 24h sliding, login path `/super-admin/login`
  - [ ] `Tenant` scheme — cookie `.Tenant.Auth`, 12h sliding, dynamic login path `/{slug}/login`
- [ ] **3.2** Configure authorization policies:
  - [ ] `SuperAdmin` — requires SuperAdmin scheme + `IsSuperAdmin=true` claim
  - [ ] `TenantUser` — requires Tenant scheme + `TenantSlug` claim
  - [ ] `TenantAdmin` — requires Tenant scheme + `TenantSlug` claim + Admin role
- [ ] **3.3** Implement `MagicLinkService`:
  - [ ] `GenerateTokenAsync(email, tenantSlug?)` — 32 random bytes → Base64Url → SHA256 hash stored in CoreDbContext MagicLinkTokens
  - [ ] `VerifyTokenAsync(rawToken)` — re-hash → lookup → check expiry → check not used → mark used → return result with email + tenantSlug
  - [ ] Token expiry: configurable (default 15 min)
- [ ] **3.4** Create `AuthModule` implementing `IModule`:
  - [ ] Register `MagicLinkService`
  - [ ] Register `ICurrentUser` as scoped
  - [ ] Register `MagicLinkCleanupService` (BackgroundService, purge tokens > 24h, runs every hour)
- [ ] **3.5** Create `SuperAdminAuthController`:
  - [ ] `GET /super-admin/login` — login form view
  - [ ] `POST /super-admin/login` — validate email in SuperAdmins table → generate magic link → send via IEmailService → show "check email" message
  - [ ] `GET /super-admin/verify?token=xxx` — verify token → issue SuperAdmin cookie (ClaimsPrincipal with IsSuperAdmin=true) → redirect to `/super-admin`
  - [ ] `POST /super-admin/logout` — sign out, clear cookie
- [ ] **3.6** Implement `CurrentUser` + `CurrentUserMiddleware`:
  - [ ] `CurrentUser` class implementing `ICurrentUser`
  - [ ] Middleware reads cookie claims → populates scoped `ICurrentUser`
  - [ ] Cross-tenant isolation: cookie's TenantSlug claim must match URL slug
  - [ ] For super admin: `IsSuperAdmin=true`, no tenant context needed
- [ ] **3.7** Create `TenantAuthController`:
  - [ ] `GET /{slug}/login` — login form (tenant-branded)
  - [ ] `POST /{slug}/login` — validate email in TenantDbContext (Identity UserManager) → generate magic link → send via IEmailService
  - [ ] `GET /{slug}/verify?token=xxx` — verify token → load user roles/permissions from TenantDbContext → issue Tenant cookie → redirect to `/{slug}/`
  - [ ] `POST /{slug}/logout` — sign out
  - [ ] Note: full QA requires a provisioned tenant (Phase 4), but controller is built now
- [ ] **3.8** Create `HasPermissionAttribute` + `HasPermissionFilter`:
  - [ ] Attribute takes permission key string
  - [ ] Filter checks `ICurrentUser.HasPermission(key)` → 403 if missing
- [ ] **3.9** Create tag helpers:
  - [ ] `has-permission` — shows content only if user has specified permission
  - [ ] `is-super-admin` — shows content only for super admin
  - [ ] `is-authenticated` — shows content only for authenticated users
- [ ] **3.10** Register `AuthModule` in module array in `Program.cs`
- [ ] **3.11** Create super admin login view + "check your email" view + verify error view

### Build & Test

- [ ] `dotnet build` passes
- [ ] `dotnet test` passes — new tests:
  - [ ] `MagicLinkServiceTests` — generate returns non-null, stores hashed version, hash ≠ raw, verify valid succeeds, verify expired fails, verify used fails, verify bogus fails
  - [ ] `CurrentUserTests` — reads claims correctly (email, roles, permissions, tenant slug, isSuperAdmin)
  - [ ] `HasPermissionFilterTests` — allows with permission, denies without
  - [ ] Integration: `POST /super-admin/login` with valid email → 200 + magic link in logs; `GET /super-admin/verify?token=valid` → cookie set + redirect; invalid token → error

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

**QA Status**: [ ] Not started · [ ] Issues raised · [ ] Signed off ✅

**QA Notes**:
> _(Write any issues or observations here)_

---

## Phase 4 — Registration, Tenant Provisioning & Feature Flags

**Goal**: New tenants can register. Tenant databases are created and migrated. Feature flags functional. Multi-tenancy is real.

**Depends on**: Phase 3 ✅

### Steps

- [ ] **4.1** Implement `TenantProvisioner` (`ITenantProvisioner`):
  - [ ] Creates `data/tenants/{slug}.db` file
  - [ ] Applies `TenantDbContext.Database.EnsureCreated()` (migrations in Phase 8)
  - [ ] Sets PRAGMA journal_mode=WAL
  - [ ] Seeds default roles: Admin (system), Member (system)
  - [ ] Seeds all permissions from `PermissionDefinitions.GetAll()`
  - [ ] Assigns all permissions to Admin role
  - [ ] Creates admin `AppUser` with email from registration
  - [ ] Updates Tenant status to Active in CoreDbContext
- [ ] **4.2** Create `RegistrationModule` implementing `IModule`:
  - [ ] Register `TenantProvisioner`
  - [ ] Register `ReservedSlugs`
- [ ] **4.3** Create `ReservedSlugs` static class:
  - [ ] Blocklist: admin, api, app, billing, register, super-admin, login, pricing, about, contact, legal, health, etc.
  - [ ] `IsReserved(slug)` method
- [ ] **4.4** Create `RegistrationController`:
  - [ ] `GET /register` — form with plan selector (loads plans from CoreDbContext)
  - [ ] `GET /register/check-slug?slug=xxx` — HTMX inline validation (available/taken/reserved/too-short)
  - [ ] `POST /register` — validate Turnstile → validate slug → create Tenant (PendingSetup) → free plan: provision + create subscription + send welcome email → paid plan: MockBillingService auto-approves + provision
  - [ ] `GET /register/callback?reference=xxx` — Paystack callback (used in Phase 8)
  - [ ] `RegisterRequest` DTO with validation attributes
- [ ] **4.5** Create registration views:
  - [ ] `Index.cshtml` — registration form
  - [ ] `_PlanSelector.cshtml` — plan cards partial
  - [ ] `_SlugValidator.cshtml` — HTMX inline response
  - [ ] `Success.cshtml` — post-registration success page
  - [ ] Create minimal `_MarketingLayout.cshtml` (navbar + footer, fleshed out Phase 7)
- [ ] **4.6** Create `FeatureFlagsModule` implementing `IModule`:
  - [ ] Register `DatabaseFeatureDefinitionProvider`
  - [ ] Register `TenantPlanFeatureFilter`
  - [ ] Register `FeatureService` as `IFeatureService`
  - [ ] Register Microsoft.FeatureManagement services
- [ ] **4.7** Implement `DatabaseFeatureDefinitionProvider`:
  - [ ] Reads Features from CoreDbContext
  - [ ] 5-minute in-memory cache
  - [ ] Each feature registered with `TenantPlan` filter
- [ ] **4.8** Implement `TenantPlanFeatureFilter`:
  - [ ] Resolve current tenant's plan via ITenantContext
  - [ ] Check per-tenant overrides first (with expiry)
  - [ ] Fall through to plan membership (PlanFeature table)
- [ ] **4.9** Implement `FeatureService` (`IFeatureService`):
  - [ ] Wraps `IFeatureManager`
  - [ ] `AllEnabledLocally` override for dev (returns true for everything)
- [ ] **4.10** Create `FeatureDefinitions` constants class (if not done in Phase 1):
  - [ ] All feature keys as string constants
- [ ] **4.11** Register `RegistrationModule` + `FeatureFlagsModule` in module array
- [ ] **4.12** Add tenant MVC route: `{slug}/{controller=Home}/{action=Index}/{id?}`

### Build & Test

- [ ] `dotnet build` passes
- [ ] `dotnet test` passes — new tests:
  - [ ] `TenantProvisionerTests` — creates SQLite file, Identity tables exist, Admin + Member roles seeded, permissions seeded, admin user created, tenant status updated to Active
  - [ ] `ReservedSlugsTests` — true for "admin"/"api"/"register", false for "acme"
  - [ ] `RegistrationControllerTests` — CheckSlug returns correct responses, POST with valid data (free plan) creates tenant + provisions DB
  - [ ] `DatabaseFeatureDefinitionProviderTests` — returns seeded features from CoreDbContext
  - [ ] `TenantPlanFeatureFilterTests` — true when feature in plan, false when not, respects overrides, respects expiry
  - [ ] `FeatureServiceTests` — returns true for all when AllEnabledLocally=true
  - [ ] Integration: POST /register → tenant created, DB file exists, welcome email in terminal

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
8. Verify data/tenants/testcorp.db exists:
   - Open with SQLite Viewer
   - AspNetUsers table: 1 row (test@test.com)
   - AspNetRoles table: 2 rows (Admin, Member)
   - Permissions table: populated
   - RolePermissions table: Admin role has all permissions
9. Verify data/core.db:
   - Tenants table: new row, slug=testcorp, status=Active
   - Subscriptions table: new row, status=Active (free plan)
10. Terminal shows ★ MAGIC LINK welcome email for test@test.com
11. Navigate to https://localhost:5001/testcorp/login
12. Enter test@test.com → submit → check terminal for magic link
13. Paste magic link URL → logged in as tenant admin
14. Cookie `.Tenant.Auth` exists
15. Register SECOND tenant (slug "other", different email)
16. Verify data/tenants/other.db exists (separate DB)
17. Try registering slug "testcorp" again → "Already taken"
```

**QA Status**: [ ] Not started · [ ] Issues raised · [ ] Signed off ✅

**QA Notes**:
> _(Write any issues or observations here)_

---

## Phase 5 — Refactor Notes Module + Audit Module

**Goal**: Notes module upgraded to full spec — `IModule`, `TenantDbContext`, `[Authorize]`, `[HasPermission]`, `[FeatureGate]`, tenant-scoped. Real audit trail.

**Depends on**: Phase 4 ✅

### Steps

- [ ] **5.1** Refactor `NotesModule` to implement `IModule`:
  - [ ] Replace static `AddNotesModule()` extension method
  - [ ] Implement `RegisterServices`, `RegisterMiddleware`, `RegisterMvc`
- [ ] **5.2** Change `Note.Id` from `int` to `Guid`:
  - [ ] Update entity
  - [ ] Update service interface + implementation
  - [ ] Update controller (all action parameters)
  - [ ] Update views (all id references)
- [ ] **5.3** Move Note entity to `TenantDbContext`:
  - [ ] Remove from old AppDbContext references
  - [ ] Update `NotesService` to inject `TenantDbContext`
- [ ] **5.4** Add auth attributes to `NotesController`:
  - [ ] `[Authorize(Policy = "TenantUser")]` on controller
  - [ ] `[HasPermission("notes.create")]` on Create actions
  - [ ] `[HasPermission("notes.edit")]` on Edit actions
  - [ ] `[HasPermission("notes.delete")]` on Delete actions
- [ ] **5.5** Add `[FeatureGate(FeatureDefinitions.Notes)]` to `NotesController`
- [ ] **5.6** Update Notes views:
  - [ ] `has-permission` tag helper on create/edit/delete buttons
  - [ ] Tenant-scoped URLs (`/{slug}/notes/...`)
- [ ] **5.7** Create `AuditModule` implementing `IModule`:
  - [ ] Implement `AuditWriter` (`IAuditWriter`) using `Channel<AuditEntry>`
  - [ ] Background consumer writes to `AuditDbContext`
  - [ ] Replace `NullAuditWriter` registration
- [ ] **5.8** Add audit calls to `NotesService`:
  - [ ] Write entry on create (action: "Created", entity: "Note")
  - [ ] Write entry on update (action: "Updated", include old/new values)
  - [ ] Write entry on delete (action: "Deleted")
- [ ] **5.9** Update Notes route to `/{slug}/notes/...`
- [ ] **5.10** Register `NotesModule` + `AuditModule` in module array
- [ ] **5.11** Rewrite `NotesControllerTests` for new tenant-scoped setup

### Build & Test

- [ ] `dotnet build` passes
- [ ] `dotnet test` passes — new/updated tests:
  - [ ] `NotesServiceTests` — CRUD operations work against TenantDbContext, audit entries written
  - [ ] `AuditWriterTests` — entries appear in AuditDbContext after background processing
  - [ ] Integration: authenticated tenant user → CRUD notes at /{slug}/notes; unauthenticated → redirect to login
  - [ ] Integration: audit entries in data/audit.db after note operations

### QA Handover — Phase 5

```
When I hand this over, verify:

1. Log in to testcorp tenant (from Phase 4): /testcorp/login → magic link
2. Navigate to /testcorp/notes → Notes page loads
3. Create a note (title, content, color) → appears in list
4. Edit the note → changes saved
5. Pin/unpin the note → state toggles
6. Delete the note → removed
7. Check data/tenants/testcorp.db → Notes table has your data
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

**QA Status**: [ ] Not started · [ ] Issues raised · [ ] Signed off ✅

**QA Notes**:
> _(Write any issues or observations here)_

---

## Phase 6 — Super Admin & Tenant Admin Dashboards

**Goal**: Super admin manages tenants/plans/features. Tenant admins manage users/roles/billing. The management layer.

**Depends on**: Phase 5 ✅

### Steps

- [ ] **6.1** Create `SuperAdminModule` implementing `IModule`
- [ ] **6.2** Create `SuperAdminController` — `[Authorize(Policy = "SuperAdmin")]`:
  - [ ] `GET /super-admin` — dashboard (tenant count, active subscriptions, recent registrations)
  - [ ] `GET /super-admin/tenants` — tenant list with status badges + search
  - [ ] `GET /super-admin/tenants/{id}` — tenant detail (info, subscription, user count)
  - [ ] `POST /super-admin/tenants/{id}/suspend` — suspend tenant
  - [ ] `POST /super-admin/tenants/{id}/activate` — reactivate tenant
  - [ ] `GET /super-admin/plans` — plan list
  - [ ] `POST /super-admin/plans` — create/edit plan
  - [ ] `GET /super-admin/features` — feature list with plan matrix
  - [ ] `POST /super-admin/features/{id}/toggle` — toggle feature for plan
  - [ ] `POST /super-admin/features/override` — per-tenant override
- [ ] **6.3** Create super admin views:
  - [ ] Admin layout (sidebar: Dashboard, Tenants, Plans, Features)
  - [ ] Dashboard with stat cards
  - [ ] Tenant list + detail views
  - [ ] Plan management views (CRUD modals)
  - [ ] Feature matrix view (plan × feature grid with toggles)
- [ ] **6.4** Implement `FeatureCacheInvalidator`:
  - [ ] Clears `DatabaseFeatureDefinitionProvider` cache when features change
- [ ] **6.5** Create `TenantAdminModule` implementing `IModule`
- [ ] **6.6** Create `TenantAdminController` — `[Authorize(Policy = "TenantAdmin")]`:
  - [ ] `GET /{slug}/admin/users` — user list
  - [ ] `POST /{slug}/admin/users/invite` — invite user (create AppUser + send magic link)
  - [ ] `POST /{slug}/admin/users/{id}/deactivate` — deactivate user
  - [ ] `GET /{slug}/admin/roles` — role list with permissions
  - [ ] `POST /{slug}/admin/users/{id}/roles` — assign role to user
- [ ] **6.7** Create `TenantBillingController` — `[Authorize(Policy = "TenantAdmin")]`:
  - [ ] `GET /{slug}/billing` — subscription status card + invoice list
  - [ ] `POST /{slug}/billing/change-plan` — change plan (via IBillingService)
  - [ ] `POST /{slug}/billing/cancel` — cancel subscription
- [ ] **6.8** Create tenant admin views:
  - [ ] User list, invite modal
  - [ ] Role list, role-permission view
  - [ ] Billing dashboard, invoice list, change plan modal, cancel confirm
- [ ] **6.9** Update tenant `_Layout.cshtml` sidebar:
  - [ ] Dashboard, Notes, Users (admin only), Settings (admin only), Billing (admin only)
  - [ ] Use `has-permission` and `is-authenticated` tag helpers
- [ ] **6.10** Register `SuperAdminModule` + `TenantAdminModule` in module array

### Build & Test

- [ ] `dotnet build` passes
- [ ] `dotnet test` passes — new tests:
  - [ ] `SuperAdminControllerTests` — only accessible with SuperAdmin auth, correct view models
  - [ ] `TenantAdminControllerTests` — invite creates user + sends email, role assignment works
  - [ ] `TenantBillingControllerTests` — change plan calls IBillingService, cancel calls CancelSubscriptionAsync
  - [ ] Integration: super admin → list tenants, view detail, suspend/activate
  - [ ] Integration: tenant admin → list users, invite user, view billing

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
> _(Write any issues or observations here)_

---

## Phase 7 — Marketing Module

**Goal**: Public-facing website — landing page, pricing from DB, about, contact, legal pages, SEO.

**Depends on**: Phase 6 ✅

### Steps

- [ ] **7.1** Create `MarketingModule` implementing `IModule`
- [ ] **7.2** Create `_MarketingLayout.cshtml` (full version):
  - [ ] Public navbar: Logo, Pricing, About, Contact, Get Started, Sign In
  - [ ] Footer: About, Contact, Terms, Privacy, copyright
  - [ ] Open Graph meta tags from ViewData
  - [ ] Mobile responsive (DaisyUI dropdown menu)
- [ ] **7.3** Create `MarketingController`:
  - [ ] `GET /` — landing page
  - [ ] `GET /pricing` — plans from CoreDbContext
  - [ ] `GET /about` — static
  - [ ] `GET /contact` — form
  - [ ] `POST /contact` — rate-limited, Turnstile-protected, sends email
  - [ ] `GET /legal/terms` — static
  - [ ] `GET /legal/privacy` — static
  - [ ] `GET /login-redirect` — slug input → redirect to `/{slug}/login`
  - [ ] `GET /sitemap.xml` — dynamic, cached
  - [ ] `GET /robots.txt` — static, cached
- [ ] **7.4** Create landing page view:
  - [ ] Hero section, feature cards, CTA
- [ ] **7.5** Create pricing page view:
  - [ ] Plan cards from DB, monthly/annual toggle, FAQ accordion
- [ ] **7.6** Create sign-in modal:
  - [ ] Slug input → redirects to `/{slug}/login`
- [ ] **7.7** Create static pages: About, Contact, Terms, Privacy
- [ ] **7.8** Set up route priority in `Program.cs`:
  - [ ] Marketing routes (explicit) registered BEFORE tenant catch-all
- [ ] **7.9** Replace old `HomeController` / `Home/Index.cshtml` with marketing landing page
- [ ] **7.10** Register `MarketingModule` in module array

### Build & Test

- [ ] `dotnet build` passes
- [ ] `dotnet test` passes — new tests:
  - [ ] Integration: `GET /` → 200 with marketing layout; `GET /pricing` → plan cards; `GET /sitemap.xml` → valid XML; `GET /robots.txt` → correct directives
  - [ ] Integration: `POST /contact` rate limited (6th request → 429)
  - [ ] Integration: `/pricing` → `/test/notes` — route priority correct

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

**QA Status**: [ ] Not started · [ ] Issues raised · [ ] Signed off ✅

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
