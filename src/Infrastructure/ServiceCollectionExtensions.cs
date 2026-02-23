using System.IO.Compression;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using saas.Data.Core;
using saas.Data.Audit;
using saas.Data.Tenant;
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
        services.AddDbContext<CoreDbContext>((serviceProvider, options) =>
        {
            options.UseSqlite(
                configuration.GetConnectionString("CoreDatabase") ?? $"Data Source={Path.Combine(dataPath, "core.db")}",
                sql => sql.MigrationsAssembly(typeof(CoreDbContext).Assembly.FullName)
            ).AddInterceptors(walInterceptor);

            var auditInterceptor = serviceProvider.GetService<AuditSaveChangesInterceptor>();
            if (auditInterceptor is not null)
                options.AddInterceptors(auditInterceptor);
        });

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
        if (emailProvider.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IEmailService, SmtpEmailService>();
        }
        else if (emailProvider.Equals("MailerSend", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<MailerSendEmailService>(client =>
            {
                var token = configuration.GetValue<string>("Email:MailerSend:ApiToken") ?? string.Empty;
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            });
            services.AddScoped<IEmailService>(sp => sp.GetRequiredService<MailerSendEmailService>());
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

    public static IServiceCollection AddRateLimitingConfig(this IServiceCollection services, IConfiguration configuration)
    {
        var rateLimitSection = configuration.GetSection("RateLimiting");

        services.AddRateLimiter(options =>
        {
            var globalLimit = rateLimitSection.GetValue("GlobalPerMinute", 100);
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = globalLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            var strictLimit = rateLimitSection.GetValue("StrictPerMinute", 5);
            options.AddPolicy("strict", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = strictLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            var regLimit = rateLimitSection.GetValue("RegistrationPerWindow", 3);
            var regWindow = rateLimitSection.GetValue("RegistrationWindowMinutes", 5);
            options.AddPolicy("registration", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = regLimit,
                        Window = TimeSpan.FromMinutes(regWindow),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            var contactLimit = rateLimitSection.GetValue("ContactPerWindow", 3);
            var contactWindow = rateLimitSection.GetValue("ContactWindowMinutes", 5);
            options.AddPolicy("contact", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = contactLimit,
                        Window = TimeSpan.FromMinutes(contactWindow),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            var webhookLimit = rateLimitSection.GetValue("WebhookPerMinute", 50);
            options.AddPolicy("webhook", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = webhookLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // Tenant-aware rate limiting — partitions by tenant slug, limits per plan
            options.AddPolicy("tenant", httpContext =>
            {
                var tenantContext = httpContext.RequestServices.GetService<ITenantContext>();
                if (tenantContext?.IsTenantRequest != true || tenantContext.Slug is null)
                {
                    // Not a tenant request — fall back to permissive global limit
                    return RateLimitPartition.GetNoLimiter("non-tenant");
                }

                // Cache lookup: TenantResolutionMiddleware already cached the tenant/plan
                // Use PlanSlug as partition key so all same-plan tenants share the same config
                var partitionKey = $"tenant-{tenantContext.Slug}";
                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
                {
                    // Resolve max requests from cached plan data
                    var cache = httpContext.RequestServices.GetService<IMemoryCache>();
                    var config = httpContext.RequestServices.GetService<IConfiguration>();
                    var maxRequests = 60; // default
                    if (cache is not null)
                    {
                        var cacheKey = $"plan-rate-limit-{tenantContext.PlanSlug}";
                        if (!cache.TryGetValue(cacheKey, out int cachedLimit))
                        {
                            // Look up the plan's limit from DB, cache it
                            using var scope = httpContext.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateScope();
                            var coreDb = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
                            var plan = coreDb.Plans.AsNoTracking()
                                .FirstOrDefault(p => p.Slug == tenantContext.PlanSlug);
                            cachedLimit = plan?.MaxRequestsPerMinute ?? 60;
                            var ttlMinutes = config?.GetValue<int?>("Caching:TTL:RateLimitPlanMinutes") ?? 5;
                            var hasSizeLimit = config?.GetValue<long?>("Caching:MemoryCacheSizeLimit").HasValue == true;
                            var entryOptions = new MemoryCacheEntryOptions
                            {
                                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(ttlMinutes)
                            };
                            if (hasSizeLimit) entryOptions.Size = 1;
                            cache.Set(cacheKey, cachedLimit, entryOptions);
                        }
                        maxRequests = cachedLimit;
                    }

                    return new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = maxRequests,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    };
                });
            });

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, token) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue)
                    ? retryAfterValue.TotalSeconds.ToString("F0")
                    : "60";

                context.HttpContext.Response.Headers.RetryAfter = retryAfter;

                // If the request comes from htmx, return a toast snippet instead of a full page
                if (context.HttpContext.Request.Headers.ContainsKey("HX-Request"))
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.HttpContext.Response.ContentType = "text/html; charset=utf-8";
                    context.HttpContext.Response.Headers["HX-Retarget"] = "#toast-container";
                    context.HttpContext.Response.Headers["HX-Reswap"] = "beforeend";
                    await context.HttpContext.Response.WriteAsync(ErrorPages.Get429Toast(), token);
                    return;
                }

                await ErrorPages.Write429Async(context.HttpContext.Response);
            };
        });

        return services;
    }

    public static IServiceCollection AddStorageConfig(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .BindConfiguration(StorageOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var provider = configuration.GetValue<string>("Storage:Provider") ?? "Local";

        if (provider.Equals("R2", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IStorageService, R2StorageService>();
        }
        else
        {
            services.AddSingleton<IStorageService, LocalStorageService>();
        }

        return services;
    }
}
