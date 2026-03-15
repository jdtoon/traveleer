using saas.IntegrationTests.Fixtures;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Navigation;

public class SidebarNavigationIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public SidebarNavigationIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    private static string SidebarHostPage => $"/{TenantSlug}/clients";

    [Fact]
    public async Task Sidebar_ContainsSalesGroupWithExpectedModules()
    {
        var response = await _client.GetAsync(SidebarHostPage);

        response.AssertSuccess();
        await response.AssertContainsAsync("Sales");
        await response.AssertContainsAsync($"/{TenantSlug}/clients");
        await response.AssertContainsAsync($"/{TenantSlug}/quotes");
        await response.AssertContainsAsync($"/{TenantSlug}/bookings");
        await response.AssertContainsAsync($"/{TenantSlug}/itineraries");
    }

    [Fact]
    public async Task Sidebar_ContainsOperationsGroupWithExpectedModules()
    {
        var response = await _client.GetAsync(SidebarHostPage);

        response.AssertSuccess();
        await response.AssertContainsAsync("Operations");
        await response.AssertContainsAsync($"/{TenantSlug}/suppliers");
        await response.AssertContainsAsync($"/{TenantSlug}/inventory");
        await response.AssertContainsAsync($"/{TenantSlug}/rate-cards");
        await response.AssertContainsAsync($"/{TenantSlug}/tasks");
    }

    [Fact]
    public async Task Sidebar_ContainsInsightsGroupWithExpectedModules()
    {
        var response = await _client.GetAsync(SidebarHostPage);

        response.AssertSuccess();
        await response.AssertContainsAsync("Insights");
        await response.AssertContainsAsync($"/{TenantSlug}/reports");
        await response.AssertContainsAsync($"/{TenantSlug}/portal/links");
    }

    [Fact]
    public async Task Sidebar_ContainsConfigurationGroupWithExpectedModules()
    {
        var response = await _client.GetAsync(SidebarHostPage);

        response.AssertSuccess();
        await response.AssertContainsAsync("Configuration");
        await response.AssertContainsAsync($"/{TenantSlug}/branding");
        await response.AssertContainsAsync($"/{TenantSlug}/settings");
        await response.AssertContainsAsync($"/{TenantSlug}/audit");
    }

    [Fact]
    public async Task Sidebar_ContainsAllSectionHeaders()
    {
        var response = await _client.GetAsync(SidebarHostPage);

        response.AssertSuccess();
        await response.AssertContainsAsync("Sales");
        await response.AssertContainsAsync("Operations");
        await response.AssertContainsAsync("Insights");
        await response.AssertContainsAsync("Configuration");
        await response.AssertContainsAsync("Account");
    }

    [Fact]
    public async Task Sidebar_UnauthenticatedUser_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/{TenantSlug}");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Sidebar_DashboardLink_AlwaysPresent()
    {
        var response = await _client.GetAsync(SidebarHostPage);

        response.AssertSuccess();
        await response.AssertContainsAsync("Dashboard");
        await response.AssertElementExistsAsync("#main-content");
    }

    [Fact]
    public async Task SuppliersPage_RendersFullLayoutWithSidebar()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/suppliers");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
    }

    [Fact]
    public async Task ItinerariesPage_RendersFullLayoutWithSidebar()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/itineraries");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
    }

    [Fact]
    public async Task TasksPage_RendersFullLayoutWithSidebar()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/tasks");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
    }

    [Fact]
    public async Task ReportsPage_RendersFullLayoutWithSidebar()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/reports");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
    }

    [Fact]
    public async Task PortalLinksPage_RendersFullLayoutWithSidebar()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/portal/links");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
    }

    [Fact]
    public async Task PortalActionsPage_RendersFullLayoutWithSidebar()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/portal/actions");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
        await response.AssertContainsAsync("Portal");
    }

    [Fact]
    public async Task PortalActionsPage_MarksPortalNavigationActive()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/portal/actions");

        response.AssertSuccess();
        await response.AssertElementExistsAsync($"a[href='/{TenantSlug}/portal/links'].active");
    }

    [Fact]
    public async Task SessionsPage_MarksSessionsNavigationActive()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/profile/sessions");

        response.AssertSuccess();
        await response.AssertElementExistsAsync($"a[href='/{TenantSlug}/profile/sessions'].active");
        await response.AssertElementExistsAsync($"a[href='/{TenantSlug}/Profile']:not(.active)");
    }
}
