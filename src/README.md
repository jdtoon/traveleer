# src/

Application entry point and root of the SaaS platform.

## Running

```bash
dotnet run                          # Development (uses appsettings.Development.json)
dotnet run --environment Production # Production mode
```

## Folder Structure

| Folder | Purpose |
|--------|---------|
| `Controllers/` | Minimal framework-level controllers (Home/Error) |
| `Data/` | EF Core DbContexts, entity configurations, seeders, migrations |
| `Infrastructure/` | Middleware, service registration, provider switching, health checks |
| `Modules/` | Self-contained vertical-slice feature modules |
| `Shared/` | Interfaces, options, and contracts that modules depend on |
| `Views/` | Shared Razor layouts and framework-level views |
| `wwwroot/` | Static assets (CSS, JS, error pages) |
| `db/` | SQLite databases, keys, tenant DBs (gitignored at runtime) |

## Configuration

Settings flow: `appsettings.json` → `appsettings.{Environment}.json` → environment variables → user secrets.

Provider switching (Email, Billing, Turnstile, Storage) is config-driven — see `Infrastructure/ServiceCollectionExtensions.cs`.
