using Microsoft.AspNetCore.Http;
using Microsoft.FeatureManagement;
using saas.Shared;

namespace saas.Modules.FeatureFlags.Services;

public class FeatureService : IFeatureService
{
    private readonly IFeatureManager _featureManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FeatureService(IFeatureManager featureManager, IHttpContextAccessor httpContextAccessor)
    {
        _featureManager = featureManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<bool> IsEnabledAsync(string featureKey)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.Items.TryGetValue("TenantFeatureFlags", out var cachedObj) == true && cachedObj is IReadOnlyList<string> enabledFeatures)
        {
            return enabledFeatures.Contains(featureKey, StringComparer.OrdinalIgnoreCase);
        }

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
