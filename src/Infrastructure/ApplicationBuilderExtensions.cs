using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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
            await SyncPermissionsToTenantsAsync(modules, logger);

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
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "db", "tenants");
        if (!Directory.Exists(basePath))
            return;

        var dbFiles = Directory.GetFiles(basePath, "*.db");
        foreach (var dbFile in dbFiles)
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
    }

    /// <summary>
    /// Sync module-defined permissions to all existing tenant databases.
    /// Adds any new permissions that don't exist yet (idempotent by Key).
    /// </summary>
    private static async Task SyncPermissionsToTenantsAsync(IReadOnlyList<IModule> modules, ILogger logger)
    {
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "db", "tenants");
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
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to sync permissions to tenant DB {DbFile}", Path.GetFileName(dbFile));
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

        app.UseHttpsRedirection();
        app.UseWebOptimizer();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseRateLimiter(); // After tenant resolution so "tenant" policy can access ITenantContext
        app.UseAuthentication();
        app.UseSwapHtmx();
        app.UseAuthorization();
        app.UseMiddleware<CurrentUserMiddleware>();

        return app;
    }

    public static WebApplication MapEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
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
        }).AddEndpointFilter(async (context, next) =>
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
        });

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
}
