using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.FeatureManagement;
using saas.Data.Core;

namespace saas.Modules.FeatureFlags.Services;

public class DatabaseFeatureDefinitionProvider : IFeatureDefinitionProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DatabaseFeatureDefinitionProvider> _logger;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public DatabaseFeatureDefinitionProvider(
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        ILogger<DatabaseFeatureDefinitionProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    public async IAsyncEnumerable<FeatureDefinition> GetAllFeatureDefinitionsAsync()
    {
        var features = await GetFeaturesFromCacheAsync();

        foreach (var feature in features)
        {
            yield return CreateDefinition(feature);
        }
    }

    public async Task<FeatureDefinition?> GetFeatureDefinitionAsync(string featureName)
    {
        var features = await GetFeaturesFromCacheAsync();
        var feature = features.FirstOrDefault(f => f.Key == featureName);

        return feature is null ? null : CreateDefinition(feature);
    }

    private static FeatureDefinition CreateDefinition(FeatureCacheEntry feature)
    {
        var definition = new FeatureDefinition { Name = feature.Key };

        if (!feature.IsEnabled)
        {
            // Kill switch is off — feature is disabled for everyone
            definition.EnabledFor = [];
        }
        else if (feature.IsGlobal)
        {
            // Global features are always on
            definition.EnabledFor =
            [
                new FeatureFilterConfiguration { Name = "AlwaysOn" }
            ];
        }
        else
        {
            // Plan-based features use our custom TenantPlan filter
            definition.EnabledFor =
            [
                new FeatureFilterConfiguration
                {
                    Name = "TenantPlan",
                    Parameters = new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["FeatureId"] = feature.Id.ToString(),
                            ["PlanIds"] = string.Join(",", feature.EnabledPlanIds),
                        })
                        .Build()
                }
            ];
        }

        return definition;
    }

    private async Task<List<FeatureCacheEntry>> GetFeaturesFromCacheAsync()
    {
        return await _cache.GetOrCreateAsync("feature-definitions", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

            _logger.LogDebug("Loading feature definitions from database");

            return await db.Features
                .AsNoTracking()
                .Include(f => f.PlanFeatures)
                .Select(f => new FeatureCacheEntry
                {
                    Id = f.Id,
                    Key = f.Key,
                    IsEnabled = f.IsEnabled,
                    IsGlobal = f.IsGlobal,
                    EnabledPlanIds = f.PlanFeatures.Select(pf => pf.PlanId).ToList()
                })
                .ToListAsync();
        }) ?? [];
    }
}

internal class FeatureCacheEntry
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsGlobal { get; set; }
    public List<Guid> EnabledPlanIds { get; set; } = [];
}
