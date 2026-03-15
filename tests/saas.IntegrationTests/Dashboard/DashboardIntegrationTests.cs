using saas.IntegrationTests.Fixtures;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Dashboard;

public class DashboardIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public DashboardIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    [Fact]
    public async Task DashboardPage_RendersFullLayout()
    {
        var response = await _client.GetAsync($"/{TenantSlug}");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
        await response.AssertElementExistsAsync("#modal-container");
        await response.AssertContainsAsync("Dashboard");
    }

    [Fact]
    public async Task DashboardPage_ContainsTasksWidgetTarget()
    {
        var response = await _client.GetAsync($"/{TenantSlug}");

        response.AssertSuccess();
        await response.AssertElementExistsAsync("#dashboard-tasks-widget");
        await response.AssertContainsAsync($"/{TenantSlug}/tasks/widget");
    }

    [Fact]
    public async Task DashboardPage_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/{TenantSlug}");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }
}
