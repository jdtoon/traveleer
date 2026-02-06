using Microsoft.Extensions.Configuration;
using Microsoft.FeatureManagement;
using saas.Modules.FeatureFlags.Services;
using Xunit;

namespace saas.Tests;

public class FeatureServiceTests
{
    /// <summary>
    /// When AllEnabledLocally = true, every feature returns true regardless of IFeatureManager.
    /// </summary>
    [Fact]
    public async Task IsEnabledAsync_AllEnabledLocally_ReturnsTrue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:AllEnabledLocally"] = "true"
            })
            .Build();

        var featureManager = new StubFeatureManager(enabledFeatures: []);
        var sut = new FeatureService(featureManager, config);

        Assert.True(await sut.IsEnabledAsync("notes"));
        Assert.True(await sut.IsEnabledAsync("some_nonexistent_feature"));
    }

    /// <summary>
    /// When AllEnabledLocally = false, the service delegates to IFeatureManager.
    /// </summary>
    [Theory]
    [InlineData("notes", true)]
    [InlineData("sso", false)]
    public async Task IsEnabledAsync_DelegatesToFeatureManager(string featureKey, bool expected)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:AllEnabledLocally"] = "false"
            })
            .Build();

        var featureManager = new StubFeatureManager(enabledFeatures: ["notes", "projects"]);
        var sut = new FeatureService(featureManager, config);

        Assert.Equal(expected, await sut.IsEnabledAsync(featureKey));
    }

    [Fact]
    public async Task GetEnabledFeaturesAsync_ReturnsOnlyEnabled()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:AllEnabledLocally"] = "false"
            })
            .Build();

        var featureManager = new StubFeatureManager(enabledFeatures: ["notes", "projects"]);
        var sut = new FeatureService(featureManager, config);

        var enabled = await sut.GetEnabledFeaturesAsync();

        Assert.Equal(2, enabled.Count);
        Assert.Contains("notes", enabled);
        Assert.Contains("projects", enabled);
    }

    // -------------------------------------------------------------------------
    // Stub IFeatureManager for unit tests
    // -------------------------------------------------------------------------

    private class StubFeatureManager : IFeatureManager
    {
        private readonly HashSet<string> _enabled;

        public StubFeatureManager(IEnumerable<string> enabledFeatures)
        {
            _enabled = new HashSet<string>(enabledFeatures);
        }

        public Task<bool> IsEnabledAsync(string feature) =>
            Task.FromResult(_enabled.Contains(feature));

        public Task<bool> IsEnabledAsync<TContext>(string feature, TContext context) =>
            Task.FromResult(_enabled.Contains(feature));

        public async IAsyncEnumerable<string> GetFeatureNamesAsync()
        {
            foreach (var name in _enabled)
                yield return name;

            await Task.CompletedTask;
        }
    }
}
