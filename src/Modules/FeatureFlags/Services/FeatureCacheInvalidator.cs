using Microsoft.Extensions.Caching.Memory;

namespace saas.Modules.FeatureFlags.Services;

/// <summary>
/// Clears the in-memory feature caches when super admin changes feature settings.
/// Inject this into super admin controllers that modify features, plan assignments, or overrides.
/// </summary>
public class FeatureCacheInvalidator
{
    private readonly IMemoryCache _cache;
    private static long _generation;

    /// <summary>
    /// Current cache generation. Included in tenant cache keys so that a global
    /// invalidation immediately orphans all old entries.
    /// </summary>
    public static long Generation => Volatile.Read(ref _generation);

    public FeatureCacheInvalidator(IMemoryCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// Invalidate all feature caches globally (feature definitions + all tenant caches).
    /// Increments the generation counter so all tenant-specific cache keys become stale.
    /// </summary>
    public void Invalidate()
    {
        _cache.Remove("feature-definitions");
        Interlocked.Increment(ref _generation);
    }

    /// <summary>
    /// Invalidate caches for a specific tenant (overrides + plan).
    /// </summary>
    public void InvalidateTenant(Guid tenantId)
    {
        var gen = Generation;
        _cache.Remove($"tenant-overrides-{tenantId}-{gen}");
        _cache.Remove($"tenant-plan-{tenantId}-{gen}");
        // Also remove old-generation keys in case they haven't expired yet
        if (gen > 0)
        {
            _cache.Remove($"tenant-overrides-{tenantId}-{gen - 1}");
            _cache.Remove($"tenant-plan-{tenantId}-{gen - 1}");
        }
    }
}
