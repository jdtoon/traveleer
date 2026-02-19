using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.FeatureManagement;
using saas.Data.Core;
using saas.Shared;

namespace saas.Modules.FeatureFlags.Services;

[FilterAlias("TenantPlan")]
public class TenantPlanFeatureFilter : IFeatureFilter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeSpan _overridesTtl;
    private readonly TimeSpan _planTtl;
    private readonly bool _hasSizeLimit;

    public TenantPlanFeatureFilter(
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _httpContextAccessor = httpContextAccessor;
        _overridesTtl = TimeSpan.FromMinutes(
            configuration.GetValue<int?>("Caching:TTL:TenantOverridesMinutes") ?? 5);
        _planTtl = TimeSpan.FromMinutes(
            configuration.GetValue<int?>("Caching:TTL:TenantPlanMinutes") ?? 10);
        _hasSizeLimit = configuration.GetValue<long?>("Caching:MemoryCacheSizeLimit").HasValue;
    }

    public async Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context)
    {
        // Resolve scoped ITenantContext from the current HTTP context
        var tenantContext = _httpContextAccessor.HttpContext?.RequestServices.GetService<ITenantContext>();

        // Non-tenant requests (marketing pages, super admin) — features are available
        if (tenantContext is null || !tenantContext.IsTenantRequest || tenantContext.TenantId is null)
            return true;

        var featureId = Guid.Parse(context.Parameters["FeatureId"] ?? string.Empty);
        var planIdsRaw = context.Parameters["PlanIds"] ?? string.Empty;
        var planIds = planIdsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(Guid.Parse)
            .ToHashSet();

        var tenantId = tenantContext.TenantId!.Value;

        // 1. Check per-tenant override first
        var overrideResult = await CheckTenantOverrideAsync(tenantId, featureId);
        if (overrideResult.HasValue)
            return overrideResult.Value;

        // 2. Check if tenant's plan includes this feature
        var tenantPlanId = await GetTenantPlanIdAsync(tenantId);
        return tenantPlanId.HasValue && planIds.Contains(tenantPlanId.Value);
    }

    private async Task<bool?> CheckTenantOverrideAsync(Guid tenantId, Guid featureId)
    {
        var overrides = await GetTenantOverridesAsync(tenantId);
        var match = overrides.FirstOrDefault(o => o.FeatureId == featureId);

        if (match is null)
            return null; // No override — fall through to plan check

        // Check expiry
        if (match.ExpiresAt.HasValue && match.ExpiresAt.Value < DateTime.UtcNow)
            return null; // Expired override — ignore it

        return match.IsEnabled;
    }

    private async Task<List<TenantOverrideCacheEntry>> GetTenantOverridesAsync(Guid tenantId)
    {
        var gen = FeatureCacheInvalidator.Generation;
        var cacheKey = $"tenant-overrides-{tenantId}-{gen}";

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _overridesTtl;
            if (_hasSizeLimit) entry.Size = 1;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

            return await db.TenantFeatureOverrides
                .AsNoTracking()
                .Where(o => o.TenantId == tenantId)
                .Select(o => new TenantOverrideCacheEntry
                {
                    FeatureId = o.FeatureId,
                    IsEnabled = o.IsEnabled,
                    ExpiresAt = o.ExpiresAt
                })
                .ToListAsync();
        }) ?? [];
    }

    private async Task<Guid?> GetTenantPlanIdAsync(Guid tenantId)
    {
        var gen = FeatureCacheInvalidator.Generation;
        var cacheKey = $"tenant-plan-{tenantId}-{gen}";

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _planTtl;
            if (_hasSizeLimit) entry.Size = 1;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

            return await db.Tenants
                .Where(t => t.Id == tenantId)
                .Select(t => (Guid?)t.PlanId)
                .FirstOrDefaultAsync();
        });
    }
}

internal class TenantOverrideCacheEntry
{
    public Guid FeatureId { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
