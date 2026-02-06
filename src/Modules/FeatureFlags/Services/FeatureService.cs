using Microsoft.FeatureManagement;
using saas.Shared;

namespace saas.Modules.FeatureFlags.Services;

public class FeatureService : IFeatureService
{
    private readonly IFeatureManager _featureManager;
    private readonly IConfiguration _configuration;

    public FeatureService(IFeatureManager featureManager, IConfiguration configuration)
    {
        _featureManager = featureManager;
        _configuration = configuration;
    }

    public async Task<bool> IsEnabledAsync(string featureKey)
    {
        // Local dev override: all features enabled
        if (_configuration.GetValue<bool>("FeatureFlags:AllEnabledLocally"))
            return true;

        return await _featureManager.IsEnabledAsync(featureKey);
    }

    public async Task<IReadOnlyList<string>> GetEnabledFeaturesAsync()
    {
        var enabled = new List<string>();
        await foreach (var name in _featureManager.GetFeatureNamesAsync())
        {
            if (await IsEnabledAsync(name))
                enabled.Add(name);
        }
        return enabled;
    }
}
