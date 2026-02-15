using Microsoft.FeatureManagement;
using saas.Shared;

namespace saas.Modules.FeatureFlags.Services;

public class FeatureService : IFeatureService
{
    private readonly IFeatureManager _featureManager;

    public FeatureService(IFeatureManager featureManager)
    {
        _featureManager = featureManager;
    }

    public async Task<bool> IsEnabledAsync(string featureKey)
    {
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
