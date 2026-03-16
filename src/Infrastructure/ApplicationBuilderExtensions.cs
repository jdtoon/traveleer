using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using saas.Data.Core;
using saas.Data.Audit;
using saas.Infrastructure.Middleware;
using saas.Data.Tenant;
using saas.Shared;
using saas.Infrastructure.Services;
using Swap.Htmx;

namespace saas.Infrastructure;

public static class ApplicationBuilderExtensions
{
    private static readonly SemaphoreSlim InitLock = new(1, 1);
    private static bool _initialized;

    public static async Task RestoreFromBackupIfNeededAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var restoreService = scope.ServiceProvider.GetService<ILitestreamRestoreService>();
        if (restoreService is not null)
            await restoreService.RestoreIfNeededAsync();
    }

    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        await InitLock.WaitAsync();

        try
        {
            if (_initialized)
                return;

            using var scope = app.Services.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMigration");

            // Initialize CoreDbContext
            var coreDb = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            await ApplyMigrationsAsync(coreDb, logger, "core");

            // Initialize AuditDbContext
            var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            await ApplyMigrationsAsync(auditDb, logger, "audit");

            // Initialize TenantDbContext for each existing tenant DB
            await ApplyTenantMigrationsAsync(scope.ServiceProvider, logger);

            // Seed core data (features collected from modules + plans + super admin)
            var modules = scope.ServiceProvider.GetRequiredService<IReadOnlyList<IModule>>();
            await CoreDataSeeder.SeedAsync(coreDb, app.Configuration, modules);

            // Sync permissions to all existing tenant DBs (idempotent)
            await SyncPermissionsToTenantsAsync(scope.ServiceProvider, modules, logger);

            // Backfill module-owned tenant defaults for existing tenant DBs.
            await SeedTenantModulesAsync(scope.ServiceProvider, modules, logger);

            // Dev seeding — only when explicitly enabled in config
            var devSeedOptions = app.Configuration.GetSection(DevSeedOptions.SectionName).Get<DevSeedOptions>();
            if (devSeedOptions?.Enabled == true)
            {
                await DevDataSeeder.SeedAsync(app.Services, devSeedOptions, modules);
            }

            _initialized = true;
        }
        finally
        {
            InitLock.Release();
        }
    }

    private static async Task ApplyMigrationsAsync(DbContext db, ILogger logger, string name)
    {
        await EnsureHistoryTableAndBaselineAsync(db, logger, name);

        var pending = await db.Database.GetPendingMigrationsAsync();
        var pendingList = pending.ToList();

        if (pendingList.Count == 0)
        {
            logger.LogInformation("{DbName} database up to date (no pending migrations)", name);
            return;
        }

        logger.LogInformation("Applying {Count} pending migrations to {DbName} database", pendingList.Count, name);
        await db.Database.MigrateAsync();
    }

    private static async Task ApplyTenantMigrationsAsync(IServiceProvider services, ILogger logger)
    {
        var environment = services.GetRequiredService<IWebHostEnvironment>();
        var basePath = Path.Combine(environment.ContentRootPath, "db", "tenants");
        if (!Directory.Exists(basePath))
            return;

        var dbFiles = Directory.GetFiles(basePath, "*.db");
        var failedTenants = new List<string>();

        foreach (var dbFile in dbFiles)
        {
            try
            {
                var options = new DbContextOptionsBuilder<TenantDbContext>()
                    .UseSqlite($"Data Source={dbFile}")
                    .Options;

                await using var tenantDb = new TenantDbContext(options);
                await EnsureHistoryTableAndBaselineAsync(tenantDb, logger, $"tenant:{Path.GetFileName(dbFile)}");
                var pending = await tenantDb.Database.GetPendingMigrationsAsync();
                var pendingList = pending.ToList();

                if (pendingList.Count == 0)
                {
                    logger.LogInformation("Tenant DB {DbFile} up to date (no pending migrations)", Path.GetFileName(dbFile));
                    continue;
                }

                logger.LogInformation("Applying {Count} pending migrations to tenant DB {DbFile}", pendingList.Count, Path.GetFileName(dbFile));
                await tenantDb.Database.MigrateAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to migrate tenant database: {DbFile}", Path.GetFileName(dbFile));
                failedTenants.Add(Path.GetFileName(dbFile));
            }
        }

        if (failedTenants.Count > 0)
            logger.LogWarning("Tenant migration completed with {Count} failure(s): {Failed}",
                failedTenants.Count, string.Join(", ", failedTenants));
    }

    /// <summary>
    /// Sync module-defined permissions to all existing tenant databases.
    /// Adds any new permissions that don't exist yet (idempotent by Key)
    /// and ensures role-permission mappings match module defaults for system roles.
    /// </summary>
    private static async Task SyncPermissionsToTenantsAsync(IServiceProvider services, IReadOnlyList<IModule> modules, ILogger logger)
    {
        var environment = services.GetRequiredService<IWebHostEnvironment>();
        var basePath = Path.Combine(environment.ContentRootPath, "db", "tenants");
        if (!Directory.Exists(basePath))
            return;

        var modulePermissions = modules.SelectMany(m => m.Permissions).ToList();
        if (modulePermissions.Count == 0)
            return;

        var dbFiles = Directory.GetFiles(basePath, "*.db");
        foreach (var dbFile in dbFiles)
        {
            try
            {
                var options = new DbContextOptionsBuilder<TenantDbContext>()
                    .UseSqlite($"Data Source={dbFile}")
                    .Options;

                await using var tenantDb = new TenantDbContext(options);

                // ── 1. Sync Permission rows ──────────────────────────────
                var existingKeys = await tenantDb.Permissions
                    .Select(p => p.Key)
                    .ToListAsync();

                var existingSet = new HashSet<string>(existingKeys, StringComparer.OrdinalIgnoreCase);
                var newPermissions = modulePermissions
                    .Where(mp => !existingSet.Contains(mp.Key))
                    .Select(mp => new saas.Modules.Auth.Entities.Permission
                    {
                        Id = Guid.NewGuid(),
                        Key = mp.Key,
                        Name = mp.Name,
                        Group = mp.Group,
                        SortOrder = mp.SortOrder,
                        Description = mp.Description
                    })
                    .ToList();

                if (newPermissions.Count > 0)
                {
                    tenantDb.Permissions.AddRange(newPermissions);
                    await tenantDb.SaveChangesAsync();
                    logger.LogInformation(
                        "Synced {Count} new permissions to tenant DB {DbFile}",
                        newPermissions.Count, Path.GetFileName(dbFile));
                }

                // ── 2. Sync role-permission mappings (system roles only) ─
                var allPermissions = await tenantDb.Permissions.ToListAsync();
                var permissionByKey = allPermissions.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);

                var systemRoles = await tenantDb.Roles
                    .Where(r => r.IsSystemRole)
                    .ToListAsync();
                var roleByName = systemRoles.ToDictionary(r => r.Name!, StringComparer.OrdinalIgnoreCase);

                var existingRolePerms = await tenantDb.RolePermissions
                    .Select(rp => new { rp.RoleId, rp.PermissionId })
                    .ToListAsync();
                var existingRolePermSet = new HashSet<(string RoleId, Guid PermissionId)>(
                    existingRolePerms.Select(rp => (rp.RoleId, rp.PermissionId)));

                var pending = new HashSet<(string RoleId, Guid PermissionId)>(existingRolePermSet);

                // Admin system role gets every permission
                if (roleByName.TryGetValue("Admin", out var adminRole))
                {
                    foreach (var perm in allPermissions)
                        pending.Add((adminRole.Id, perm.Id));
                }

                // Module-declared mappings for other system roles (e.g. Member)
                var roleMappings = modules.SelectMany(m => m.DefaultRolePermissions).ToList();
                foreach (var mapping in roleMappings)
                {
                    if (roleByName.TryGetValue(mapping.RoleName, out var role) &&
                        permissionByKey.TryGetValue(mapping.PermissionKey, out var perm))
                    {
                        pending.Add((role.Id, perm.Id));
                    }
                }

                // Only insert rows that don't already exist
                var toInsert = pending.Except(existingRolePermSet)
                    .Select(x => new saas.Modules.Auth.Entities.RolePermission
                    {
                        RoleId = x.RoleId,
                        PermissionId = x.PermissionId
                    })
                    .ToList();

                if (toInsert.Count > 0)
                {
                    tenantDb.RolePermissions.AddRange(toInsert);
                    await tenantDb.SaveChangesAsync();
                    logger.LogInformation(
                        "Synced {Count} role-permission mappings to tenant DB {DbFile}",
                        toInsert.Count, Path.GetFileName(dbFile));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to sync permissions to tenant DB {DbFile}", Path.GetFileName(dbFile));
            }
        }
    }

    private static async Task SeedTenantModulesAsync(IServiceProvider services, IReadOnlyList<IModule> modules, ILogger logger)
    {
        var environment = services.GetRequiredService<IWebHostEnvironment>();
        var basePath = Path.Combine(environment.ContentRootPath, "db", "tenants");
        if (!Directory.Exists(basePath))
            return;

        foreach (var dbFile in Directory.GetFiles(basePath, "*.db"))
        {
            try
            {
                using var scope = services.CreateScope();
                var tenantDb = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
                tenantDb.Database.SetConnectionString($"Data Source={dbFile}");

                foreach (var module in modules)
                {
                    await module.SeedTenantAsync(scope.ServiceProvider);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to seed tenant defaults for DB {DbFile}", Path.GetFileName(dbFile));
            }
        }
    }

    private static async Task EnsureHistoryTableAndBaselineAsync(DbContext db, ILogger logger, string name)
    {
        var history = db.GetService<IHistoryRepository>();
        var creator = db.GetService<IRelationalDatabaseCreator>();

        if (!await creator.ExistsAsync())
            return;

        var hasTables = await DatabaseHasUserTables(db);
        if (!hasTables)
            return;

        var applied = await db.Database.GetAppliedMigrationsAsync();
        var appliedList = applied.ToList();

        if (history.Exists() && appliedList.Count > 0)
            return;

        logger.LogInformation("{DbName} database exists without migrations history. Baseline current schema.", name);

        if (!history.Exists())
        {
            var createScript = history.GetCreateScript();
            if (!string.IsNullOrWhiteSpace(createScript))
                await db.Database.ExecuteSqlRawAsync(createScript);
        }

        var migrations = db.GetService<IMigrationsAssembly>().Migrations.Keys;
        var productVersion = typeof(DbContext).Assembly.GetName().Version?.ToString() ?? "10.0.0";

        foreach (var migrationId in migrations)
        {
            var insertScript = history.GetInsertScript(new HistoryRow(migrationId, productVersion));
            await db.Database.ExecuteSqlRawAsync(insertScript);
        }
    }

    private static async Task<bool> DatabaseHasUserTables(DbContext db)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE '__EFMigrations%' LIMIT 1;";
        var result = await command.ExecuteScalarAsync();
        return result is not null && result is not DBNull;
    }

    public static WebApplication ConfigurePipeline(this WebApplication app)
    {
        app.UseResponseCompression();
        app.UseForwardedHeaders();
        app.UseSerilogRequestLogging();
        app.UseMiddleware<SecurityHeadersMiddleware>();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}");

        app.UseHttpsRedirection();
        app.UseWebOptimizer();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseMiddleware<FeatureFlagMiddleware>();
        app.UseRateLimiter(); // After tenant resolution so "tenant" policy can access ITenantContext
        app.UseAuthentication();
        app.UseSwapHtmx();
        app.UseAuthorization();
        app.UseMiddleware<CurrentUserMiddleware>();

        return app;
    }

    public static WebApplication MapEndpoints(this WebApplication app)
    {
        // Liveness endpoint — excludes infrastructure checks (Redis, RabbitMQ, Seq, etc.)
        // Used by Docker HEALTHCHECK and basic uptime monitors
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = check => !check.Tags.Contains("infrastructure"),
            ResponseWriter = WriteHealthResponse
        }).AddEndpointFilter(HealthCheckIpFilter);

        // Full health endpoint — includes all checks including infrastructure
        // Used by the Super Admin health dashboard
        app.MapHealthChecks("/health/full", new HealthCheckOptions
        {
            ResponseWriter = WriteHealthResponse
        }).AddEndpointFilter(HealthCheckIpFilter);

        app.MapControllerRoute(
            name: "marketing-root",
            pattern: "",
            defaults: new { controller = "Marketing", action = "Index" });

        app.MapControllerRoute(
            name: "marketing-pricing",
            pattern: "pricing",
            defaults: new { controller = "Marketing", action = "Pricing" });

        app.MapControllerRoute(
            name: "marketing-about",
            pattern: "about",
            defaults: new { controller = "Marketing", action = "About" });

        app.MapControllerRoute(
            name: "marketing-contact",
            pattern: "contact",
            defaults: new { controller = "Marketing", action = "Contact" });

        app.MapControllerRoute(
            name: "marketing-terms",
            pattern: "legal/terms",
            defaults: new { controller = "Marketing", action = "Terms" });

        app.MapControllerRoute(
            name: "marketing-privacy",
            pattern: "legal/privacy",
            defaults: new { controller = "Marketing", action = "Privacy" });

        app.MapControllerRoute(
            name: "marketing-login-redirect",
            pattern: "login-redirect",
            defaults: new { controller = "Marketing", action = "LoginRedirect" });

        app.MapControllerRoute(
            name: "marketing-login-modal",
            pattern: "login-modal",
            defaults: new { controller = "Marketing", action = "LoginModal" });

        app.MapControllerRoute(
            name: "ui-modal-close",
            pattern: "ui/modal-close",
            defaults: new { controller = "Home", action = "ModalClose" });

        app.MapControllerRoute(
            name: "marketing-sitemap",
            pattern: "sitemap.xml",
            defaults: new { controller = "Marketing", action = "Sitemap" });

        app.MapControllerRoute(
            name: "marketing-robots",
            pattern: "robots.txt",
            defaults: new { controller = "Marketing", action = "Robots" });

        app.MapControllerRoute(
            name: "tenant",
            pattern: "{slug}/{controller=Dashboard}/{action=Index}/{id?}")
            .RequireRateLimiting("tenant");

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");

        return app;
    }

    private static async Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description
            })
        };
        await context.Response.WriteAsJsonAsync(result);
    }

    private static async ValueTask<object?> HealthCheckIpFilter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var allowedIPs = config["HealthCheck:AllowedIPs"];

        // Empty or missing = allow all (default behaviour)
        if (string.IsNullOrWhiteSpace(allowedIPs))
            return await next(context);

        var remoteIp = context.HttpContext.Connection.RemoteIpAddress?.ToString();
        var allowed = allowedIPs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (remoteIp is null || !allowed.Contains(remoteIp))
        {
            context.HttpContext.Response.StatusCode = 404;
            return Results.NotFound();
        }

        return await next(context);
    }
}
