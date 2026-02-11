# Deploying with Coolify (Docker Compose)

Guide for deploying the SaaS platform to production using [Coolify](https://coolify.io) with the project's `docker-compose.yml`.

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Create the Coolify Project](#2-create-the-coolify-project)
3. [Configure Environment Variables](#3-configure-environment-variables)
4. [Set Up External Services](#4-set-up-external-services)
5. [Deploy](#5-deploy)
6. [Post-Deployment Verification](#6-post-deployment-verification)
7. [Domain & SSL](#7-domain--ssl)
8. [Persistent Storage](#8-persistent-storage)
9. [Monitoring & Health Checks](#9-monitoring--health-checks)
10. [Updates & Redeployment](#10-updates--redeployment)
11. [Backup & Recovery](#11-backup--recovery)
12. [Troubleshooting](#12-troubleshooting)

---

## 1. Prerequisites

- A Coolify instance running on your server (self-hosted or Coolify Cloud)
- A domain name pointed at your server (e.g. `app.yourdomain.com`)
- Accounts with:
  - [Paystack](https://dashboard.paystack.com) — billing
  - [MailerSend](https://www.mailersend.com) — transactional email
  - [Cloudflare](https://dash.cloudflare.com) — Turnstile + R2 storage + R2 backups
- Your Git repository accessible from Coolify (GitHub, GitLab, etc.)

---

## 2. Create the Coolify Project

1. In Coolify dashboard, click **New Resource → Docker Compose**
2. Connect your Git repository
3. Set the **Docker Compose file** to `docker-compose.yml`
4. **Important**: Coolify merges `docker-compose.override.yml` by default (same as Docker). You must either:

   **Option A (Recommended)**: In Coolify's compose settings, set the compose file explicitly to `docker-compose.yml` only — this excludes the dev override.

   **Option B**: Delete or rename `docker-compose.override.yml` in your production branch.

   The override file sets `Development` environment and mock providers — you do NOT want this in production.

5. Coolify will detect two services: `app` and `litestream`

---

## 3. Configure Environment Variables

All production config is defined in `docker-compose.yml` with `CHANGE_ME` placeholders. In Coolify, set these via the **Environment Variables** panel for the `app` service.

### Required Variables

#### Site Configuration

| Variable | Example | Notes |
|---|---|---|
| `Site__BaseUrl` | `https://app.yourdomain.com` | Must match your public domain with `https://` |
| `Site__Name` | `Your SaaS` | Display name in emails and UI |
| `Site__SupportEmail` | `support@yourdomain.com` | Contact form submissions go here |

#### Super Admin

| Variable | Example | Notes |
|---|---|---|
| `SuperAdmin__Email` | `admin@yourdomain.com` | First login creates the super admin account |

#### Billing (Paystack)

| Variable | Example | Notes |
|---|---|---|
| `Billing__Provider` | `Paystack` | Already set in compose |
| `Billing__Paystack__SecretKey` | `sk_live_xxxxx` | **Live** key from Paystack dashboard |
| `Billing__Paystack__PublicKey` | `pk_live_xxxxx` | **Live** key from Paystack dashboard |
| `Billing__Paystack__WebhookSecret` | `whsec_xxxxx` | From Paystack webhook settings |
| `Billing__Paystack__CallbackBaseUrl` | `https://app.yourdomain.com` | Must match `Site__BaseUrl` |

#### Email (MailerSend)

| Variable | Example | Notes |
|---|---|---|
| `Email__Provider` | `MailerSend` | Already set in compose |
| `Email__FromAddress` | `noreply@yourdomain.com` | Must match verified domain in MailerSend |
| `Email__FromName` | `Your SaaS` | Display name on sent emails |
| `Email__MailerSend__ApiToken` | `mlsn.xxxxx` | API token from MailerSend dashboard |

#### Bot Protection (Cloudflare Turnstile)

| Variable | Example | Notes |
|---|---|---|
| `Turnstile__Provider` | `Cloudflare` | Already set in compose |
| `Turnstile__SiteKey` | `0x4AAAA...` | From Cloudflare Turnstile dashboard |
| `Turnstile__SecretKey` | `0x4AAAA...` | From Cloudflare Turnstile dashboard |

> Add your production domain to the Turnstile site configuration in Cloudflare.

#### Storage (Cloudflare R2)

| Variable | Example | Notes |
|---|---|---|
| `Storage__Provider` | `R2` | Already set in compose |
| `Storage__R2Bucket` | `your-saas-storage` | R2 bucket name |
| `Storage__R2Endpoint` | `https://xxxx.r2.cloudflarestorage.com` | Your R2 S3-compatible endpoint |
| `Storage__R2AccessKey` | `xxxxx` | R2 API token access key |
| `Storage__R2SecretKey` | `xxxxx` | R2 API token secret key |
| `Storage__R2PublicUrl` | `https://cdn.yourdomain.com` | Optional public URL for assets |

#### Litestream Backup (R2)

These are set on the `litestream` service (or the `.env` file):

| Variable | Example | Notes |
|---|---|---|
| `R2_ACCESS_KEY_ID` | `xxxxx` | Can be same as Storage R2 key or separate |
| `R2_SECRET_ACCESS_KEY` | `xxxxx` | R2 API token secret |
| `R2_ENDPOINT` | `https://xxxx.r2.cloudflarestorage.com` | R2 endpoint |
| `R2_BUCKET` | `saas-backups` | Separate bucket for backups recommended |

> **Tip**: In Coolify, you can set shared environment variables across services, or set them per-service.

### Variables That Are Already Set (No Change Needed)

These are pre-configured in `docker-compose.yml` and usually don't need overriding:

| Variable | Value | Notes |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Do not change |
| `ASPNETCORE_HTTP_PORTS` | `8080` | Internal port |
| `FeatureFlags__AllEnabledLocally` | `false` | Feature flags governed by plan |
| `HealthCheck__AllowedIPs` | (empty) | Empty = allow all; set IPs to restrict |

---

## 4. Set Up External Services

Before deploying, ensure these are ready:

### Paystack
1. Switch to **Live mode** in Paystack dashboard
2. Copy **live** API keys (`sk_live_`, `pk_live_`)
3. Set webhook URL: `https://app.yourdomain.com/api/webhooks/paystack`
4. Enable relevant events: `charge.success`, `subscription.create`, `subscription.disable`, `subscription.not_renew`, `subscription.expiring_cards`, `invoice.create`, `invoice.update`, `invoice.payment_failed`

### MailerSend
1. Add and verify your sending domain in MailerSend
2. Complete DNS verification (SPF, DKIM, DMARC records)
3. Generate an API token with full access

### Cloudflare Turnstile
1. Add your production domain to the Turnstile widget configuration
2. Widget type: "Managed" (recommended for production)

### Cloudflare R2
1. Create two buckets:
   - **Storage bucket** (e.g. `your-saas-storage`) — for file uploads
   - **Backup bucket** (e.g. `saas-backups`) — for Litestream database backups
2. Create R2 API tokens with **Object Read & Write** for each bucket
3. Optionally set up a custom domain for public access on the storage bucket

---

## 5. Deploy

1. Push your code to the connected Git repository
2. In Coolify, trigger a deploy (or enable auto-deploy on push)
3. Coolify will:
   - Build the `app` image from `src/Dockerfile`
   - Pull the `litestream/litestream:0.5` image
   - Start the `app` service first
   - Wait for health check to pass
   - Start the `litestream` sidecar

### First Deploy Notes

On first deploy:
- The app creates `core.db`, `audit.db`, and the `tenants/` directory
- `CoreDataSeeder` seeds plans (Free, Starter, Professional, Enterprise) and features
- Super Admin account is created with the email from `SuperAdmin__Email`
- `PaystackPlanSyncService` syncs plans to Paystack within 5 seconds
- No demo tenant is created (DevSeed is off in production)

---

## 6. Post-Deployment Verification

Run through this checklist after the first deploy:

### Health Check
```bash
curl https://app.yourdomain.com/health
# Expected: {"status":"Healthy","results":{"core-database":{"status":"Healthy"},"tenant-directory":{"status":"Healthy"}}}
```

### Super Admin Access
1. Navigate to `https://app.yourdomain.com/super-admin/login`
2. Enter your `SuperAdmin__Email`
3. Check your email for the magic link
4. Click to log in → you should see the Super Admin dashboard

### Plan Sync
1. In Super Admin → Plans, verify all 4 plans are listed
2. In Paystack Dashboard → Plans, verify Starter/Professional/Enterprise plans exist with correct prices

### Registration Flow
1. Open `https://app.yourdomain.com/register`
2. Turnstile widget should appear
3. Register a Free plan tenant → should provision immediately
4. Register a paid plan tenant → should redirect to Paystack checkout

### Email Delivery
1. Trigger a magic link login
2. Verify email arrives from your configured `FromAddress`
3. Check SES sending statistics in AWS console

### Webhook Connectivity
1. In Paystack Dashboard → Webhooks, check the delivery log
2. After a successful payment, you should see `200 OK` responses
3. Check app logs for `[Paystack Webhook]` entries

### Litestream Backup
```bash
# Check litestream container logs in Coolify
# Look for: "replicating to" messages for each database
```

---

## 7. Domain & SSL

Coolify handles SSL automatically via Let's Encrypt. Ensure:

1. Your domain's DNS A record points to your server's IP
2. In Coolify, set the domain for the `app` service: `app.yourdomain.com`
3. Enable HTTPS (Coolify generates and renews certs automatically)
4. The app listens on port `8080` internally — Coolify's reverse proxy maps `443 → 8080`

### Paystack Callback URLs
After setting up your domain, verify these URLs work:
- Registration callback: `https://app.yourdomain.com/register/callback`
- Billing callback: `https://app.yourdomain.com/{tenant-slug}/billing/callback`
- Webhook: `https://app.yourdomain.com/api/webhooks/paystack`

---

## 8. Persistent Storage

The `docker-compose.yml` defines a named volume `app-data` mounted at `/app/db`. This contains:

```
/app/db/
├── core.db           # Plans, tenants, subscriptions, invoices
├── audit.db          # Shared audit log
├── keys/             # Data protection keys (encryption)
├── litestream.yml    # Auto-generated backup config
├── tenants/
│   ├── tenant-a.db   # Per-tenant data
│   └── tenant-b.db
└── uploads/          # Local storage (if using Local provider)
```

**Critical**: This volume must persist across redeployments. In Coolify:
- Verify the volume is NOT set to be recreated on deploy
- Consider backing up the volume directory on the host as an additional safety measure
- Litestream provides continuous backup to R2, but local volume = fastest recovery

---

## 9. Monitoring & Health Checks

### Built-in Health Check

The Docker health check runs every 30 seconds:
```
curl -f http://localhost:8080/health
```

Coolify uses this to determine service readiness and can restart unhealthy containers.

### Restrict Health Check Access (Optional)

To restrict the `/health` endpoint to specific IPs:
```
HealthCheck__AllowedIPs=10.0.0.1,192.168.1.100
```
Empty = allow all (default).

### Key Metrics to Monitor

| What | How | Where |
|---|---|---|
| App health | `/health` endpoint | Coolify dashboard |
| Container restarts | Docker restart count | Coolify dashboard |
| Litestream status | Sidecar container logs | `docker compose logs litestream` |
| Email delivery | SES sending statistics | AWS SES console |
| Payment webhooks | Webhook delivery log | Paystack dashboard |
| Error rates | Application logs | Coolify log viewer |

### Background Services

These run automatically in production:

| Service | Interval | Purpose |
|---|---|---|
| `PaystackPlanSyncService` | Every 60 min | Sync plans to Paystack |
| `PaystackSubscriptionSyncService` | Every 6 hours | Reconcile subscription statuses |
| `LitestreamConfigSyncService` | Every 5 min | Update backup config for new tenants |
| `PendingTenantCleanupService` | Periodic | Clean abandoned registration attempts |
| `MagicLinkCleanupService` | Periodic | Expire old magic link tokens |

---

## 10. Updates & Redeployment

### Standard Update

1. Push changes to your Git repository
2. In Coolify, trigger a new deployment
3. Coolify rebuilds the `app` image and restarts the container
4. The `app-data` volume persists — no data loss
5. EF Core migrations run automatically on startup if pending

### Zero-Downtime Considerations

- Coolify does rolling deploys if configured
- The health check ensures the new container is ready before routing traffic
- Litestream sidecar restarts after the app is healthy
- Short interruption (seconds) during container swap is expected with single-instance deployment

### Rollback

If a deploy fails:
1. In Coolify, click on the previous deployment
2. Trigger a rollback
3. Or: revert the Git commit and redeploy

---

## 11. Backup & Recovery

### How Backup Works

1. The app's `LitestreamConfigSyncService` generates a YAML config listing all SQLite databases
2. The Litestream sidecar continuously replicates WAL changes to Cloudflare R2
3. New tenant databases are detected within 5 minutes and added to the backup

### What's Backed Up

| Database | Content | R2 Path |
|---|---|---|
| `core.db` | Plans, tenants, subscriptions, invoices, features | `s3://saas-backups/core.db` |
| `audit.db` | Audit log entries | `s3://saas-backups/audit.db` |
| `tenants/*.db` | Per-tenant users, roles, notes, data | `s3://saas-backups/tenants/{slug}.db` |

### Disaster Recovery

If you lose the data volume:

```bash
# 1. Stop the app
docker compose -f docker-compose.yml down

# 2. Manually restore from R2 using litestream
docker run --rm -v saas_app-data:/app/db \
  -e LITESTREAM_ACCESS_KEY_ID=your-key \
  -e LITESTREAM_SECRET_ACCESS_KEY=your-secret \
  litestream/litestream:0.5 \
  restore -config /app/db/litestream.yml /app/db/core.db

# 3. Repeat for audit.db and each tenant DB
# 4. Restart the app
docker compose -f docker-compose.yml up -d
```

> **Recommendation**: Also take periodic snapshots of the Docker volume or host directory as a secondary backup.

---

## 12. Troubleshooting

### App Won't Start

| Symptom | Cause | Fix |
|---|---|---|
| Container exits immediately | Missing required config | Check Coolify logs for startup errors |
| Health check fails | Database path permission issue | Ensure volume is mounted correctly |
| `InvalidOperationException` on startup | R2 credentials missing | Set all `Storage__R2*` variables |

### Litestream Not Starting

| Symptom | Cause | Fix |
|---|---|---|
| Sidecar exits immediately | App not healthy yet | Litestream depends on app health check |
| "config file not found" | App hasn't generated config | Check app logs for `LitestreamConfigSync` |
| R2 access denied | Wrong credentials in `.env` | Verify `R2_ACCESS_KEY_ID` and `R2_SECRET_ACCESS_KEY` |

### Paystack Issues

| Symptom | Cause | Fix |
|---|---|---|
| Plans not appearing in Paystack | Sync failed | Check logs for `PaystackPlanSync` errors |
| Webhooks returning 500 | Wrong webhook secret | Match the secret in Paystack dashboard settings |
| Checkout redirect fails | `CallbackBaseUrl` wrong | Must match your public domain with `https://` |
| "Transaction not found" on callback | Race condition | Webhook may arrive before redirect; usually resolves within seconds |

### Email Issues

| Symptom | Cause | Fix |
|---|---|---|
| No emails sent | SES in sandbox mode | Request production access in AWS |
| Emails go to spam | Domain not authenticated | Set up SPF, DKIM, DMARC for your domain |
| `MessageRejected` | From address not verified | Verify in SES console |

### General

| Symptom | Cause | Fix |
|---|---|---|
| 502 Bad Gateway | App still starting | Wait for health check to pass (up to 10s) |
| SSL errors | DNS not propagated | Wait for DNS propagation; check A record |
| Styles/JS broken | Asset pipeline issue | Ensure `libman restore` ran during Docker build |
| Features not gated | `AllEnabledLocally=true` leaked | Verify env var is `false` in production |

---

## Quick Reference: All Environment Variables

```yaml
# ── App Service ──────────────────────────────────────────
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_HTTP_PORTS=8080
Site__BaseUrl=https://app.yourdomain.com
Site__Name=Your SaaS
Site__SupportEmail=support@yourdomain.com
SuperAdmin__Email=admin@yourdomain.com
FeatureFlags__AllEnabledLocally=false
Billing__Provider=Paystack
Billing__Paystack__SecretKey=sk_live_xxxxx
Billing__Paystack__PublicKey=pk_live_xxxxx
Billing__Paystack__WebhookSecret=whsec_xxxxx
Billing__Paystack__CallbackBaseUrl=https://app.yourdomain.com
Email__Provider=SES
Email__FromAddress=noreply@yourdomain.com
Email__SES__AccessKey=AKIA...
Email__SES__SecretKey=xxxxx
Email__SES__Region=af-south-1
Turnstile__Provider=Cloudflare
Turnstile__SiteKey=0x4AAAA...
Turnstile__SecretKey=0x4AAAA...
Storage__Provider=R2
Storage__R2Bucket=your-saas-storage
Storage__R2Endpoint=https://xxxx.r2.cloudflarestorage.com
Storage__R2AccessKey=xxxxx
Storage__R2SecretKey=xxxxx
Storage__R2PublicUrl=https://cdn.yourdomain.com
Backup__R2Bucket=saas-backups
Backup__R2Endpoint=https://xxxx.r2.cloudflarestorage.com
HealthCheck__AllowedIPs=

# ── Litestream Service ───────────────────────────────────
LITESTREAM_ACCESS_KEY_ID=xxxxx
LITESTREAM_SECRET_ACCESS_KEY=xxxxx
R2_ENDPOINT=https://xxxx.r2.cloudflarestorage.com
R2_BUCKET=saas-backups
```
