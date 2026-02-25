# Configuration Reference

Complete reference for every `appsettings.json` section. All settings follow the standard ASP.NET Core configuration layering: `appsettings.json` → `appsettings.{Environment}.json` → environment variables → command line.

---

## Site

General site identity. Used in emails, SEO, and tenant provisioning.

```json
{
  "Site": {
    "BaseUrl": "https://localhost:5001",
    "Name": "SaaS App",
    "SupportEmail": "support@example.com"
  }
}
```

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `BaseUrl` | string | `https://localhost:5001` | Public URL — used for magic link emails, verification links, sitemap |
| `Name` | string | `SaaS App` | Site name — displayed in emails, page titles |
| `SupportEmail` | string | — | Contact form submissions sent here |

**Option class:** `SiteSettings`

---

## ConnectionStrings

Database paths (SQLite).

```json
{
  "ConnectionStrings": {
    "CoreDatabase": "Data Source=db/core.db",
    "AuditDatabase": "Data Source=db/audit.db"
  }
}
```

| Key | Default | Description |
|-----|---------|-------------|
| `CoreDatabase` | `Data Source=db/core.db` | Core DB (tenants, plans, subscriptions, billing, features, super admins) |
| `AuditDatabase` | `Data Source=db/audit.db` | Audit DB (entity change tracking) |

Tenant databases are stored at `{Tenancy:DatabasePath}/{slug}.db` (not in ConnectionStrings).

---

## SuperAdmin

```json
{
  "SuperAdmin": {
    "Email": "admin@example.com"
  }
}
```

| Key | Type | Description |
|-----|------|-------------|
| `Email` | string | Default super admin email — seeded on first boot by `CoreDataSeeder` |

---

## Tenancy

```json
{
  "Tenancy": {
    "DatabasePath": "db/tenants",
    "PendingCleanupHours": 24
  }
}
```

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `DatabasePath` | string | `db/tenants` | Directory where tenant SQLite databases are stored |
| `PendingCleanupHours` | int | `24` | Hours before abandoned `PendingSetup` tenants are cleaned up |

---

## Email

```json
{
  "Email": {
    "Provider": "Console",
    "FromAddress": "noreply@example.com",
    "FromName": "SaaS App",
    "Smtp": {
      "Host": "smtp.example.com",
      "Port": 587,
      "Username": "",
      "Password": "",
      "UseSsl": true
    },
    "MailerSend": {
      "ApiKey": "",
      "FromAddress": "",
      "FromName": ""
    }
  }
}
```

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Provider` | string | `Console` | Email provider: `Console`, `Smtp`, `MailerSend` |
| `FromAddress` | string | — | Default sender address |
| `FromName` | string | — | Default sender display name |

### Provider Details

| Provider | Class | Use Case |
|----------|-------|----------|
| `Console` | `ConsoleEmailService` | Development — logs email content to console |
| `Smtp` | `SmtpEmailService` | Staging/production with SMTP server |
| `MailerSend` | `MailerSendEmailService` | Production with MailerSend API |

**Option class:** `EmailOptions`

---

## Billing

```json
{
  "Billing": {
    "Provider": "Mock",
    "Currency": "ZAR",
    "Paystack": {
      "SecretKey": "",
      "PublicKey": "",
      "WebhookSecret": "",
      "CallbackBaseUrl": "https://localhost:5001"
    },
    "Tax": {
      "Rate": 0.15,
      "Label": "VAT",
      "Included": true
    },
    "Company": {
      "Name": "SaaS App",
      "Address": "",
      "VatNumber": ""
    },
    "Invoice": {
      "Prefix": "INV",
      "PaymentTermDays": 7
    },
    "Trial": {
      "DefaultDays": 14
    },
    "GracePeriod": {
      "Days": 3,
      "DunningAttempts": 3,
      "DunningIntervalHours": 72
    },
    "Features": {
      "AnnualBilling": true,
      "PerSeatBilling": false,
      "UsageBilling": false,
      "AddOns": false,
      "Discounts": true,
      "SetupFees": false
    },
    "UsageMetrics": {}
  }
}
```

### Billing Root

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Provider` | string | `Mock` | Billing provider: `Mock` or `Paystack` |
| `Currency` | string | `ZAR` | Currency code for all charges |

### Billing:Paystack

| Key | Type | Description |
|-----|------|-------------|
| `SecretKey` | string | Paystack secret key (`sk_live_...` or `sk_test_...`) |
| `PublicKey` | string | Paystack public key (`pk_live_...`) |
| `WebhookSecret` | string | Paystack webhook signing secret |
| `CallbackBaseUrl` | string | Base URL for Paystack payment callback redirects |

### Billing:Tax

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Rate` | decimal | `0.15` | Tax rate (15% VAT) |
| `Label` | string | `VAT` | Tax label on invoices |
| `Included` | bool | `true` | If true, prices are tax-inclusive; if false, tax is added on top |

### Billing:Company

| Key | Type | Description |
|-----|------|-------------|
| `Name` | string | Company name printed on invoices |
| `Address` | string | Company address on invoices |
| `VatNumber` | string | VAT registration number on invoices |

### Billing:Invoice

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Prefix` | string | `INV` | Invoice number prefix (e.g. `INV-00001`) |
| `PaymentTermDays` | int | `7` | Payment term shown on invoices |

### Billing:Trial

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `DefaultDays` | int | `14` | Default trial period for new subscriptions (null = no trial) |

### Billing:GracePeriod

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Days` | int | `3` | Grace period after failed payment before suspension |
| `DunningAttempts` | int | `3` | Number of payment retry attempts |
| `DunningIntervalHours` | int | `72` | Hours between dunning retry attempts |

### Billing:Features (Toggles)

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `AnnualBilling` | bool | `true` | Show annual billing option in pricing/checkout |
| `PerSeatBilling` | bool | `false` | Enable per-seat (per-entity) billing |
| `UsageBilling` | bool | `false` | Enable metered usage billing |
| `AddOns` | bool | `false` | Enable purchasable add-ons |
| `Discounts` | bool | `true` | Enable discount codes |
| `SetupFees` | bool | `false` | Enable one-time setup fees |

### Billing:UsageMetrics

Dictionary of usage metric configurations keyed by metric name:

```json
{
  "UsageMetrics": {
    "medical_claims": {
      "DisplayName": "Successful Medical Claims",
      "OveragePrice": 4.50,
      "IncludedByPlan": {
        "standard": 0,
        "premium": 100
      }
    }
  }
}
```

| Key | Type | Description |
|-----|------|-------------|
| `DisplayName` | string | Human-readable metric name for invoices |
| `OveragePrice` | decimal | Price per unit beyond included amount |
| `IncludedByPlan` | dict | `{ planSlug: includedQuantity }` — null means unlimited |

### Provider Details

| Provider | Class | Use Case |
|----------|-------|----------|
| `Mock` | `MockBillingService` | Development — creates real DB records without Paystack |
| `Paystack` | `PaystackBillingService` | Production — full Paystack integration |

**Option class:** `BillingOptions`

---

## Turnstile (Bot Protection)

```json
{
  "Turnstile": {
    "Provider": "Mock",
    "SiteKey": "",
    "SecretKey": ""
  }
}
```

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Provider` | string | `Mock` | Bot protection: `Mock` or `Cloudflare` |
| `SiteKey` | string | — | Cloudflare Turnstile site key |
| `SecretKey` | string | — | Cloudflare Turnstile secret key |

| Provider | Class | Use Case |
|----------|-------|----------|
| `Mock` | `MockBotProtection` | Development — always passes validation |
| `Cloudflare` | `TurnstileBotProtection` | Production — Cloudflare Turnstile verification |

**Option class:** `TurnstileOptions`

---

## Litestream (Backup)

```json
{
  "Litestream": {
    "Enabled": false,
    "LitestreamConfigPath": "db/litestream.yml",
    "SentinelPath": "db/.litestream-reload",
    "R2Bucket": "saas-backups",
    "R2Endpoint": "",
    "SyncInterval": "30s",
    "MonitorInterval": "5s",
    "CheckpointInterval": "5m",
    "SnapshotInterval": "24h",
    "SnapshotRetention": "168h",
    "AutoRestoreEnabled": true,
    "KeyBackupEnabled": true,
    "KeyBackupInterval": "1h",
    "KeyBackupPath": "system/keys/dataprotection-keys.zip",
    "KeyBackupMarkerPath": "db/.keys-last-backup"
  }
}
```

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Enabled` | bool | `false` | Enable Litestream continuous replication |
| `LitestreamConfigPath` | string | `db/litestream.yml` | Generated config file path for sidecar |
| `SentinelPath` | string | `db/.litestream-reload` | Sentinel file — touched to signal sidecar reload |
| `R2Bucket` | string | `saas-backups` | Cloudflare R2 bucket name |
| `R2Endpoint` | string | — | R2 S3-compatible endpoint URL |
| `SyncInterval` | string | `30s` | WAL sync frequency |
| `MonitorInterval` | string | `5s` | Litestream status check interval |
| `CheckpointInterval` | string | `5m` | SQLite WAL checkpoint interval |
| `SnapshotInterval` | string | `24h` | Full snapshot frequency |
| `SnapshotRetention` | string | `168h` | How long to keep snapshots (7 days) |
| `AutoRestoreEnabled` | bool | `true` | Restore from R2 on startup if DB missing |
| `KeyBackupEnabled` | bool | `true` | Back up DataProtection keys to R2 |
| `KeyBackupInterval` | string | `1h` | Key backup frequency |

**Option class:** `LitestreamOptions`

---

## Storage

```json
{
  "Storage": {
    "Provider": "Local",
    "LocalBasePath": "db/uploads",
    "R2": {
      "BucketName": "",
      "Endpoint": "",
      "AccessKey": "",
      "SecretKey": ""
    }
  }
}
```

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Provider` | string | `Local` | Storage provider: `Local` or `R2` |
| `LocalBasePath` | string | `db/uploads` | Local file storage path |

| Provider | Class | Use Case |
|----------|-------|----------|
| `Local` | `LocalStorageService` | Development — filesystem storage |
| `R2` | `R2StorageService` | Production — Cloudflare R2 |

**Option class:** `StorageOptions`

---

## Messaging

```json
{
  "Messaging": {
    "Provider": "InMemory",
    "RabbitMQ": {
      "Host": "localhost",
      "Port": 5672,
      "Username": "guest",
      "Password": "guest",
      "VirtualHost": "/"
    }
  }
}
```

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Provider` | string | `InMemory` | Message bus: `InMemory` or `RabbitMQ` |

**InMemory** uses MassTransit's in-memory transport (development). **RabbitMQ** uses a real message broker (production).

---

## Caching

```json
{
  "Caching": {
    "Provider": "Memory",
    "MemoryCacheSizeLimit": null,
    "TTL": {
      "TenantResolutionMinutes": 3,
      "FeatureDefinitionsMinutes": 5,
      "TenantOverridesMinutes": 5,
      "TenantPlanMinutes": 10,
      "RateLimitPlanMinutes": 5
    },
    "Redis": {
      "ConnectionString": "localhost:6379",
      "InstanceName": "saas:"
    }
  }
}
```

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Provider` | string | `Memory` | Cache provider: `Memory` or `Redis` |
| `MemoryCacheSizeLimit` | int? | `null` | Memory cache entry limit (null = unlimited) |

### TTL Settings

| Key | Default | Used By |
|-----|---------|---------|
| `TenantResolutionMinutes` | `3` | `TenantResolutionMiddleware` — caches slug → tenant lookup |
| `FeatureDefinitionsMinutes` | `5` | `DatabaseFeatureDefinitionProvider` — caches feature definitions |
| `TenantOverridesMinutes` | `5` | `TenantPlanFeatureFilter` — caches per-tenant feature overrides |
| `TenantPlanMinutes` | `10` | `TenantPlanFeatureFilter` — caches tenant's plan ID |
| `RateLimitPlanMinutes` | `5` | Rate limiter — caches plan-based rate limits |

---

## Hangfire

```json
{
  "Hangfire": {
    "Storage": "InMemory",
    "SQLitePath": "db/hangfire.db",
    "WorkerCount": 2
  }
}
```

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Storage` | string | `InMemory` | Hangfire storage: `InMemory` or `SQLite` |
| `SQLitePath` | string | `db/hangfire.db` | SQLite path for persistent job storage |
| `WorkerCount` | int | `2` | Number of background job workers |

Dashboard accessible at `/super-admin/hangfire` (SuperAdmin auth required).

---

## RateLimiting

```json
{
  "RateLimiting": {
    "GlobalPerMinute": 100,
    "StrictPerMinute": 5,
    "RegistrationPerWindow": 3,
    "RegistrationWindowMinutes": 5,
    "ContactPerWindow": 3,
    "ContactWindowMinutes": 5,
    "WebhookPerMinute": 50
  }
}
```

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `GlobalPerMinute` | int | `100` | Default rate limit for all routes |
| `StrictPerMinute` | int | `5` | Strict limit for sensitive operations |
| `RegistrationPerWindow` | int | `3` | Max registration attempts per window |
| `RegistrationWindowMinutes` | int | `5` | Registration window duration |
| `ContactPerWindow` | int | `3` | Max contact form submissions per window |
| `ContactWindowMinutes` | int | `5` | Contact window duration |
| `WebhookPerMinute` | int | `50` | Paystack webhook rate limit |

Additionally, tenant-aware rate limiting uses `MaxRequestsPerMinute` from the tenant's plan.

---

## Infrastructure

```json
{
  "Infrastructure": {
    "SeqUrl": "",
    "RabbitMqManagementUrl": "",
    "UptimeKumaUrl": ""
  }
}
```

| Key | Type | Description |
|-----|------|-------------|
| `SeqUrl` | string | Seq log viewer URL — embedded in SuperAdmin infrastructure page |
| `RabbitMqManagementUrl` | string | RabbitMQ management UI URL |
| `UptimeKumaUrl` | string | Uptime Kuma status page URL |

---

## DevSeed

Development-only seeding of demo data. **Only runs when `Enabled: true`**.

```json
{
  "DevSeed": {
    "Enabled": true,
    "TenantSlug": "demo",
    "TenantName": "Demo Workspace",
    "AdminEmail": "admin@demo.local",
    "MemberEmail": "member@demo.local",
    "PlanSlug": "starter"
  }
}
```

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Enabled` | bool | `false` | Enable demo data seeding on startup |
| `TenantSlug` | string | `demo` | Demo tenant slug |
| `TenantName` | string | `Demo Workspace` | Demo tenant display name |
| `AdminEmail` | string | `admin@demo.local` | Admin user email (auto-created) |
| `MemberEmail` | string | `member@demo.local` | Member user email (auto-created) |
| `PlanSlug` | string | `starter` | Plan to assign to demo tenant |

**Option class:** `DevSeedOptions`

---

## Provider Switching Matrix

All providers are switched via config — no code changes needed:

| Service | Config Key | Dev | Production |
|---------|-----------|-----|------------|
| Email | `Email:Provider` | `Console` | `MailerSend` or `Smtp` |
| Billing | `Billing:Provider` | `Mock` | `Paystack` |
| Bot Protection | `Turnstile:Provider` | `Mock` | `Cloudflare` |
| Storage | `Storage:Provider` | `Local` | `R2` |
| Messaging | `Messaging:Provider` | `InMemory` | `RabbitMQ` |
| Caching | `Caching:Provider` | `Memory` | `Redis` |
| Hangfire | `Hangfire:Storage` | `InMemory` | `SQLite` |
| Litestream | `Litestream:Enabled` | `false` | `true` |

## Environment Variable Overrides

All settings can be overridden with environment variables using `__` as the section separator:

```bash
Billing__Provider=Paystack
Billing__Paystack__SecretKey=sk_live_xxx
Email__Provider=MailerSend
Litestream__Enabled=true
DevSeed__Enabled=false
```

Docker Compose uses this pattern extensively — see `docker-compose.yml`.
