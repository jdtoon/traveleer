using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using saas.Data.Core;
using saas.Data.Audit;
using saas.Data.Seeding;
using saas.Infrastructure.Middleware;
using saas.Data.Tenant;
using Swap.Htmx;

namespace saas.Infrastructure;

public static class ApplicationBuilderExtensions
{
    private static readonly SemaphoreSlim InitLock = new(1, 1);
    private static bool _initialized;

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

            // Seed master data
            await MasterDataSeeder.SeedAsync(coreDb, app.Configuration);

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
        app.UseRateLimiter();
        app.UseMiddleware<TenantResolutionMiddleware>();
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
        });

        app.MapControllerRoute(
            name: "tenant",
            pattern: "{slug}/{controller=Home}/{action=Index}/{id?}");

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");

        return app;
    }
}
