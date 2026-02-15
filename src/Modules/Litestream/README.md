# Litestream Module

Manages SQLite continuous replication via [Litestream](https://litestream.io/) to Cloudflare R2.

## What It Does

| Component | Purpose |
|-----------|---------|
| `LitestreamModule` | Registers options, status service, and (when enabled) the config sync, restore, and key backup services |
| `LitestreamConfigSyncService` | Background service that generates `litestream.yml` with all SQLite databases (core, audit, hangfire, tenants) and writes a sentinel file to trigger the sidecar reload |
| `LitestreamRestoreService` | On startup, restores missing databases from R2 replicas using the `litestream restore` CLI |
| `LitestreamStatusService` | Reports backup readiness (binary available, config exists, R2 configured, database counts) for the SuperAdmin dashboard |
| `KeyRingBackupService` | Periodically zips and uploads ASP.NET DataProtection keys to R2 |
| `LitestreamReadinessHealthCheck` | Health check endpoint reporting Litestream readiness |

## Configuration (`Litestream` section)

| Key | Default | Purpose |
|-----|---------|---------|
| `Enabled` | `false` | Master switch — all services skip when `false` |
| `LitestreamConfigPath` | `/app/db/litestream.yml` | Output path for generated YAML |
| `SentinelPath` | `/app/db/.litestream-reload` | Sentinel file path for sidecar reload |
| `R2Bucket` | `saas-backups` | Cloudflare R2 bucket name |
| `R2Endpoint` | _(empty)_ | R2 S3-compatible endpoint URL |
| `AutoRestoreEnabled` | `true` | Restore databases from R2 on startup if missing |
| `KeyBackupEnabled` | `true` | Enable periodic DataProtection key backup |
| `KeyBackupInterval` | `1h` | How often to back up keys |

## Environment Modes

| Mode | Enabled | Notes |
|------|---------|-------|
| `dotnet run` (local) | `false` | No Litestream needed — databases are local files |
| Docker Compose (dev) | `true` | Config sync runs; Litestream sidecar in `production` profile |
| Docker Compose (prod) | `true` | Full backup + restore + sidecar replication |

## Databases Backed Up

- `core.db` — tenants, plans, subscriptions, features
- `audit.db` — audit log
- `hangfire.db` — job scheduling state (when Hangfire uses SQLite storage)
- `tenants/*.db` — per-tenant databases (discovered dynamically)
