using Microsoft.AspNetCore.Http;
using Microsoft.FeatureManagement;
using saas.Modules.FeatureFlags.Services;
using Xunit;

namespace saas.Tests.Modules.FeatureFlags;

public class FeatureServiceTests
{
    private class FakeHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = new DefaultHttpContext();
    }

    /// <summary>
    /// The service delegates to IFeatureManager for enabled features.
    /// </summary>
    [Theory]
    [InlineData("notes", true)]
    [InlineData("sso", false)]
    public async Task IsEnabledAsync_DelegatesToFeatureManager(string featureKey, bool expected)
    {
        var featureManager = new StubFeatureManager(enabledFeatures: ["notes", "projects"]);
        var sut = new FeatureService(featureManager, new FakeHttpContextAccessor());

        Assert.Equal(expected, await sut.IsEnabledAsync(featureKey));
    }

    [Fact]
    public async Task GetEnabledFeaturesAsync_ReturnsOnlyEnabled()
    {
        var featureManager = new StubFeatureManager(enabledFeatures: ["notes", "projects"]);
        var sut = new FeatureService(featureManager, new FakeHttpContextAccessor());

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
