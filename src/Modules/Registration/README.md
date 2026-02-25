# Registration Module

Tenant signup flow — email verification, slug validation, bot protection, and dual-path provisioning (free plans provision instantly, paid plans redirect through Paystack payment first).

## Structure

```
Registration/
├── RegistrationModule.cs
├── Entities/
│   └── PendingRegistration.cs           # Core DB: holds unverified/verified signups
├── Models/
│   └── RegisterRequest.cs               # Form model: Slug, Email, PlanId, BillingCycle, TurnstileToken
├── Services/
│   ├── RegistrationEmailService.cs      # Sends verification + welcome emails
│   └── (ITenantProvisioner registered here — implemented by TenantProvisionerService in Infrastructure)
├── Controllers/
│   └── RegistrationController.cs        # Full signup flow
└── Views/
    └── Registration/
        ├── Index.cshtml                 # Signup form
        ├── VerifyResult.cshtml          # Email verification landing page
        ├── Callback.cshtml              # Paystack post-payment callback
        ├── _SlugValidation.cshtml       # HTMX partial — real-time slug check
        ├── _VerifyEmailSent.cshtml      # HTMX partial — "check your email"
        ├── _RegistrationSuccess.cshtml  # HTMX partial — success message
        └── _RegistrationError.cshtml    # HTMX partial — error message
```

## Signup Flow

```
User visits /register?plan={planId}
    │
    ▼
Fills form: workspace slug, email, plan, billing cycle
    │
    ├─ Real-time slug validation via HTMX → GET /register/check-slug?slug=xxx
    │     └─ Checks: format (lowercase, alpha-numeric, 3-50 chars),
    │        reserved slugs (from all modules), uniqueness in DB
    │
    ▼
POST /register (rate limited: "registration" policy, bot protected)
    │
    ├─ Creates PendingRegistration in Core DB
    │     └─ VerificationToken, ExpiresAt (24h)
    │
    └─ Sends verification email with token link
         └─ GET /register/verify?token=xxx

    ▼
User clicks verification link
    │
    ├─ Token valid and not expired?
    │     │
    │     ├─ FREE PLAN:
    │     │     ├─ ITenantProvisioner.ProvisionAsync() — creates tenant, DB, admin user
    │     │     └─ Redirect to /{slug}/auth/login
    │     │
    │     └─ PAID PLAN:
    │           ├─ IBillingService.InitializeSubscriptionAsync() — gets Paystack checkout URL
    │           └─ Redirect to Paystack → payment → callback
    │
    └─ Token invalid/expired → error page

    ▼  (paid plan only)
GET /register/callback?reference=xxx
    │
    ├─ Verify Paystack payment
    ├─ ITenantProvisioner.ProvisionAsync()
    └─ Redirect to /{slug}/auth/login
```

## Entity

**`PendingRegistration`** (Core DB):

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `Guid` | Primary key |
| `Slug` | `string` | Requested workspace slug |
| `Email` | `string` | Admin email |
| `PlanId` | `Guid` | Selected plan |
| `BillingCycle` | `string` | `"Monthly"` or `"Annual"` |
| `VerificationToken` | `string` | Email verification token |
| `ExpiresAt` | `DateTime` | Token expiry (24h from creation) |
| `IsVerified` | `bool` | Set to true after email verification |
| `CreatedAt` | `DateTime` | When submitted |

Pending registrations are cleaned up by `PendingTenantCleanupService` (from the Tenancy module) after 24 hours if unverified.

## Routes

| Method | URL | Action | Auth | Rate Limited |
|--------|-----|--------|------|-------------|
| GET | `/register` | `Index` | Public | No |
| GET | `/register/check-slug` | `CheckSlug` | Public | No |
| POST | `/register` | `Register` | Public | `registration` policy |
| GET | `/register/verify` | `Verify` | Public | No |
| GET | `/register/callback` | `Callback` | Public | No |

## Tenant Provisioning (What Happens During `ProvisionAsync`)

The `TenantProvisionerService` (in `Infrastructure/Provisioning/`) performs:

1. Validates slug (format, reserved, uniqueness)
2. Creates `Tenant` + `Subscription` in Core DB
3. Creates tenant SQLite database file at `db/tenants/{slug}.db`
4. Applies all tenant migrations
5. Syncs Litestream configuration (if enabled)
6. Seeds Identity roles and permissions (from all module declarations)
7. Calls `SeedTenantAsync()` on each module
8. Creates the admin user account
9. Publishes `TenantCreatedEvent` (triggers welcome email via MassTransit)

## Services

| Service | Interface | Purpose |
|---------|-----------|---------|
| `RegistrationEmailService` | `IRegistrationEmailService` | `SendVerificationEmailAsync()`, `SendWelcomeEmailAsync()` |
| `TenantProvisionerService` | `ITenantProvisioner` | Full tenant provisioning orchestrator |

## Configuration

Uses these config sections:

| Section | Keys Used |
|---------|-----------|
| `Site` | `BaseUrl` — for verification email link |
| `Billing` | `Provider` — determines free vs paid flow |
| `Turnstile` | `Provider`, `SiteKey`, `SecretKey` — bot protection on form |
| `RateLimiting` | `RegistrationPerWindow`, `RegistrationWindowMinutes` |

## Reserved Slugs

This module reserves: `register`, `login`, `logout`

These are combined with slugs from all other modules (e.g. `super-admin`, `www`, `app`) and validated during slug checking.
