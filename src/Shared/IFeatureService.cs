namespace saas.Shared;

/// <summary>
/// Feature flag checks. Wraps Microsoft.FeatureManagement with tenant context.
/// </summary>
public interface IFeatureService
{
    Task<bool> IsEnabledAsync(string featureKey);
    Task<IReadOnlyList<string>> GetEnabledFeaturesAsync();
}
