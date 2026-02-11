# QA Fixes Tracker

All 17 issues from the QA testing round have been resolved. This document summarises each fix.

---

## Phase 1 — Critical Bugs

### 1. Mock billing breaks paid-plan registration
**Problem**: `MockBillingService.InitializeSubscriptionAsync` returned no `PaymentUrl`. Controller required a redirect URL for all paid plans, causing registration to fail in dev.
**Fix**: Added `RequiresRedirect` flag to `SubscriptionInitResult`. Mock returns `RequiresRedirect: false` and provisions inline. Controller branches: if no redirect, provisions immediately like free plans.
**Files**: `IBillingService.cs`, `MockBillingService.cs`, `RegistrationController.cs`

### 2. Windows path separators in litestream.yml
**Problem**: `Directory.GetFiles` returns `\` paths on Windows. Litestream runs on Linux and needs `/`.
**Fix**: Added `path.Replace('\\', '/')` in `AppendDbEntry()`.
**Files**: `LitestreamConfigSyncService.cs`

### 3. Deactivated users can still log in
**Problem**: Magic-link login had no `IsActive` check. Deactivated users could verify tokens and log in.
**Fix**: `LoginPost` now returns "magic link sent" even for missing/inactive users (prevents enumeration). `Verify` rejects inactive users with explicit error. SuperAdmin auth also checks `IsActive`.
**Files**: `TenantAuthController.cs`, `SuperAdminAuthController.cs`

### 4. Max users limit not enforced on invite
**Problem**: `InviteUserAsync` never checked the plan's `MaxUsers`. Tenants could invite unlimited users.
**Fix**: Injected `CoreDbContext` into `TenantAdminService`. `InviteUserAsync` now queries the plan limit and counts active users, returning a specific error when the limit is reached.
**Files**: `ITenantAdminService.cs`, `TenantAdminService.cs`, `TenantAdminController.cs`

### 5. Blank page after role deletion error
**Problem**: `DeleteRole` error path returned `SwapResponse().WithErrorToast().Build()` with no `.WithView()`.
**Fix**: Both success and error paths now load the role list and include `.WithView("_RoleList", roles)`.
**Files**: `TenantAdminController.cs`

---

## Phase 2 — Feature Gating & UX

### 6. Sidebar shows ungated feature links
**Problem**: Notes and Audit Log links showed regardless of whether the feature was enabled for the tenant's plan.
**Fix**: Injected `IFeatureService` into `_TenantLayout.cshtml`. Notes link wrapped in `@if (notesEnabled)`. Admin button gated by `has-permission name="users.read"`.
**Files**: `_TenantLayout.cshtml`

### 7. Feature cache not invalidated immediately
**Problem**: `FeatureCacheInvalidator.Invalidate()` tried to clear specific keys but missed generation-based staleness. Toggling features in SuperAdmin could take minutes to take effect.
**Fix**: Introduced a static `_generation` counter using `Interlocked.Increment`. Cache keys now include the generation stamp. `Invalidate()` bumps the generation, orphaning all previous cache entries.
**Files**: `FeatureCacheInvalidator.cs`, `TenantPlanFeatureFilter.cs`

### 8. Features not wired up (SSO + custom_roles + audit_log)
**Problem**: SSO was listed as a feature with no implementation. `custom_roles` had no `[RequireFeature]` on controller actions. No audit log viewer existed.
**Fix**:
- **SSO**: Removed from `AuthModule.Features` (no implementation exists).
- **custom_roles**: Added `[RequireFeature("custom_roles")]` to all 8 role management actions in `TenantAdminController`.
- **audit_log**: Created `AuditLogController` with Index/List/Detail actions, registered view paths in `AuditModule`, created views with filtering and pagination.
**Files**: `AuthModule.cs`, `TenantAdminController.cs`, `AuditModule.cs`, `AuditLogController.cs`, Audit views

### 9. Contact form doesn't clear after success
**Problem**: After successful submission, the form kept the user's input.
**Fix**: Added `hx-on::after-request="if(event.detail.successful) this.reset()"` to the form element.
**Files**: `Contact.cshtml`

### 10. Tenant admin needs separate layout
**Problem**: Users, Roles, and Billing pages used the main tenant layout. No dedicated admin area.
**Fix**: Created `_TenantAdminLayout.cshtml` with its own sidebar (Users, Roles gated by `custom_roles`, Billing, Audit Log gated by `audit_log`), breadcrumbs, and "Back to App" link. Updated all TenantAdmin and Audit `_ViewStart.cshtml` files.
**Files**: `_TenantAdminLayout.cshtml` (new), `_TenantLayout.cshtml`, TenantAdmin `_ViewStart.cshtml`, Audit `_ViewStart.cshtml`

### 11. Permission toggle styling
**Problem**: Toggle switches didn't clearly show on/off state.
**Fix**: Changed from `toggle toggle-sm toggle-primary` to `checkbox checkbox-sm checkbox-success` for clearer binary visual.
**Files**: `_RoleDetail.cshtml`

---

## Phase 3 — UX Improvements

### 12. No global HTMX loading indicators
**Problem**: No visual feedback during HTMX requests. Buttons could be double-clicked.
**Fix**: Added global handlers in `layout.js`:
- Fixed-position progress bar at top of page (3px, primary color)
- `htmx:beforeRequest`: disables trigger button, adds DaisyUI `loading loading-spinner` class
- `htmx:afterSettle` / error events: re-enables button, hides progress bar
**Files**: `layout.js`

---

## Phase 4 — Security & Rate Limiting

### 13. Tenant-aware rate limiting
**Problem**: All rate limiting was IP-based only. No per-tenant/plan throttling.
**Fix**:
- Added `MaxRequestsPerMinute` to `Plan` entity (Free: 30, Starter: 60, Pro: 120, Enterprise: unlimited)
- Created `"tenant"` rate-limit policy that partitions by tenant slug, reads plan limits via `IMemoryCache`
- Moved `UseRateLimiter()` after `TenantResolutionMiddleware` so `ITenantContext` is available
- Applied `.RequireRateLimiting("tenant")` to the tenant route
- Added field to SuperAdmin plan edit UI
**Files**: `Plan.cs`, `ServiceCollectionExtensions.cs`, `ApplicationBuilderExtensions.cs`, `CoreDataSeeder.cs`, `_PlanEditModal.cshtml`, `ISuperAdminService.cs`, `SuperAdminService.cs`, `SuperAdminController.cs`

### 14. Health endpoint exposes internal details
**Problem**: `/health` returned detailed status for all health checks with no access restriction.
**Fix**: Added an endpoint filter that checks `HealthCheck:AllowedIPs` config. Empty = allow all (default for dev). Production config has the setting ready to be populated with comma-separated IPs.
**Files**: `ApplicationBuilderExtensions.cs`, `appsettings.Production.json`

---

## Phase 5 — Infrastructure

### 15. Litestream config not synced immediately after tenant creation
**Problem**: `LitestreamConfigSyncService` runs on a 5-minute interval. New tenant DBs weren't backed up until the next sync.
**Fix**: Extracted `ILitestreamConfigSync` interface from the background service. Injected it into `TenantProvisionerService`. After `MigrateAsync()`, calls `SyncConfigAsync()` immediately. Registered as singleton so both hosted service and DI resolve the same instance.
**Files**: `ILitestreamConfigSync.cs` (new), `LitestreamConfigSyncService.cs`, `BackupModule.cs`, `TenantProvisionerService.cs`

---

## Phase 6 — Billing Enhancements

### 16. Pro-rated plan changes
**Problem**: Plan changes charged the full new plan price with no proration. Old subscription was cancelled outright.
**Fix**:
- Added `PreviewPlanChangeAsync` to `IBillingService` returning `PlanChangePreview` with computed proration
- Both Mock and Paystack calculate: unused credit, prorated new cost, amount due (upgrade) or credit (downgrade)
- Paystack `ChangePlanAsync` now charges the prorated difference instead of full price
- UI flow: Select plan → Preview modal (showing breakdown) → Confirm → Execute
- Created `_PlanChangeConfirmModal.cshtml` with detailed proration breakdown
**Files**: `IBillingService.cs`, `MockBillingService.cs`, `PaystackBillingService.cs`, `TenantBillingController.cs`, `_ChangePlanModal.cshtml`, `_PlanChangeConfirmModal.cshtml` (new)

---

## Phase 7 — Docker & Documentation

### 17. Docker Compose fails without .env file
**Problem**: `docker-compose.yml` has `env_file: .env` which fails if the file doesn't exist. Litestream env vars also fail without `.env`.
**Fix**: Changed `env_file` to use `path: .env` + `required: false`. Added empty defaults for R2 env vars: `${R2_ACCESS_KEY_ID:-}`.
**Files**: `docker-compose.yml`

### 18. Replace QA doc with focused docs
**Problem**: `QA-TESTING.md` was a testing script, not a fixes tracker.
**Fix**: Deleted `QA-TESTING.md`. Created this `FIXES.md` and `INTEGRATION-GUIDE.md`.
**Files**: `docs/FIXES.md` (new), `docs/INTEGRATION-GUIDE.md` (new)
