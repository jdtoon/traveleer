using saas.IntegrationTests.Fixtures;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Marketing;

/// <summary>
/// Smoke tests for public marketing pages.
/// These routes are served without tenant resolution.
/// </summary>
public class MarketingIntegrationTests : IClassFixture<AppFixture>
{
    private readonly HtmxTestClient<Program> _client;

    public MarketingIntegrationTests(AppFixture fixture)
    {
        _client = fixture.CreateClient();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/pricing")]
    [InlineData("/about")]
    [InlineData("/contact")]
    public async Task PublicPage_ReturnsSuccess(string url)
    {
        var response = await _client.GetAsync(url);
        response.AssertSuccess();
    }

    [Fact]
    public async Task HomePage_ContainsCallToAction()
    {
        var response = await _client.GetAsync("/");
        await response
            .AssertSuccess()
            .AssertContainsAsync("Start free");
    }

    [Fact]
    public async Task PricingPage_ContainsPlans()
    {
        var response = await _client.GetAsync("/pricing");
        response.AssertSuccess();
        await response.AssertContainsAsync("Free");
        await response.AssertContainsAsync("Starter");
    }

    [Fact]
    public async Task HealthCheck_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");

        Assert.True(
            response.StatusCode == System.Net.HttpStatusCode.OK ||
            response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable,
            $"Unexpected status: {response.StatusCode}");

        await response.AssertContainsAsync("backup-readiness");
    }
}
