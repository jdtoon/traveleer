using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;

namespace saas.Infrastructure;

public static class CachingExtensions
{
    public static IServiceCollection AddCachingConfig(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration.GetValue<string>("Caching:Provider") ?? "Memory";

        if (provider.Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = configuration.GetValue<string>("Caching:Redis:ConnectionString")
                ?? "localhost:6379";

            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = connectionString;
                options.InstanceName = configuration.GetValue<string>("Caching:Redis:InstanceName") ?? "saas:";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        // Always register IMemoryCache for in-process caching (rate limiter, etc.)
        var sizeLimit = configuration.GetValue<long?>("Caching:MemoryCacheSizeLimit");
        services.AddMemoryCache(options =>
        {
            if (sizeLimit.HasValue)
                options.SizeLimit = sizeLimit.Value;
        });

        // Register the cache service abstraction
        services.AddSingleton<ICacheService, DistributedCacheService>();

        return services;
    }
}

/// <summary>
/// High-level caching abstraction over IDistributedCache with typed get/set.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default) where T : class;
    Task RemoveAsync(string key, CancellationToken ct = default);
}

public class DistributedCacheService : ICacheService
{
    private readonly IDistributedCache _cache;
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(5);

    public DistributedCacheService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        var json = await _cache.GetStringAsync(key, ct);
        if (json is null) return null;
        return System.Text.Json.JsonSerializer.Deserialize<T>(json);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default) where T : class
    {
        var json = System.Text.Json.JsonSerializer.Serialize(value);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration ?? DefaultExpiration
        };
        await _cache.SetStringAsync(key, json, options, ct);
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        await _cache.RemoveAsync(key, ct);
    }
}
