using System.IO.Compression;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Data.Audit;
using saas.Data.Tenant;

namespace saas.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataProtectionConfig(this IServiceCollection services, IWebHostEnvironment environment)
    {
        var keysPath = Path.Combine(environment.ContentRootPath, "data", "keys");
        Directory.CreateDirectory(keysPath);

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
            .SetApplicationName("saas");

        return services;
    }

    public static IServiceCollection AddCompressionConfig(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });

        services.Configure<BrotliCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Fastest;
        });

        services.Configure<GzipCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Fastest;
        });

        return services;
    }

    public static IServiceCollection AddDatabaseConfig(this IServiceCollection services, IConfiguration configuration)
    {
        // Ensure data directories exist
        var dataPath = Path.Combine(Directory.GetCurrentDirectory(), "data");
        Directory.CreateDirectory(dataPath);
        Directory.CreateDirectory(Path.Combine(dataPath, "tenants"));
        Directory.CreateDirectory(Path.Combine(dataPath, "keys"));

        var walInterceptor = new WalModeInterceptor();

        // CoreDbContext — fixed connection string
        services.AddDbContext<CoreDbContext>(options =>
            options.UseSqlite(
                configuration.GetConnectionString("CoreDatabase") ?? $"Data Source={Path.Combine(dataPath, "core.db")}",
                sql => sql.MigrationsAssembly(typeof(CoreDbContext).Assembly.FullName)
            ).AddInterceptors(walInterceptor));

        // AuditDbContext — fixed connection string
        services.AddDbContext<AuditDbContext>(options =>
            options.UseSqlite(
                configuration.GetConnectionString("AuditDatabase") ?? $"Data Source={Path.Combine(dataPath, "audit.db")}",
                sql => sql.MigrationsAssembly(typeof(AuditDbContext).Assembly.FullName)
            ).AddInterceptors(walInterceptor));

        // TenantDbContext — registered but connection string set dynamically (Phase 2)
        // For now, register with a placeholder that won't be used until tenant resolution is added
        services.AddDbContext<TenantDbContext>(options =>
            options.UseSqlite("Data Source=:memory:")
                   .AddInterceptors(walInterceptor));

        return services;
    }

    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        return services;
    }
}
