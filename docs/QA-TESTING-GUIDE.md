# QA Testing Guide

Complete checklist for manually validating every integration, provider option, and user flow — first locally (`dotnet run`), then via Docker (`docker compose up`).

Work through each section in order. Mark each checkbox as you go. If a test fails, note the section number and exact symptoms in the **Issue Log** at the bottom.

---

## Table of Contents

- [Prerequisites](#prerequisites)
- [Phase 1 — Local (`dotnet run`) with Mock/Default Providers](#phase-1--local-dotnet-run-with-mockdefault-providers)
- [Phase 2 — Local (`dotnet run`) with Real Providers](#phase-2--local-dotnet-run-with-real-providers)
- [Phase 3 — Docker (`docker compose up`) with Default Providers](#phase-3--docker-docker-compose-up-with-default-providers)
- [Phase 4 — Docker with Real Providers](#phase-4--docker-with-real-providers)
- [Phase 5 — Docker Production Profile (Litestream)](#phase-5--docker-production-profile-litestream)
- [Issue Log Template](#issue-log-template)

---

## Prerequisites

### Tools
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (preview)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [ngrok](https://ngrok.com/download) (for Paystack webhook testing)
- A modern browser with dev tools (F12)

### Accounts (for real provider testing)
- Paystack test account → [dashboard.paystack.com](https://dashboard.paystack.com)
- Gmail with App Password enabled → [myaccount.google.com/apppasswords](https://myaccount.google.com/apppasswords)
- MailerSend account with verified domain → [mailersend.com](https://www.mailersend.com)
- Cloudflare account → Turnstile + R2 → [dash.cloudflare.com](https://dash.cloudflare.com)

### Clean State

```powershell
# Delete all databases to start fresh (from project root)
Remove-Item src/db/core.db, src/db/audit.db -ErrorAction SilentlyContinue
Remove-Item src/db/tenants/* -ErrorAction SilentlyContinue
Remove-Item src/db/hangfire.db -ErrorAction SilentlyContinue
```

---

## Phase 1 — Local (`dotnet run`) with Mock/Default Providers

**Objective**: Verify the app starts, seeds, and all core flows work with built-in mocks.

**Config**: Default `appsettings.json` + `appsettings.Development.json` (no env var overrides).

**Expected providers**: Billing=Mock, Email=Console, Turnstile=Mock, Storage=Local, Messaging=InMemory, Caching=Memory, Hangfire=InMemory, Litestream=Disabled.

```powershell
cd src
dotnet run
```

### 1.1 Startup & Health

| # | Test | Expected | Pass |
|---|------|----------|------|
| 1.1.1 | App starts without errors | No exceptions in console. Listening on `https://localhost:5001` | ☐ |
| 1.1.2 | Dev seed runs | Console logs: "Provisioned demo tenant" and "Created member user" | ☐ |
| 1.1.3 | `GET /health` | HTTP 200. JSON body shows `core-database: Healthy`, `tenant-directory: Healthy`, `litestream-readiness: Healthy` or `Degraded` (Litestream disabled is acceptable) | ☐ |
| 1.1.4 | `GET /health` JSON structure | Each check has `status`, `description` fields | ☐ |

### 1.2 Marketing Pages (Public)

| # | Test | Expected | Pass |
|---|------|----------|------|
| 1.2.1 | `GET /` | Home page renders, no errors | ☐ |
| 1.2.2 | `GET /pricing` | Pricing page shows plans (Free, Starter, Professional, Enterprise) with prices | ☐ |
| 1.2.3 | `GET /about` | About page renders | ☐ |
| 1.2.4 | `GET /contact` | Contact form renders, **no** Turnstile widget (Mock provider) | ☐ |
| 1.2.5 | `POST /contact` — submit form | Logs show "Contact form submitted" in console. No real email sent (Console provider). Toast/success message appears. | ☐ |
| 1.2.6 | `GET /legal/terms` | Terms page renders | ☐ |
| 1.2.7 | `GET /legal/privacy` | Privacy page renders | ☐ |
| 1.2.8 | `GET /sitemap.xml` | Valid XML sitemap returned | ☐ |
| 1.2.9 | `GET /robots.txt` | Valid robots.txt returned | ☐ |

### 1.3 Registration (Mock Billing)

| # | Test | Expected | Pass |
|---|------|----------|------|
| 1.3.1 | `GET /register` | Registration form renders. Plan selector visible. No Turnstile widget. | ☐ |
| 1.3.2 | Select **Free** plan, fill form, submit | Tenant provisioned immediately (no redirect to payment). Redirected to `/{slug}/login`. Console logs welcome email. | ☐ |
| 1.3.3 | Select **Starter** (paid) plan, fill form, submit | Mock billing: provisions immediately without Paystack redirect. Redirected to `/{slug}/login`. | ☐ |
| 1.3.4 | Duplicate slug | Register with slug "demo" → validation error "slug already taken" | ☐ |
| 1.3.5 | Slug validation (`/register/check-slug`) | Type in slug field → HTMX calls `check-slug` → shows available/taken indicator | ☐ |
| 1.3.6 | Reserved slug | Try slug "super-admin", "login", "register" → rejected | ☐ |

### 1.4 Tenant Auth — Magic Link (Console Email)

| # | Test | Expected | Pass |
|---|------|----------|------|
| 1.4.1 | `GET /demo/login` | Login form renders. No Turnstile widget. | ☐ |
| 1.4.2 | Enter `admin@demo.local`, submit | Console output shows magic link URL. Success message on page. | ☐ |
| 1.4.3 | Copy magic link URL, open in browser | Logged in as admin. Redirected to `/demo/Dashboard`. | ☐ |
| 1.4.4 | Enter `member@demo.local`, submit | Console magic link. Open → logged in as member. Less admin options visible. | ☐ |
| 1.4.5 | Enter non-existent email | **No error exposed** — same "check your email" message (prevents enumeration). No console output for magic link. | ☐ |
| 1.4.6 | Use expired/invalid magic link | Error page or "invalid link" message | ☐ |
| 1.4.7 | Logout (`POST /{slug}/logout`) | Redirected to login page. Accessing `/demo/Dashboard` redirects to login. | ☐ |

### 1.5 Dashboard & Notes (Authenticated)

> Log in as `admin@demo.local` via magic link first.

| # | Test | Expected | Pass |
|---|------|----------|------|
| 1.5.1 | `GET /demo` or `/demo/Dashboard` | Dashboard page renders with welcome content | ☐ |
| 1.5.2 | Navigate to Notes | Notes list shows seeded sample notes (from DevSeed) | ☐ |
| 1.5.3 | Create a new note | Fill title/content → submit → note appears in list. HTMX partial update (no full page reload). | ☐ |
| 1.5.4 | Edit a note | Change title → save → updated in list | ☐ |
| 1.5.5 | Toggle pin on a note | Pin icon toggles, note moves to pinned section | ☐ |
| 1.5.6 | Delete a note | Confirm dialog → note removed from list | ☐ |
| 1.5.7 | Notes as **member** | Log in as `member@demo.local`. Verify notes access matches member permissions. | ☐ |

### 1.6 Profile & Sessions

> Log in as `admin@demo.local`.

| # | Test | Expected | Pass |
|---|------|----------|------|
| 1.6.1 | `GET /demo/profile` | Profile page shows user info | ☐ |
| 1.6.2 | Update display name | Change name → save → updated | ☐ |
| 1.6.3 | `GET /demo/profile/sessions` | Shows current session with browser/device info | ☐ |
| 1.6.4 | Revoke a session | If multiple sessions, revoke one → session removed | ☐ |
| 1.6.5 | 2FA setup (`/demo/profile/two-factor`) | Shows QR code, accepts TOTP code from authenticator app | ☐ |

### 1.7 Tenant Admin

> Log in as `admin@demo.local`.

| # | Test | Expected | Pass |
|---|------|----------|------|
| 1.7.1 | `GET /demo/TenantAdmin/Users` | User list shows admin + member | ☐ |
| 1.7.2 | Invite a new user | Enter email → sends invitation (console email). Invitation appears in list. | ☐ |
| 1.7.3 | Deactivate a user | Deactivate member → status changes. Member cannot log in. | ☐ |
| 1.7.4 | Activate a user | Reactivate member → can log in again | ☐ |
| 1.7.5 | Roles page | Shows default roles (Admin, Member). Custom role creation if plan supports `custom_roles`. | ☐ |

### 1.8 Tenant Billing (Mock)

| # | Test | Expected | Pass |
|---|------|----------|------|
| 1.8.1 | `GET /demo/TenantBilling` | Shows current plan (Starter), subscription status | ☐ |
| 1.8.2 | Click "Change Plan" | Modal shows available plans with pricing | ☐ |
| 1.8.3 | Preview plan change | Shows proration breakdown (mock values) | ☐ |
| 1.8.4 | Confirm plan change | Plan changes immediately (mock, no payment) | ☐ |
| 1.8.5 | Cancel subscription | Confirm → status changes to Cancelled | ☐ |

### 1.9 Tenant Settings

| # | Test | Expected | Pass |
|---|------|----------|------|
| 1.9.1 | `GET /demo/TenantSettings` | Settings page renders | ☐ |
| 1.9.2 | Update general settings | Change name → save → updated | ☐ |
| 1.9.3 | Export data | Click export → downloads data file | ☐ |
| 1.9.4 | Request deletion | Click request deletion → confirmation → tenant marked for deletion | ☐ |
| 1.9.5 | Cancel deletion | Cancel deletion → tenant back to normal | ☐ |

### 1.10 Notifications

| # | Test | Expected | Pass |
|---|------|----------|------|
| 1.10.1 | Notification dropdown | Bell icon shows notification count (if any exist) | ☐ |
| 1.10.2 | View notifications | Click bell → dropdown shows notifications | ☐ |
| 1.10.3 | Mark as read | Click notification → marked read, count decreases | ☐ |
| 1.10.4 | Mark all read | Click "mark all" → all cleared | ☐ |

### 1.11 Audit Log

| # | Test | Expected | Pass |
|---|------|----------|------|
| 1.11.1 | `GET /demo/AuditLog` | Audit log page renders (if plan supports `audit_log`) | ☐ |
| 1.11.2 | Entries from actions | Creating/editing notes, logging in, etc. should appear as audit entries | ☐ |
| 1.11.3 | Detail view | Click an entry → shows detail with before/after values | ☐ |

### 1.12 Super Admin — Auth

| # | Test | Expected | Pass |
|---|------|----------|------|
| 1.12.1 | `GET /super-admin/login` | Super admin login page renders | ☐ |
| 1.12.2 | Enter `admin@localhost`, submit | Console shows magic link. No Turnstile widget. | ☐ |
| 1.12.3 | Open magic link | Logged in as super admin. Redirected to `/super-admin`. | ☐ |
| 1.12.4 | Enter wrong email | Same "check your email" message (no enumeration) | ☐ |

### 1.13 Super Admin — Dashboard

> Log in as super admin.

| # | Test | Expected | Pass |
|---|------|----------|------|
| 1.13.1 | `GET /super-admin` | Dashboard with stats (tenant count, user count, etc.) | ☐ |
| 1.13.2 | Tenants list (`/super-admin/tenants`) | Shows demo tenant + any registered tenants | ☐ |
| 1.13.3 | Tenant detail | Click a tenant → shows slug, plan, status, users | ☐ |
| 1.13.4 | Suspend tenant | Suspend demo → status changes. Demo tenant pages return suspended error. | ☐ |
| 1.13.5 | Activate tenant | Reactivate demo → tenant works again | ☐ |
| 1.13.6 | Plans management | `GET /super-admin/plans` → list of plans with edit | ☐ |
| 1.13.7 | Feature flags | `GET /super-admin/features` → list of features with toggle/override | ☐ |
| 1.13.8 | Toggle a feature | Toggle "notes" off for a tenant → tenant loses notes access | ☐ |
| 1.13.9 | Litestream status | `GET /super-admin/backups` → shows Litestream status (disabled/enabled) | ☐ |

### 1.14 Hangfire Dashboard

| # | Test | Expected | Pass |
|---|------|----------|------|
| 1.14.1 | `GET /super-admin/hangfire` (as super admin) | Hangfire dashboard renders | ☐ |
| 1.14.2 | Recurring jobs visible | 4 jobs listed: billing-reconciliation, stale-session-cleanup, expired-trial-check, tenant-deletion-purge | ☐ |
| 1.14.3 | Without super admin auth | Redirect to super admin login | ☐ |

### 1.15 Error & Edge Cases

| # | Test | Expected | Pass |
|---|------|----------|------|
| 1.15.1 | `GET /nonexistent-slug/Dashboard` | 404 or tenant-not-found page | ☐ |
| 1.15.2 | `GET /demo/Notes` without auth | Redirected to `/demo/login` (browser) or 200 + HX-Redirect header (HTMX) | ☐ |
| 1.15.3 | Access `/demo/TenantAdmin/Users` as member | 403 or redirect (insufficient permissions) | ☐ |
| 1.15.4 | Rapid-fire requests | Hit contact form > 3 times in 5 min → HTTP 429 returned | ☐ |
| 1.15.5 | `GET /super-admin` without auth | Redirected to `/super-admin/login` | ☐ |

---

## Phase 2 — Local (`dotnet run`) with Real Providers

Test each provider **one at a time** using environment variables. Keep all other providers at their defaults.

### 2.1 Email — SMTP (Gmail)

```powershell
$env:Email__Provider = "Smtp"
$env:Email__FromAddress = "your-email@gmail.com"
$env:Email__FromName = "QA Test"
$env:Email__Smtp__Host = "smtp.gmail.com"
$env:Email__Smtp__Port = "587"
$env:Email__Smtp__Username = "your-email@gmail.com"
$env:Email__Smtp__Password = "your-app-password"
$env:Email__Smtp__UseSsl = "true"
dotnet run
```

| # | Test | Expected | Pass |
|---|------|----------|------|
| 2.1.1 | App starts | No SMTP errors in console | ☐ |
| 2.1.2 | Magic link login | Go to `/demo/login`, enter a **real** email you own, submit → actual email arrives in inbox | ☐ |
| 2.1.3 | Email content | Email body uses the `MagicLink.html` template, has proper branding, link works | ☐ |
| 2.1.4 | Click magic link from email | Logs you into the app correctly | ☐ |
| 2.1.5 | Register tenant (Free plan) | Welcome email sent to the admin email → arrives in inbox using `Welcome.html` template | ☐ |
| 2.1.6 | Contact form submission | Email sent to `Site:SupportEmail` address | ☐ |
| 2.1.7 | Invite user | Enter real email in invite form → `TeamInvitation.html` template email arrives | ☐ |
| 2.1.8 | Invalid SMTP creds | Set wrong password → app starts but email send fails with `AuthenticationException` in logs | ☐ |

**Cleanup**: `Remove-Item env:Email__*`

### 2.2 Email — MailerSend

```powershell
$env:Email__Provider = "MailerSend"
$env:Email__FromAddress = "noreply@yourdomain.com"
$env:Email__FromName = "QA Test"
$env:Email__MailerSend__ApiToken = "mlsn.xxxxx"
dotnet run
```

| # | Test | Expected | Pass |
|---|------|----------|------|
| 2.2.1 | App starts | No errors | ☐ |
| 2.2.2 | Magic link login | Real email arrives via MailerSend | ☐ |
| 2.2.3 | Email content | `MagicLink.html` template rendered correctly | ☐ |
| 2.2.4 | Welcome email on registration | Received via MailerSend | ☐ |
| 2.2.5 | Invalid API token | Set wrong token → email send returns 401 in logs | ☐ |

**Cleanup**: `Remove-Item env:Email__*`

### 2.3 Turnstile — Cloudflare (Real Keys)

```powershell
$env:Turnstile__Provider = "Cloudflare"
$env:Turnstile__SiteKey = "0x4AAAA..."
$env:Turnstile__SecretKey = "0x4AAAA..."
dotnet run
```

> Ensure `localhost` is added as an allowed domain in your Cloudflare Turnstile site config.

| # | Test | Expected | Pass |
|---|------|----------|------|
| 2.3.1 | Registration page | Turnstile widget renders (checkbox/interactive challenge) | ☐ |
| 2.3.2 | Contact page | Turnstile widget renders | ☐ |
| 2.3.3 | Tenant login | Turnstile widget renders below email field | ☐ |
| 2.3.4 | Submit registration with completed challenge | Passes server-side validation, tenant created | ☐ |
| 2.3.5 | Submit contact form with completed challenge | Passes, email sent (or logged to console) | ☐ |
| 2.3.6 | Submit login with completed challenge | Magic link sent | ☐ |
| 2.3.7 | Tamper with token | Open DevTools, change `cf-turnstile-response` value → submit → server rejects with validation error | ☐ |
| 2.3.8 | Skip challenge | Remove hidden input entirely → submit → server rejects | ☐ |

**Cleanup**: `Remove-Item env:Turnstile__*`

### 2.4 Turnstile — Cloudflare Test Keys

```powershell
$env:Turnstile__Provider = "Cloudflare"
$env:Turnstile__SiteKey = "1x00000000000000000000AA"
$env:Turnstile__SecretKey = "1x0000000000000000000000000000000AA"
dotnet run
```

| # | Test | Expected | Pass |
|---|------|----------|------|
| 2.4.1 | Always-pass test key | Widget renders and auto-passes. Form submits successfully. | ☐ |

```powershell
$env:Turnstile__SiteKey = "2x00000000000000000000AB"
$env:Turnstile__SecretKey = "2x0000000000000000000000000000000AB"
dotnet run
```

| # | Test | Expected | Pass |
|---|------|----------|------|
| 2.4.2 | Always-fail test key | Widget renders, challenge completes, but server rejects every submission | ☐ |

**Cleanup**: `Remove-Item env:Turnstile__*`

### 2.5 Storage — Cloudflare R2

```powershell
$env:Storage__Provider = "R2"
$env:Storage__R2Bucket = "your-bucket"
$env:Storage__R2Endpoint = "https://xxx.r2.cloudflarestorage.com"
$env:Storage__R2AccessKey = "your-key"
$env:Storage__R2SecretKey = "your-secret"
$env:Storage__R2PublicUrl = "https://cdn.yourdomain.com"   # optional
dotnet run
```

| # | Test | Expected | Pass |
|---|------|----------|------|
| 2.5.1 | App starts | No R2 errors on startup. `IStorageService` resolves to `R2StorageService`. | ☐ |
| 2.5.2 | Upload flow (if UI exists) | File uploads go to R2. Check bucket in Cloudflare dashboard. | ☐ |
| 2.5.3 | Invalid R2 creds | Set wrong key → startup or first upload throws `AccessDenied` error | ☐ |

**Cleanup**: `Remove-Item env:Storage__*`

### 2.6 Billing — Paystack

> Requires ngrok for webhook testing.

**Terminal 1** — ngrok:
```powershell
ngrok http 5001
# Copy the https URL (e.g. https://abc123.ngrok-free.app)
```

**Terminal 2** — app:
```powershell
$env:Billing__Provider = "Paystack"
$env:Billing__Paystack__SecretKey = "sk_test_xxxxx"
$env:Billing__Paystack__PublicKey = "pk_test_xxxxx"
$env:Billing__Paystack__WebhookSecret = "your_webhook_secret"
$env:Billing__Paystack__CallbackBaseUrl = "https://abc123.ngrok-free.app"
dotnet run
```

> Also set the webhook URL in Paystack Dashboard → Settings → API Keys & Webhooks → `https://abc123.ngrok-free.app/api/webhooks/paystack`

| # | Test | Expected | Pass |
|---|------|----------|------|
| **Plan Sync** ||||
| 2.6.1 | App starts | Logs show `[Paystack]` plan sync. Plans created on Paystack. | ☐ |
| 2.6.2 | Check Paystack Dashboard → Plans | "Starter (Monthly)", "Professional (Monthly)", "Enterprise (Monthly)" exist | ☐ |
| **Registration with Payment** ||||
| 2.6.3 | Register with Starter plan | Redirected to Paystack checkout page | ☐ |
| 2.6.4 | Pay with test card `4084 0840 8408 4081` (CVV: `408`, OTP: `123456`) | Payment succeeds. Redirected to `/register/callback?reference=...` | ☐ |
| 2.6.5 | After callback | Tenant provisioned. Redirected to `/{slug}/login`. Welcome email logged/sent. | ☐ |
| 2.6.6 | Pay with declining card `4084 0840 8408 4082` | Payment fails. User sees error. Tenant remains in `PendingSetup`. | ☐ |
| **Plan Change** ||||
| 2.6.7 | Log in as tenant admin → Billing → Change Plan | Modal shows plans | ☐ |
| 2.6.8 | Preview upgrade (Starter→Professional) | Shows prorated charge amount | ☐ |
| 2.6.9 | Confirm upgrade | Redirected to Paystack for prorated payment. After payment, plan updates. | ☐ |
| 2.6.10 | Preview downgrade (Professional→Starter) | Shows credit for next cycle | ☐ |
| 2.6.11 | Confirm downgrade | Plan changes. Credit applied. | ☐ |
| **Cancellation** ||||
| 2.6.12 | Cancel subscription | Confirm → subscription cancelled on Paystack side too | ☐ |
| **Webhooks** ||||
| 2.6.13 | Check ngrok inspect (`http://127.0.0.1:4040`) | Paystack webhook POSTs visible (charge.success, subscription.create, etc.) | ☐ |
| 2.6.14 | App logs | `[Paystack Webhook]` entries after each event | ☐ |
| 2.6.15 | Invalid webhook signature | Send a POST to `/api/webhooks/paystack` with wrong signature → rejected | ☐ |

**Cleanup**: `Remove-Item env:Billing__*`

### 2.7 Feature Flags — Plan Gating

| # | Test | Expected | Pass |
|---|------|----------|------|
| 2.7.1 | Free plan tenant | Register a Free plan tenant. Navigate to Notes → access denied or hidden (if `notes` requires Starter+) | ☐ |
| 2.7.2 | Starter plan tenant | Demo tenant (Starter) → Notes accessible | ☐ |
| 2.7.3 | Super Admin override | Toggle "notes" feature ON for free tenant → now accessible | ☐ |
| 2.7.4 | Super Admin override OFF | Override to disabled for Starter tenant → notes blocked | ☐ |
| 2.7.5 | `custom_roles` feature | If only on Professional+ plan, Starter tenant shouldn't see Roles page | ☐ |
| 2.7.6 | `audit_log` feature | Check if Audit Log link appears based on plan | ☐ |

---

## Phase 3 — Docker (`docker compose up`) with Default Providers

**Objective**: Verify the Docker setup works with the default infrastructure (Redis, RabbitMQ, SQLite Hangfire).

```powershell
# From project root (c:\swap\saas)
# Clean state
docker compose down -v

# Start (no .env file needed for defaults)
docker compose up --build
```

**Expected providers**: Billing=Mock, Email=Console, Turnstile=Mock, Storage=Local, **Messaging=RabbitMQ**, **Caching=Redis**, **Hangfire=SQLite**, Litestream config generated but sidecar not started.

### 3.1 Container Health

| # | Test | Expected | Pass |
|---|------|----------|------|
| 3.1.1 | All 3 containers start | `docker compose ps` shows `saas-app`, `saas-redis`, `saas-rabbitmq` all running | ☐ |
| 3.1.2 | Redis healthy | `docker compose exec redis redis-cli ping` → `PONG` | ☐ |
| 3.1.3 | RabbitMQ healthy | `http://localhost:15672` → management UI (guest/guest) | ☐ |
| 3.1.4 | App healthy | `curl http://localhost:8080/health` → HTTP 200 | ☐ |
| 3.1.5 | App logs clean | `docker compose logs app` → no startup errors. Dev seed runs. | ☐ |

### 3.2 RabbitMQ Messaging Verification

| # | Test | Expected | Pass |
|---|------|----------|------|
| 3.2.1 | RabbitMQ management UI | `http://localhost:15672` → login with guest/guest → Queues tab shows MassTransit queues | ☐ |
| 3.2.2 | Register a new tenant | After registration, check RabbitMQ → `TenantCreatedEvent` message published | ☐ |
| 3.2.3 | Consumer processed | App logs show `TenantCreatedConsumer` handling the event (welcome email logged to console) | ☐ |
| 3.2.4 | Login event | Log into a tenant → `UserLoggedInEvent` published via RabbitMQ | ☐ |
| 3.2.5 | Queue names | Queues named with MassTransit conventions (`saas-tenant-created-event`, etc.) | ☐ |

### 3.3 Redis Caching Verification

| # | Test | Expected | Pass |
|---|------|----------|------|
| 3.3.1 | Redis connected | `docker compose exec redis redis-cli info clients` → shows connected clients > 0 | ☐ |
| 3.3.2 | Tenant resolution cached | Visit `/demo/Dashboard` twice → second request faster (tenant resolved from Redis) | ☐ |
| 3.3.3 | Cache keys visible | `docker compose exec redis redis-cli keys "saas:*"` → shows cached entries | ☐ |
| 3.3.4 | Redis data persists | `docker compose restart app` → tenant still resolves (Redis data survives app restart) | ☐ |

### 3.4 Hangfire SQLite Verification

| # | Test | Expected | Pass |
|---|------|----------|------|
| 3.4.1 | Hangfire DB created | `docker compose exec app ls -la /app/db/hangfire.db` → file exists | ☐ |
| 3.4.2 | Super admin Hangfire dashboard | Log in as super admin → `http://localhost:8080/super-admin/hangfire` → dashboard renders | ☐ |
| 3.4.3 | Recurring jobs | 4 jobs listed and next execution times shown | ☐ |
| 3.4.4 | Jobs persist across restart | `docker compose restart app` → Hangfire dashboard still shows jobs | ☐ |

### 3.5 All Core Flows (Docker)

Re-run these core flows inside Docker to verify they work with RabbitMQ/Redis/SQLite-Hangfire:

| # | Test | Expected | Pass |
|---|------|----------|------|
| 3.5.1 | Dev seed — demo tenant exists | `/demo/login` page loads | ☐ |
| 3.5.2 | Magic link login | Enter `admin@demo.local` → console shows magic link → copy from `docker compose logs app` → open → logged in | ☐ |
| 3.5.3 | Notes CRUD | Create, edit, pin, delete notes — all work | ☐ |
| 3.5.4 | Register new tenant (Mock billing) | Provisions successfully, messages flow through RabbitMQ | ☐ |
| 3.5.5 | Super admin panel | All pages render correctly | ☐ |
| 3.5.6 | Contact form | Submission succeeds | ☐ |
| 3.5.7 | Rate limiting | Hit contact form rapidly → 429 after 3 requests in 5 min | ☐ |

### 3.6 Data Persistence

| # | Test | Expected | Pass |
|---|------|----------|------|
| 3.6.1 | Restart app container | `docker compose restart app` → all data (tenants, notes, users) still present | ☐ |
| 3.6.2 | Stop and start all | `docker compose down && docker compose up -d` → data persists (volumes not removed) | ☐ |
| 3.6.3 | Volume wipe | `docker compose down -v && docker compose up -d` → clean start, dev seed re-creates data | ☐ |

---

## Phase 4 — Docker with Real Providers

Set real credentials via `.env` or inline env vars in docker-compose overrides.

### 4.1 Docker + Paystack

Create/update `.env`:
```dotenv
Billing__Provider=Paystack
Billing__Paystack__SecretKey=sk_test_xxxxx
Billing__Paystack__PublicKey=pk_test_xxxxx
Billing__Paystack__WebhookSecret=your_webhook_secret
Billing__Paystack__CallbackBaseUrl=https://abc123.ngrok-free.app
```

Start ngrok pointing to Docker port: `ngrok http 8080`

```powershell
docker compose down -v
docker compose up --build
```

| # | Test | Expected | Pass |
|---|------|----------|------|
| 4.1.1 | Plan sync on startup | App logs show Paystack plan sync success | ☐ |
| 4.1.2 | Register paid plan | Redirect to Paystack → pay → callback → provisioned | ☐ |
| 4.1.3 | Webhooks arrive | Check ngrok inspect + app logs for webhook events | ☐ |
| 4.1.4 | Plan change flow | Upgrade/downgrade works via Paystack | ☐ |
| 4.1.5 | Cancel subscription | Works, reflected on Paystack side | ☐ |

### 4.2 Docker + SMTP Email

```dotenv
Email__Provider=Smtp
Email__FromAddress=your-email@gmail.com
Email__FromName=QA Test Docker
Email__Smtp__Host=smtp.gmail.com
Email__Smtp__Port=587
Email__Smtp__Username=your-email@gmail.com
Email__Smtp__Password=your-app-password
Email__Smtp__UseSsl=true
```

| # | Test | Expected | Pass |
|---|------|----------|------|
| 4.2.1 | App starts | No SMTP errors | ☐ |
| 4.2.2 | Magic link email | Real email arrives in inbox | ☐ |
| 4.2.3 | Template rendering | Email uses `MagicLink.html` template properly | ☐ |

### 4.3 Docker + MailerSend Email

```dotenv
Email__Provider=MailerSend
Email__FromAddress=noreply@yourdomain.com
Email__FromName=QA Test Docker
Email__MailerSend__ApiToken=mlsn.xxxxx
```

| # | Test | Expected | Pass |
|---|------|----------|------|
| 4.3.1 | App starts | No errors | ☐ |
| 4.3.2 | Magic link email | Real email arrives via MailerSend | ☐ |
| 4.3.3 | Welcome email on registration | Arrives via MailerSend | ☐ |

### 4.4 Docker + Turnstile

```dotenv
Turnstile__Provider=Cloudflare
Turnstile__SiteKey=0x4AAAA...
Turnstile__SecretKey=0x4AAAA...
```

> Add your Docker host (e.g. `localhost`) to Turnstile allowed domains.

| # | Test | Expected | Pass |
|---|------|----------|------|
| 4.4.1 | Widget renders on registration | Turnstile challenge visible at `http://localhost:8080/register` | ☐ |
| 4.4.2 | Widget renders on contact form | Visible at `http://localhost:8080/contact` | ☐ |
| 4.4.3 | Widget renders on tenant login | Visible at `http://localhost:8080/demo/login` | ☐ |
| 4.4.4 | Submit with completed challenge | All forms accept submission | ☐ |

### 4.5 Docker + R2 Storage

```dotenv
Storage__Provider=R2
Storage__R2Bucket=your-bucket
Storage__R2Endpoint=https://xxx.r2.cloudflarestorage.com
Storage__R2AccessKey=your-key
Storage__R2SecretKey=your-secret
```

| # | Test | Expected | Pass |
|---|------|----------|------|
| 4.5.1 | App starts | No R2 errors in `docker compose logs app` | ☐ |
| 4.5.2 | Uploads go to R2 | Check Cloudflare R2 dashboard | ☐ |

### 4.6 Docker + InMemory Overrides

Verify you can override Docker defaults back to in-memory providers:

```dotenv
Messaging__Provider=InMemory
Caching__Provider=Memory
Hangfire__Storage=InMemory
```

| # | Test | Expected | Pass |
|---|------|----------|------|
| 4.6.1 | App starts with InMemory providers | No errors (Redis/RabbitMQ containers still run but aren't used) | ☐ |
| 4.6.2 | Core flows work | Registration, login, notes CRUD all function | ☐ |

---

## Phase 5 — Docker Production Profile (Litestream)

**Objective**: Test the full production stack including Litestream backup sidecar.

### 5.1 Setup

Create `.env` with R2 credentials:
```dotenv
ASPNETCORE_ENVIRONMENT=Production
R2_ACCESS_KEY_ID=your-access-key
R2_SECRET_ACCESS_KEY=your-secret-key
R2_ENDPOINT=https://xxx.r2.cloudflarestorage.com
R2_BUCKET=saas-backups
Litestream__Enabled=true

# Use mock providers for other services to isolate Litestream testing
Billing__Provider=Mock
Email__Provider=Console
Turnstile__Provider=Mock
Storage__Provider=Local
```

```powershell
docker compose down -v
docker compose --profile production up --build
```

### 5.2 Litestream Sidecar

| # | Test | Expected | Pass |
|---|------|----------|------|
| 5.2.1 | All 4 containers start | `docker compose --profile production ps` → `saas-app`, `saas-redis`, `saas-rabbitmq`, `saas-litestream` | ☐ |
| 5.2.2 | App starts first | Litestream waits for app health check before starting | ☐ |
| 5.2.3 | Litestream config generated | `docker compose exec app cat /app/db/litestream.yml` → shows `core.db`, `audit.db` entries | ☐ |
| 5.2.4 | Litestream replication starts | `docker compose logs litestream` → shows "replicating" messages | ☐ |
| 5.2.5 | R2 bucket populated | Check Cloudflare R2 → WAL segments being uploaded for each DB | ☐ |

### 5.3 Config Sync on New Tenant

| # | Test | Expected | Pass |
|---|------|----------|------|
| 5.3.1 | Register a new tenant | Tenant created successfully | ☐ |
| 5.3.2 | Wait up to 5 minutes | Config sync job regenerates `litestream.yml` | ☐ |
| 5.3.3 | New tenant DB in config | `docker compose exec app cat /app/db/litestream.yml` → includes new tenant DB path | ☐ |
| 5.3.4 | Litestream reloads | Sidecar logs show config reload, begins replicating new DB | ☐ |

### 5.4 Hangfire DB Backup

| # | Test | Expected | Pass |
|---|------|----------|------|
| 5.4.1 | Hangfire DB in Litestream config | `cat /app/db/litestream.yml` → includes `hangfire.db` entry (Hangfire=SQLite in production) | ☐ |
| 5.4.2 | Hangfire WAL in R2 | R2 bucket shows hangfire.db backup segments | ☐ |

### 5.5 Backup Restore Test

```powershell
# Stop everything
docker compose --profile production down

# Remove data (simulate data loss)
docker volume rm saas_app-data

# Restart
docker compose --profile production up -d

# Watch app logs
docker compose logs -f app
```

| # | Test | Expected | Pass |
|---|------|----------|------|
| 5.5.1 | Auto-restore triggers | App logs show restore process: restoring `core.db`, `audit.db`, then tenant DBs | ☐ |
| 5.5.2 | Core DB restored | Super admin login works, tenants list shows all previous tenants | ☐ |
| 5.5.3 | Tenant DBs restored | Can log into previously created tenants, notes/data intact | ☐ |
| 5.5.4 | Hangfire DB restored | Hangfire dashboard shows previous job history | ☐ |
| 5.5.5 | Litestream resumes | Sidecar continues replicating after restore | ☐ |

### 5.6 Key Backup

| # | Test | Expected | Pass |
|---|------|----------|------|
| 5.6.1 | Key backup running | App logs or `/health` shows key backup healthy | ☐ |
| 5.6.2 | Keys in R2 | R2 bucket has `system/keys/dataprotection-keys.zip` | ☐ |
| 5.6.3 | After restore, auth still works | Data protection keys restored → existing auth cookies still valid | ☐ |

### 5.7 Health Check (Production)

| # | Test | Expected | Pass |
|---|------|----------|------|
| 5.7.1 | `/health` full status | All 3 checks healthy: `core-database`, `tenant-directory`, `litestream-readiness` | ☐ |
| 5.7.2 | IP restriction | Set `HealthCheck__AllowedIPs=1.2.3.4` → your request should be blocked (403 or empty) | ☐ |
| 5.7.3 | Remove restriction | Set to empty → `/health` accessible again | ☐ |

---

## Issue Log Template

Copy this template for each issue found:

```markdown
### Issue #[N]

- **Phase**: [1/2/3/4/5]
- **Test #**: [e.g. 3.2.3]
- **Environment**: [Local `dotnet run` / Docker / Docker Production]
- **Provider Config**: [e.g. Billing=Mock, Email=Console, ...]
- **Steps to Reproduce**:
  1. ...
  2. ...
  3. ...
- **Expected**: ...
- **Actual**: ...
- **Error Logs** (if any):
  ```
  paste relevant log output here
  ```
- **Screenshots**: [attach if applicable]
- **Severity**: [Critical / Major / Minor / Cosmetic]
- **Notes**: ...
```

### Severity Definitions

| Severity | Definition |
|----------|-----------|
| **Critical** | App crashes, data loss, or security vulnerability. Blocks deployment. |
| **Major** | Feature broken end-to-end. User cannot complete a key workflow. |
| **Minor** | Feature works but with incorrect behavior, bad UX, or missing validation. |
| **Cosmetic** | Visual/wording issue. No functional impact. |

---

## Quick Reference — Provider Config Matrix

| Provider | Config Key | Options | Local Default | Docker Default | Production |
|----------|-----------|---------|---------------|----------------|------------|
| Billing | `Billing:Provider` | `Mock`, `Paystack` | Mock | Mock | Paystack |
| Email | `Email:Provider` | `Console`, `Smtp`, `MailerSend` | Console | Console | MailerSend |
| Bot Protection | `Turnstile:Provider` | `Mock`, `Cloudflare` | Mock | Mock | Cloudflare |
| Storage | `Storage:Provider` | `Local`, `R2` | Local | Local | R2 |
| Messaging | `Messaging:Provider` | `InMemory`, `RabbitMQ` | InMemory | RabbitMQ | RabbitMQ |
| Caching | `Caching:Provider` | `Memory`, `Redis` | Memory | Redis | Redis |
| Hangfire | `Hangfire:Storage` | `InMemory`, `SQLite` | InMemory | SQLite | SQLite |
| Litestream | `Litestream:Enabled` | `true`, `false` | false | true (config only) | true + sidecar |

## Quick Reference — Test Cards (Paystack)

| Card Number | Type | Result |
|-------------|------|--------|
| `4084 0840 8408 4081` | Visa | Success (OTP: `123456`) |
| `5060 6666 6666 6666 666` | Verve | Success |
| `4084 0840 8408 4082` | Visa | Declined |

## Quick Reference — Turnstile Test Keys

| Behavior | Site Key | Secret Key |
|----------|----------|------------|
| Always passes | `1x00000000000000000000AA` | `1x0000000000000000000000000000000AA` |
| Always fails | `2x00000000000000000000AB` | `2x0000000000000000000000000000000AB` |
| Forces challenge | `3x00000000000000000000FF` | `3x0000000000000000000000000000000FF` |
