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

## Starting a New Project

```bash
# 1. Clone and set up remotes
git clone https://github.com/you/saas-starter.git my-new-project
cd my-new-project
git remote rename origin upstream
git remote add origin https://github.com/you/my-new-project.git
git push -u origin main

# 2. Delete the example module
rm -rf src/Modules/Notes

# 3. Remove the Notes line from Program.cs (in the "App modules" section)

# 4. Create your own modules in src/Modules/YourModule/
#    Use the Notes module as a template for the IModule pattern

# 5. Register your modules in Program.cs under "App modules"
```

### Pulling Framework Updates

```bash
git fetch upstream
git merge upstream/main
# Framework modules merge cleanly — only app modules may need conflict resolution
```

The `.gitattributes` file configures merge strategies so `Program.cs` uses union merge and app modules (`Modules/Notes/`) use "ours" — your customizations won't be overwritten by upstream.
