using Microsoft.FeatureManagement;
using saas.Modules.FeatureFlags.Services;
using saas.Shared;

namespace saas.Modules.FeatureFlags;

public class FeatureFlagsModule : IModule
{
    public string Name => "FeatureFlags";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Register our custom feature definition provider
        services.AddSingleton<IFeatureDefinitionProvider, DatabaseFeatureDefinitionProvider>();

        // Register Microsoft Feature Management with our custom tenant plan filter
        services.AddFeatureManagement()
            .AddFeatureFilter<TenantPlanFeatureFilter>();

        // Register our IFeatureService wrapper
        services.AddScoped<IFeatureService, FeatureService>();

        // Cache invalidation helper
        services.AddSingleton<FeatureCacheInvalidator>();
    }
}
