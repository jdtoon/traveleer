using System.IO.Compression;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using saas.Data.Core;
using saas.Data.Audit;
using saas.Data.Tenant;
using Amazon.SimpleEmailV2;
using saas.Infrastructure.Middleware;
using saas.Infrastructure.Services;
using saas.Modules.Audit.Services;
using saas.Shared;

namespace saas.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataProtectionConfig(this IServiceCollection services, IWebHostEnvironment environment)
    {
        var keysPath = Path.Combine(environment.ContentRootPath, "db", "keys");
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
        var dataPath = Path.Combine(Directory.GetCurrentDirectory(), "db");
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
        services.AddDbContext<TenantDbContext>((serviceProvider, options) =>
        {
            var tenantContext = serviceProvider.GetRequiredService<ITenantContext>();
            if (tenantContext.IsTenantRequest && tenantContext.Slug is not null)
            {
                var tenantPath = configuration["Tenancy:DatabasePath"] ?? Path.Combine("db", "tenants");
                var basePath = Path.IsPathRooted(tenantPath)
                    ? tenantPath
                    : Path.Combine(Directory.GetCurrentDirectory(), tenantPath);
                var dbPath = Path.Combine(basePath, $"{tenantContext.Slug}.db");
                options.UseSqlite($"Data Source={dbPath}");
            }
            else
            {
                options.UseSqlite("Data Source=:memory:");
            }

            options.AddInterceptors(walInterceptor);

            // Audit interceptor — captures changes and enqueues to background writer.
            // Registered as singleton; resolves scoped ITenantContext/ICurrentUser at call time.
            var auditInterceptor = serviceProvider.GetService<AuditSaveChangesInterceptor>();
            if (auditInterceptor is not null)
                options.AddInterceptors(auditInterceptor);
        });

        return services;
    }

    public static IServiceCollection AddCoreServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, TenantContext>();

        // Site settings
        services.AddOptions<SiteSettings>()
            .BindConfiguration(SiteSettings.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<EmailOptions>()
            .BindConfiguration(EmailOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<TurnstileOptions>()
            .BindConfiguration(TurnstileOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Provider switching for Email & BotProtection
        var emailProvider = configuration.GetValue<string>("Email:Provider") ?? "Console";
        if (emailProvider.Equals("SES", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IAmazonSimpleEmailServiceV2>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<EmailOptions>>().Value;
                var regionName = string.IsNullOrWhiteSpace(options.SesRegion)
                    ? "us-east-1"
                    : options.SesRegion;
                return new AmazonSimpleEmailServiceV2Client(
                    Amazon.RegionEndpoint.GetBySystemName(regionName));
            });
            services.AddScoped<IEmailService, SesEmailService>();
        }
        else
        {
            services.AddScoped<IEmailService, ConsoleEmailService>();
        }

        var turnstileProvider = configuration.GetValue<string>("Turnstile:Provider") ?? "Mock";
        if (turnstileProvider.Equals("Cloudflare", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<TurnstileBotProtection>();
            services.AddScoped<IBotProtection, TurnstileBotProtection>();
        }
        else
        {
            services.AddScoped<IBotProtection, MockBotProtection>();
        }

        services.AddAuthentication();
        services.AddAuthorization();

        return services;
    }

    public static IServiceCollection AddForwardedHeadersConfig(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return services;
    }

    public static IServiceCollection AddRateLimitingConfig(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy("strict", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy("registration", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 3,
                        Window = TimeSpan.FromMinutes(5),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy("contact", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 3,
                        Window = TimeSpan.FromMinutes(5),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy("webhook", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 50,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.ContentType = "text/html";
                await context.HttpContext.Response.WriteAsync(
                    "<h1>Too Many Requests</h1><p>Please slow down and try again shortly.</p>",
                    token);
            };
        });

        return services;
    }
}
