# Production Deployment Guide

General guide for running the SaaS platform in production. For Coolify-specific steps, see [DEPLOYMENT-COOLIFY.md](DEPLOYMENT-COOLIFY.md).

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Prerequisites](#2-prerequisites)
3. [Environment Setup](#3-environment-setup)
4. [Service Exposure & Port Security](#4-service-exposure--port-security)
5. [First Boot Sequence](#5-first-boot-sequence)
6. [Admin Access](#6-admin-access)
7. [Backup Strategy](#7-backup-strategy)
8. [Monitoring](#8-monitoring)
9. [Security Checklist](#9-security-checklist)
10. [Operational Runbook](#10-operational-runbook)

---

## 1. Architecture Overview

```
Internet
    │
    ▼
┌─────────────────────┐
│  Reverse Proxy       │   Coolify / Caddy / Nginx (TLS termination)
│  :443 → app:8080     │
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐    ┌─────────────────┐
│  App (.NET 10)       │◄──►│  Redis           │   Caching
│  :8080               │    │  127.0.0.1:6379  │
│                      │    └─────────────────┘
│  SQLite databases:   │    ┌─────────────────┐
│   /app/db/core.db    │◄──►│  RabbitMQ        │   Messaging
│   /app/db/audit.db   │    │  127.0.0.1:5672  │
│   /app/db/tenants/*  │    └─────────────────┘
│   /app/db/hangfire.db│    ┌─────────────────┐
│                      │───►│  Seq             │   Structured logs
└──────────┬──────────┘    │  127.0.0.1:5341  │
           │                └─────────────────┘
           ▼
┌─────────────────────┐    ┌─────────────────┐
│  Litestream sidecar  │───►│  Cloudflare R2   │   Continuous backup
│  (reads WAL files)   │    │  (S3-compatible) │
└─────────────────────┘    └─────────────────┘
```

### External Services

| Service | Purpose | Provider |
|---------|---------|----------|
| Billing | Subscriptions & payments | Paystack |
| Email | Transactional emails | MailerSend or SMTP |
| Bot protection | Registration captcha | Cloudflare Turnstile |
| Object storage | File uploads | Cloudflare R2 |
| Database backup | Continuous SQLite replication | Litestream → R2 |

---

## 2. Prerequisites

- Linux VPS with Docker & Docker Compose v2
- Domain name with DNS A record → server IP
- Reverse proxy with automatic TLS (Coolify, Caddy, or Nginx + certbot)
- Accounts: Paystack (live), MailerSend/SMTP, Cloudflare (Turnstile + R2)

### Resource Requirements

| Component | Minimum | Recommended |
|-----------|---------|-------------|
| CPU | 1 vCPU | 2 vCPU |
| RAM | 1 GB | 2 GB |
| Disk | 10 GB | 20 GB SSD |
| Network | 100 Mbps | 1 Gbps |

---

## 3. Environment Setup

```bash
# Clone and configure
git clone <your-repo> && cd saas
cp .env.example .env

# Edit .env with production values
# At minimum, set:
#   Site__BaseUrl, SuperAdmin__Email
#   Billing__Paystack__SecretKey/PublicKey/WebhookSecret
#   Email provider credentials
#   Turnstile keys
#   Storage__R2* credentials
#   R2_ACCESS_KEY_ID, R2_SECRET_ACCESS_KEY (for Litestream)
```

Key variables to change from defaults:

| Variable | Why |
|----------|-----|
| `ASPNETCORE_ENVIRONMENT` | Must be `Production` |
| `Messaging__RabbitMQ__Username` | Change from `guest` |
| `Messaging__RabbitMQ__Password` | Change from `guest` |
| `DevSeed__Enabled` | Must be `false` |

See [.env.example](../.env.example) for the full list with descriptions.

---

## 4. Service Exposure & Port Security

The `docker-compose.yml` binds infrastructure ports to `127.0.0.1` by default, meaning they are **only accessible from the host machine** — not from the internet.

| Service | Container Port | Host Binding | Access Method |
|---------|---------------|--------------|---------------|
| App | 8080 | `0.0.0.0:8080` | Via reverse proxy |
| Redis | 6379 | `127.0.0.1:6379` | SSH tunnel only |
| RabbitMQ AMQP | 5672 | `127.0.0.1:5672` | SSH tunnel only |
| RabbitMQ UI | 15672 | `127.0.0.1:15672` | SSH tunnel only |
| Seq API | 5341 | `127.0.0.1:5341` | SSH tunnel only |
| Seq UI | 80 | `127.0.0.1:8081` | SSH tunnel only |
| Uptime Kuma | 3001 | `127.0.0.1:3001` | SSH tunnel only |

### Accessing Admin UIs via SSH Tunnel

```bash
# RabbitMQ Management (http://localhost:15672)
ssh -L 15672:127.0.0.1:15672 user@your-server

# Seq Log Viewer (http://localhost:8081)
ssh -L 8081:127.0.0.1:8081 user@your-server

# Uptime Kuma (http://localhost:3001)
ssh -L 3001:127.0.0.1:3001 user@your-server

# Multiple tunnels at once
ssh -L 15672:127.0.0.1:15672 \
    -L 8081:127.0.0.1:8081 \
    -L 3001:127.0.0.1:3001 \
    user@your-server
```

### Hangfire Dashboard

The Hangfire job dashboard is available at `/super-admin/hangfire` and is protected by the Super Admin authentication filter. No SSH tunnel required — access it through the normal app URL after logging in to Super Admin.

---

## 5. First Boot Sequence

On first deploy with an empty data volume, the app performs these steps automatically:

```
1. Auto-restore gate
   └─ Checks R2 for existing backups, restores if found
2. Database creation
   └─ core.db, audit.db created if not present
3. EF Core migrations
   └─ Schema applied to all databases
4. CoreDataSeeder
   └─ Seeds plans (Free, Starter, Professional, Enterprise)
   └─ Seeds feature flags
5. Super Admin account
   └─ Created from SuperAdmin__Email
6. PaystackPlanSyncService (within 5 seconds)
   └─ Creates plans in Paystack with correct prices
7. LitestreamConfigSyncService (within 5 minutes)
   └─ Generates litestream.yml for the sidecar
8. Litestream sidecar starts
   └─ Waits for config, begins continuous replication
```

### Verification Checklist

After first boot, confirm:

- [ ] `curl https://your-domain.com/health` returns `Healthy`
- [ ] Super Admin login works at `/super-admin/login`
- [ ] Plans visible in Super Admin → Plans
- [ ] Plans synced to Paystack Dashboard → Plans
- [ ] Test registration creates a tenant
- [ ] Magic link email arrives
- [ ] Litestream logs show `replicating to` messages

---

## 6. Admin Access

### Super Admin Panel

- **URL**: `https://your-domain.com/super-admin/login`
- **Auth**: Magic link sent to `SuperAdmin__Email`
- **Pages**: Dashboard, Tenants, Plans, Features, Backups, Audit Log, Jobs

### Paystack Webhook URL

```
https://your-domain.com/api/webhooks/paystack
```

Set this in Paystack Dashboard → Settings → Webhooks. Enable events:
- `charge.success`
- `subscription.create`, `subscription.disable`, `subscription.not_renew`
- `invoice.create`, `invoice.update`, `invoice.payment_failed`

---

## 7. Backup Strategy

### Continuous Replication (Litestream)

All SQLite databases are continuously replicated to Cloudflare R2:

| Database | R2 Path | Content |
|----------|---------|---------|
| `core.db` | `s3://{bucket}/core.db` | Plans, tenants, subscriptions |
| `audit.db` | `s3://{bucket}/audit.db` | Audit log |
| `hangfire.db` | `s3://{bucket}/hangfire.db` | Job history |
| `tenants/{slug}.db` | `s3://{bucket}/tenants/{slug}.db` | Per-tenant data |

New tenant databases are auto-detected and added to Litestream config within 5 minutes.

### DataProtection Keys

ASP.NET DataProtection keys in `/app/db/keys/` are backed up separately when `Litestream__KeyBackupEnabled=true`. These are critical for decrypting auth cookies — losing them invalidates all active sessions.

### Disaster Recovery

If the data volume is lost:

```bash
# Simply restart — the auto-restore gate handles everything
docker compose down
docker compose --profile production up -d
docker compose logs -f app   # Watch restore + migration sequence
```

### Manual Snapshot (Belt & Suspenders)

```bash
# Snapshot the Docker volume to a tarball
docker run --rm -v saas_app-data:/data -v $(pwd):/backup \
    alpine tar czf /backup/saas-backup-$(date +%Y%m%d).tar.gz -C /data .
```

---

## 8. Monitoring

### Health Endpoint

```bash
curl -s https://your-domain.com/health | jq .
```

Returns database and tenant-directory health. Restrict access with `HealthCheck__AllowedIPs` if needed.

### Seq (Structured Logs)

Access via SSH tunnel on port 8081. All application logs are sent to Seq for querying and alerting. Filter by:
- `SourceContext` — service class name
- `TenantId` — per-tenant filtering
- `RequestPath` — specific endpoints

### Uptime Kuma

Access via SSH tunnel on port 3001. Configure monitors:
- **HTTP**: `http://saas-app:8080/health` (internal Docker network)
- Alerts via Discord, Slack, email, etc.

### Background Jobs

| Service | Schedule | Purpose |
|---------|----------|---------|
| `BillingReconciliationJob` | Daily 2 AM | Verify subscription statuses |
| `PaystackPlanSyncService` | Every 60 min | Sync plans to Paystack |
| `PaystackSubscriptionSyncService` | Every 6 hours | Reconcile subscriptions |
| `LitestreamConfigSyncService` | Every 5 min | Update backup config |
| `PendingTenantCleanupService` | Periodic | Clean abandoned registrations |
| `MagicLinkCleanupService` | Periodic | Expire old tokens |

Monitor these in the Hangfire dashboard at `/super-admin/hangfire`.

---

## 9. Security Checklist

### Before Going Live

- [ ] `ASPNETCORE_ENVIRONMENT=Production` (not Development)
- [ ] `DevSeed__Enabled=false`
- [ ] RabbitMQ credentials changed from `guest/guest`
- [ ] All infrastructure ports bound to `127.0.0.1`
- [ ] Paystack using **live** keys (not `sk_test_`)
- [ ] Turnstile using **real** site/secret keys (not test keys)
- [ ] Email provider configured and domain verified (SPF/DKIM/DMARC)
- [ ] R2 backup bucket created and Litestream running
- [ ] Reverse proxy with TLS termination in front of app
- [ ] SSH key-only auth on the server (no password login)
- [ ] Firewall allows only 22, 80, 443

### Ongoing

- [ ] Monitor Litestream logs for replication errors
- [ ] Review Paystack webhook delivery log weekly
- [ ] Rotate R2 API tokens periodically
- [ ] Keep Docker images updated (`docker compose pull`)
- [ ] Review Super Admin audit log for unusual activity

---

## 10. Operational Runbook

### Restart the App

```bash
docker compose restart app
```

### View Logs

```bash
# App logs (last 100 lines, follow)
docker compose logs -f --tail 100 app

# Litestream logs
docker compose logs -f litestream

# All services
docker compose logs -f
```

### Scale Hangfire Workers

```env
Hangfire__WorkerCount=4
```

Then restart: `docker compose restart app`

### Add a New Tenant Database to Backup

Happens automatically via `LitestreamConfigSyncService` within 5 minutes. To force immediately:

```bash
docker compose restart litestream
```

### Full Redeployment

```bash
git pull
docker compose --profile production up -d --build
```

The `app-data` volume persists across rebuilds. Migrations run automatically on startup.

### Emergency: Restore from Backup

```bash
docker compose down
docker volume rm saas_app-data    # WARNING: removes local data
docker compose --profile production up -d
# Auto-restore gate fetches all databases from R2
docker compose logs -f app
```

### Check Litestream Replication Status

```bash
docker compose exec litestream litestream snapshots -config /app/db/litestream.yml s3://saas-backups/core.db
```
