using saas.IntegrationTests.Fixtures;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Navigation;

public class BreadcrumbIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public BreadcrumbIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    [Fact]
    public async Task ClientsPage_RendersDashboardAndCurrentBreadcrumb()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/clients");

        response.AssertSuccess();
        await response.AssertElementExistsAsync("div.breadcrumbs");
        await response.AssertElementExistsAsync($"div.breadcrumbs a[href='/{TenantSlug}']");
        await response.AssertContainsAsync("Clients");
    }

    [Fact]
    public async Task PortalActionsPage_RendersParentAndCurrentBreadcrumbs()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/portal/actions");

        response.AssertSuccess();
        await response.AssertElementExistsAsync("div.breadcrumbs");
        await response.AssertElementExistsAsync($"div.breadcrumbs a[href='/{TenantSlug}']");
        await response.AssertElementExistsAsync($"div.breadcrumbs a[href='/{TenantSlug}/portal/links']");
        await response.AssertContainsAsync("Portal");
        await response.AssertContainsAsync("Client Actions");
    }

    [Fact]
    public async Task SessionsPage_RendersParentAndCurrentBreadcrumbs()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/profile/sessions");

        response.AssertSuccess();
        await response.AssertElementExistsAsync("div.breadcrumbs");
        await response.AssertElementExistsAsync($"div.breadcrumbs a[href='/{TenantSlug}']");
        await response.AssertElementExistsAsync($"div.breadcrumbs a[href='/{TenantSlug}/profile']");
        await response.AssertContainsAsync("Profile & 2FA");
        await response.AssertContainsAsync("Active Sessions");
    }

    [Fact]
    public async Task Breadcrumbs_WhenUnauthenticatedUserVisitsTenantPage_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/{TenantSlug}/clients");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }
}
