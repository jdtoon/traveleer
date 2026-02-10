# 08 — Local Development

> The clone-and-run guide. Defines everything a developer needs to go from `git clone` to a running application with zero external dependencies, zero API keys, and zero manual configuration.

**Prerequisites**: [01 — Architecture](01-architecture.md), [07 — Infrastructure](07-infrastructure.md)

---

## 1. The Zero-Config Promise

A new developer should be able to run:

```bash
git clone <repo-url>
cd saas
dotnet run --project src
```

And have a fully working application. No Docker required. No API keys. No database servers. No manual setup steps. Every external service has a mock/local fallback that activates automatically when `ASPNETCORE_ENVIRONMENT=Development`.

| Concern | Production | Local Development |
|---------|-----------|-------------------|
| Database | SQLite on Docker volume | SQLite in `src/data/` (auto-created) |
| Billing | Paystack | MockBillingService (auto-approves) |
| Email | AWS SES | ConsoleEmailService (logs to terminal) |
| Bot protection | Cloudflare Turnstile | MockBotProtection (auto-passes) |
| Feature flags | DB-backed with plan filtering | All enabled (`AllEnabledLocally: true`) |
| Backups | Litestream → Cloudflare R2 | None (not needed locally) |
| HTTPS | Reverse proxy (Caddy) | ASP.NET dev cert |

---

## 2. Prerequisites

| Tool | Version | Required | Notes |
|------|---------|----------|-------|
| .NET SDK | 10.0+ | ✅ | `dotnet --version` to verify |
| Git | Any | ✅ | For cloning the repo |
| Docker | Any | ❌ | Only needed for production-like testing |
| Node.js | None | ❌ | Not used — Tailwind runs in browser, LibMan handles client packages |
| IDE | VS Code or Rider | Recommended | VS Code with C# Dev Kit extension |

### Recommended VS Code Extensions

| Extension | ID | Purpose |
|-----------|-----|---------|
| C# Dev Kit | `ms-dotnettools.csdevkit` | IntelliSense, debugging, project management |
| SQLite Viewer | `qwtel.sqlite-viewer` | Inspect SQLite databases directly in VS Code |

---

## 3. First-Time Setup

### Step 1: Clone & Restore

```bash
git clone <repo-url>
cd saas

# Restore NuGet packages
dotnet restore

# Restore client-side libraries (htmx, tailwindcss, daisyui)
dotnet tool restore        # If libman CLI is a local tool
libman restore             # Restores to wwwroot/lib/
```

> **Note**: If `libman` is not installed, install it once globally:
> ```bash
> dotnet tool install -g Microsoft.Web.LibraryManager.Cli
> ```

### Step 2: Trust the Dev Certificate (HTTPS)

```bash
dotnet dev-certs https --trust
```

This enables `https://localhost:5001` without browser warnings.

### Step 3: Run

```bash
dotnet run --project src
```

Open `https://localhost:5001` in a browser.

That's it. No Step 4.

---

## 4. What Happens on First Run

When the application starts in Development mode, the following happens automatically:

```
1. Program.cs starts
2. EnsureCreated() / Migrate()
   ├── Creates data/ directory if missing
   ├── Creates data/core.db with all CoreDbContext tables
   ├── Creates data/audit.db with AuditEntry table
   └── Seeds master data (plans, features, super admin, default plan-features)
3. MockBillingService registered (no Paystack calls)
4. ConsoleEmailService registered (emails logged to terminal)
5. MockBotProtection registered (Turnstile bypassed)
6. Feature flags: AllEnabledLocally = true (all features enabled)
7. Kestrel starts on https://localhost:5001, http://localhost:5000
8. Browser opens automatically
```

### Auto-Seeded Data

On first run, `MasterDataSeeder` creates:

| Entity | Seeded Data |
|--------|------------|
| **Plans** | Free, Starter (R199/mo), Professional (R499/mo), Enterprise (R999/mo) |
| **Features** | All features from `FeatureDefinitions` constants |
| **PlanFeatures** | Mapping of which features are in which plan |
| **SuperAdmin** | Email from `Auth:SuperAdmin:Email` config (default: `admin@localhost`) |

---

## 5. Configuration Files

### appsettings.json (Base — All Environments)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  },
  "AllowedHosts": "*",

  "ConnectionStrings": {
    "CoreDatabase": "Data Source=data/core.db",
    "AuditDatabase": "Data Source=data/audit.db"
  },
  "TenantDatabasePath": "data/tenants",

  "Auth": {
    "SuperAdmin": {
      "Email": "admin@localhost"
    },
    "MagicLink": {
      "TokenExpiryMinutes": 15
    }
  },

  "Billing": {
    "Provider": "Mock",
    "Paystack": {
      "SecretKey": "",
      "PublicKey": "",
      "WebhookSecret": "",
      "CallbackBaseUrl": "https://localhost:5001"
    }
  },

  "Email": {
    "Provider": "Console",
    "FromAddress": "noreply@localhost",
    "FromName": "SaaS App (Dev)"
  },

  "Turnstile": {
    "Provider": "Mock",
    "SiteKey": "",
    "SecretKey": ""
  },

  "FeatureFlags": {
    "AllEnabledLocally": true
  },

  "DataProtection": {
    "KeyPath": "data/keys"
  }
}
```

### appsettings.Development.json (Development Overrides)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information",
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  },

  "Auth": {
    "SuperAdmin": {
      "Email": "admin@localhost"
    }
  }
}
```

The Development config is intentionally minimal — the base `appsettings.json` already defaults to all mock/local providers. The Development file only needs logging overrides.

### appsettings.Production.json (Production Overrides)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    },
    "Console": {
      "FormatterName": "json",
      "FormatterOptions": {
        "SingleLine": true,
        "IncludeScopes": true
      }
    }
  },

  "Billing": {
    "Provider": "Paystack"
  },

  "Email": {
    "Provider": "SES"
  },

  "Turnstile": {
    "Provider": "Cloudflare"
  },

  "FeatureFlags": {
    "AllEnabledLocally": false
  }
}
```

Production secrets (`Paystack:SecretKey`, `Email:SES:AccessKeyId`, etc.) come from environment variables in the `.env` file — never committed to the repo.

---

## 6. Launch Profiles

### Properties/launchSettings.json

```json
{
  "$schema": "http://json.schemastore.org/launchsettings.json",
  "profiles": {
    "https": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "https://localhost:5001;http://localhost:5000",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    },
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "http://localhost:5000",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

Use `dotnet run --project src --launch-profile https` (default) for HTTPS, or `--launch-profile http` for HTTP-only.

---

## 7. Local Data Directory Structure

After the first run, the `src/data/` directory is created automatically:

```
src/
└── data/                          ← Auto-created on first run
    ├── core.db                    ← Platform DB (plans, tenants, subscriptions)
    ├── core.db-wal                ← WAL journal
    ├── core.db-shm                ← Shared memory
    ├── audit.db                   ← Audit log DB
    ├── keys/                      ← Data protection keys
    │   └── key-{guid}.xml
    └── tenants/                   ← Tenant databases
        ├── acme.db                ← Created when "acme" tenant registers
        ├── globex.db
        └── ...
```

### .gitignore Rules

The `data/` directory is excluded from version control:

```gitignore
# SQLite databases (auto-created on run)
src/data/

# Except keep the directory structure hint
!src/data/.gitkeep
```

### Resetting Local Data

To start completely fresh:

```bash
# Delete all local databases
rm -rf src/data/

# Run again — everything is recreated and re-seeded
dotnet run --project src
```

---

## 8. Local Workflows

### Super Admin Login

1. Navigate to `https://localhost:5001/super-admin/login`
2. Enter `admin@localhost` (the seeded super admin email)
3. Click "Send Magic Link"
4. **Check the terminal** — the `ConsoleEmailService` logs the magic link URL directly:
   ```
   ★ MAGIC LINK for admin@localhost: https://localhost:5001/super-admin/verify?token=abc123def456
   ```
5. Copy the URL from the terminal and paste into the browser
6. You're logged in as super admin

### Creating a Test Tenant

1. Log in as super admin (above)
2. Navigate to Tenants → Create New Tenant, or
3. Use the public registration flow at `https://localhost:5001/register`
4. Fill in: Organisation Name, Email, Slug (e.g., `test`)
5. Select any plan — `MockBillingService` auto-approves all payments
6. The tenant database `data/tenants/test.db` is created automatically
7. **Check the terminal** for the welcome magic link
8. Access the tenant at `https://localhost:5001/test/`

### Tenant User Login

1. Navigate to `https://localhost:5001/{slug}/login` (e.g., `/test/login`)
2. Enter the admin email used during registration
3. Check the terminal for the magic link
4. Click the magic link URL — logged in as tenant admin

### Testing Plan Upgrades/Downgrades

With `MockBillingService`, plan changes are instant:
- Go to `/{slug}/billing`
- Click "Change Plan"
- Select a new plan
- Mock service auto-approves — no payment page redirect

---

## 9. Running Tests

```bash
# Run all tests
dotnet test

# Run with verbose output
dotnet test --verbosity normal

# Run a specific test class
dotnet test --filter "FullyQualifiedName~NotesControllerTests"

# Run with coverage (if coverlet is configured)
dotnet test --collect:"XPlat Code Coverage"
```

Tests use an in-memory or temporary SQLite database. No local data directory is needed for test execution.

---

## 10. Client-Side Libraries

Client-side packages are managed by **LibMan** (no npm/node required):

| Package | Version | Destination |
|---------|---------|-------------|
| htmx.org | 2.0.8 | `wwwroot/lib/htmx/` |
| @tailwindcss/browser | 4.1.18 | `wwwroot/lib/tailwindcss/` |
| daisyui | 5.5.14 | `wwwroot/lib/daisyui/` |

### Restoring Client Libraries

```bash
cd src
libman restore
```

### Updating a Library Version

Edit `src/libman.json`, change the version number, then:

```bash
cd src
libman restore --force
```

### How Tailwind Works Locally

Tailwind CSS v4 runs **in the browser** during development via the `@tailwindcss/browser` package. No build step, no CLI, no PostCSS config.

In `_Layout.cshtml`:
```html
<!-- Development: browser-runtime Tailwind -->
<environment include="Development">
    <script src="~/lib/tailwindcss/dist/index.global.min.js"></script>
</environment>

<!-- Production: pre-built CSS via WebOptimizer -->
<environment exclude="Development">
    <link rel="stylesheet" href="~/css/theme.min.css" />
</environment>
```

This means:
- **Development**: Tailwind compiles in-browser (instant, no build step)
- **Production**: CSS is pre-built and minified by WebOptimizer at publish time

---

## 11. Docker Compose (Local Production Testing)

To test the full production-like setup locally:

### docker-compose.override.yml

```yaml
# docker-compose.override.yml (local overrides — not committed)
services:
  app:
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - Billing__Provider=Mock
      - Email__Provider=Console
      - Turnstile__Provider=Mock
      - FeatureFlags__AllEnabledLocally=true
    ports:
      - "8080:8080"
```

```bash
# Build and run
docker compose up --build

# Access at http://localhost:8080
```

This runs the containerised version but still uses mock services. To test with real Paystack/SES, create a `.env` file with real credentials and set `ASPNETCORE_ENVIRONMENT=Production`.

---

## 12. Common Tasks Cheat Sheet

| Task | Command |
|------|---------|
| Run the app | `dotnet run --project src` |
| Run tests | `dotnet test` |
| Restore client libs | `cd src && libman restore` |
| Reset all data | `rm -rf src/data/` |
| Add a NuGet package | `dotnet add src/saas.csproj package <PackageName>` |
| Add an EF migration (Core) | `dotnet ef migrations add <Name> --project src --context CoreDbContext` |
| Add an EF migration (Tenant) | `dotnet ef migrations add <Name> --project src --context TenantDbContext` |
| Apply migrations | Automatic on startup (Development) |
| Build Docker image | `docker compose build` |
| Run in Docker | `docker compose up` |
| View SQLite DB | Open `.db` file in VS Code with SQLite Viewer extension |
| Trust HTTPS cert | `dotnet dev-certs https --trust` |

---

## 13. Environment Variable Reference

All configuration can be overridden via environment variables using the `__` (double underscore) separator:

| Config Path | Environment Variable | Local Default |
|-------------|---------------------|---------------|
| `ConnectionStrings:CoreDatabase` | `ConnectionStrings__CoreDatabase` | `Data Source=data/core.db` |
| `ConnectionStrings:AuditDatabase` | `ConnectionStrings__AuditDatabase` | `Data Source=data/audit.db` |
| `TenantDatabasePath` | `TenantDatabasePath` | `data/tenants` |
| `Auth:SuperAdmin:Email` | `Auth__SuperAdmin__Email` | `admin@localhost` |
| `Auth:MagicLink:TokenExpiryMinutes` | `Auth__MagicLink__TokenExpiryMinutes` | `15` |
| `Billing:Provider` | `Billing__Provider` | `Mock` |
| `Email:Provider` | `Email__Provider` | `Console` |
| `Turnstile:Provider` | `Turnstile__Provider` | `Mock` |
| `FeatureFlags:AllEnabledLocally` | `FeatureFlags__AllEnabledLocally` | `true` |

---

## 14. Troubleshooting

### "Database is locked" errors

SQLite WAL mode is set automatically by `WalModeInterceptor` (see [02 — Database](02-database-multitenancy.md) §6). If you still see locking errors:
- Ensure no other process has the `.db` file open (close SQLite viewers)
- Delete the `-wal` and `-shm` files and restart

### Magic link not appearing in terminal

- Verify `Email:Provider` is `Console` (check terminal output at startup for `[MOCK EMAIL]` or `ConsoleEmailService` registration)
- Check that the email address matches exactly (case-insensitive)
- Look for the `★ MAGIC LINK` marker in the terminal output

### Client-side styles not loading

- Run `libman restore` in the `src/` directory
- Verify `wwwroot/lib/tailwindcss/dist/index.global.min.js` exists
- Check browser console for script loading errors
- Ensure `_Layout.cshtml` includes the browser Tailwind script in Development environment

### Port already in use

- Change the port in `Properties/launchSettings.json`, or
- Kill the existing process: `lsof -ti:5001 | xargs kill` (macOS/Linux) or find the process in Task Manager (Windows)

### Tenant database not created

- Verify `data/tenants/` directory exists (auto-created on first run)
- Check that the slug is valid (lowercase, alphanumeric + hyphens)
- Look for provisioning errors in the terminal output

---

## 15. Startup Checklist (Quick Reference)

```
□ .NET 10 SDK installed              → dotnet --version
□ Repo cloned                        → git clone ... && cd saas
□ NuGet packages restored            → dotnet restore
□ Client libraries restored          → cd src && libman restore
□ Dev cert trusted (optional)        → dotnet dev-certs https --trust
□ Run the app                        → dotnet run --project src
□ Open browser                       → https://localhost:5001
□ Super admin login                  → /super-admin/login → admin@localhost
□ Check terminal for magic link      → ★ MAGIC LINK for admin@localhost: ...
□ Create a test tenant               → /register → fill form → auto-approved
□ Access tenant                      → /{slug}/ → check terminal for magic link
```

---

## Next Steps

→ [09 — Marketing Module](09-marketing.md) for the public-facing marketing site, pricing page, and registration entry point.
