# Integration Guide

Quick-start guide for running and extending the SaaS platform.

---

## Local Development (no Docker)

```bash
cd src
dotnet run
```

- Opens at `https://localhost:5001` (or per `launchSettings.json`)
- Uses `appsettings.Development.json` overrides
- All providers default to mocks: `Billing=Mock`, `Email=Console`, `Turnstile=Mock`
- `FeatureFlags:AllEnabledLocally=true` enables all features regardless of plan
- Dev seed creates a `demo` tenant if `DevSeed:Enabled=true`

---

## Local Development (Docker)

```bash
docker compose up --build
```

- `docker-compose.override.yml` is auto-merged — sets `Development` environment, mocks all providers
- No `.env` file required — `env_file` is optional, litestream sidecar is disabled via `profiles: [production]`
- App runs at `http://localhost:8080`
- Data persists in the `app-data` Docker volume

---

## Production (Docker)

```bash
# Create .env with real credentials
cat > .env <<EOF
R2_ACCESS_KEY_ID=your_key
R2_SECRET_ACCESS_KEY=your_secret
R2_ENDPOINT=https://your-account.r2.cloudflarestorage.com
R2_BUCKET=saas-backups
EOF

# Run production-only (no override file)
docker compose -f docker-compose.yml up -d
```

Ensure `appsettings.Production.json` or environment variables configure:
- `Billing:Provider=Paystack` + Paystack keys
- `Email:Provider=SES` + AWS SES credentials
- `Turnstile:Provider=Cloudflare` + site/secret keys
- `Storage:Provider=R2` + R2 bucket config

---

## Architecture Overview

### Multi-Tenant Database Strategy
- **Core DB** (`db/core.db`): Plans, Tenants, Subscriptions, Invoices, Payments, Features
- **Audit DB** (`db/audit.db`): Shared audit log with `TenantSlug` column
- **Tenant DBs** (`db/tenants/{slug}.db`): Per-tenant Identity (users, roles) + domain data (notes)
- All SQLite with WAL mode + Litestream backup to Cloudflare R2

### Module System
Each module implements `IModule` and self-registers:
- `ControllerViewPaths` — maps controller → view folder for Razor locator
- `PartialViewSearchPaths` — Swap.Htmx partial search paths
- `Features` — feature flags with min plan slug
- `Permissions` — RBAC permissions seeded per tenant
- `DefaultRoles` — roles created during tenant provisioning
- `RegisterServices()` — DI registration
- `ReservedSlugs` / `PublicRoutePrefixes` — slug validation

### Provider Pattern
Swap implementations via `appsettings.json`:

| Service | Dev Provider | Prod Provider | Config Key |
|---------|-------------|---------------|------------|
| Billing | `MockBillingService` | `PaystackBillingService` | `Billing:Provider` |
| Email | `ConsoleEmailService` | `SesEmailService` | `Email:Provider` |
| Bot Protection | `MockBotProtection` | `CloudflareTurnstile` | `Turnstile:Provider` |
| Storage | `LocalStorageService` | `R2StorageService` | `Storage:Provider` |

### Feature Flags
- Defined per module via `IModule.Features`
- Seeded to Core DB → linked to plans via `PlanFeature` junction table
- Evaluated by `TenantPlanFeatureFilter` (checks plan + per-tenant overrides)
- Gated in controllers: `[RequireFeature("feature_key")]`
- Gated in views: `@inject IFeatureService` → `await FeatureService.IsEnabledAsync("key")`
- Cache invalidated via generation-stamp pattern in `FeatureCacheInvalidator`

### Rate Limiting

| Policy | Limit | Scope | Applied To |
|--------|-------|-------|-----------|
| Global | 100/min | Per IP | All requests |
| `strict` | 5/min | Per IP | Sensitive endpoints |
| `registration` | 3/5min | Per IP | Registration |
| `contact` | 3/5min | Per IP | Contact form |
| `webhook` | 50/min | Per IP | Paystack webhooks |
| `tenant` | Plan-based/min | Per tenant slug | All tenant routes |

Plan defaults: Free=30, Starter=60, Professional=120, Enterprise=unlimited.

### Billing Flow

**Registration (paid plan)**:
1. User selects plan → `RegistrationController.Register`
2. Creates tenant in `PendingSetup` → calls `IBillingService.InitializeSubscriptionAsync`
3. Mock: provisions inline (no redirect). Paystack: redirects to checkout.
4. On callback: verifies payment, provisions tenant DB, sends welcome email.

**Plan change (pro-rated)**:
1. User clicks "Change Plan" in billing admin → `_ChangePlanModal` shows plans
2. User selects → `PreviewPlanChange` returns `_PlanChangeConfirmModal` with proration breakdown
3. User confirms → `ChangePlan` executes the change
4. Upgrade: charges prorated difference. Downgrade: credits next cycle.

---

## Key Patterns

### Swap.Htmx Controllers
```csharp
public class MyController : SwapController
{
    // Full page (with layout)
    public IActionResult Index() => SwapView(model);

    // Partial update
    public IActionResult List() => SwapView("_List", model);

    // Multi-target response
    public IActionResult Save()
    {
        return SwapResponse()
            .WithView("_ModalClose")
            .AlsoUpdate("list-target", "_List", model)
            .WithSuccessToast("Saved!")
            .Build();
    }
}
```

### Adding a New Module
1. Create folder `src/Modules/MyModule/`
2. Implement `IModule` in `MyModuleModule.cs`
3. Register in `Program.cs`: `modules.Add(new MyModuleModule())`
4. Add controller, views, services as needed
5. Define features/permissions in the module

### Health Check
- `GET /health` — returns JSON with all checks
- Optionally restricted by IP via `HealthCheck:AllowedIPs` in config (comma-separated)
- Empty = allow all (development default)

---

## Environment Variables Reference

| Variable | Default | Description |
|----------|---------|-------------|
| `ASPNETCORE_ENVIRONMENT` | `Development` | Runtime environment |
| `Site__BaseUrl` | `https://localhost:5001` | Public URL |
| `SuperAdmin__Email` | `admin@localhost` | Super admin email |
| `Billing__Provider` | `Mock` | `Mock` or `Paystack` |
| `Email__Provider` | `Console` | `Console` or `SES` |
| `Turnstile__Provider` | `Mock` | `Mock` or `Cloudflare` |
| `Storage__Provider` | `Local` | `Local` or `R2` |
| `FeatureFlags__AllEnabledLocally` | `true` | Enable all features in dev |
| `DevSeed__Enabled` | `false` | Auto-create demo tenant |
| `HealthCheck__AllowedIPs` | _(empty)_ | Comma-separated IPs |
