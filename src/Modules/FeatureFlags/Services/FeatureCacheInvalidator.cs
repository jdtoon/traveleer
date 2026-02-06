using Microsoft.Extensions.Caching.Memory;

namespace saas.Modules.FeatureFlags.Services;

/// <summary>
/// Clears the in-memory feature caches when super admin changes feature settings.
/// Inject this into super admin controllers that modify features, plan assignments, or overrides.
/// </summary>
public class FeatureCacheInvalidator
{
    private readonly IMemoryCache _cache;

    public FeatureCacheInvalidator(IMemoryCache cache)
    {
        _cache = cache;
    }

    public void Invalidate()
    {
        _cache.Remove("feature-definitions");
        // Tenant-specific caches ("tenant-overrides-{id}", "tenant-plan-{id}") will
        // expire naturally within 5-10 minutes. For immediate invalidation of a
        // specific tenant, call InvalidateTenant(tenantId).
    }

    public void InvalidateTenant(Guid tenantId)
    {
        _cache.Remove($"tenant-overrides-{tenantId}");
        _cache.Remove($"tenant-plan-{tenantId}");
    }
}
