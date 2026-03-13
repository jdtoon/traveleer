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
    public async Task HomePage_RendersTravelAgencyCallToAction()
    {
        var response = await _client.GetAsync("/");
        response.AssertSuccess();
        await response.AssertContainsAsync("Stop wrestling with hotel rates in spreadsheets.");
        await response.AssertContainsAsync("Travel agencies use Traveleer");
    }

    [Fact]
    public async Task PricingPage_ContainsPlans()
    {
        var response = await _client.GetAsync("/pricing");
        response.AssertSuccess();
        await response.AssertContainsAsync("Plans for agencies that want cleaner rate, quote, and booking operations.");
        await response.AssertContainsAsync("Get started");
    }

    [Fact]
    public async Task PricingContentPartial_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync("/pricing/content?mode=annual");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("Plans for agencies that want cleaner rate, quote, and booking operations.");
        await response.AssertDoesNotContainAsync("<html");
    }

    [Fact]
    public async Task ContactForm_OnInvalidSubmit_ReturnsErrorPartial()
    {
        var page = await _client.GetAsync("/contact");
        page.AssertSuccess();

        var response = await _client.SubmitFormAsync(page, "form[hx-post='/contact']", new Dictionary<string, string>());

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("required");
    }

    [Fact]
    public async Task HealthCheck_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");

        Assert.True(
            response.StatusCode == System.Net.HttpStatusCode.OK ||
            response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable,
            $"Unexpected status: {response.StatusCode}");

        await response.AssertContainsAsync("litestream-readiness");
    }
}
