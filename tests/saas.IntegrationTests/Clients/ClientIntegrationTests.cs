using saas.IntegrationTests.Fixtures;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Clients;

public class ClientIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _tenantClient;
    private const string TenantSlug = "demo";

    public ClientIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _tenantClient = fixture.CreateTenantClient(TenantSlug);
    }

    [Fact]
    public async Task ClientsPage_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/{TenantSlug}/clients");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task ClientsList_WhenUnauthenticated_HtmxRedirectsToLogin()
    {
        var response = await _tenantClient.HtmxGetAsync($"/{TenantSlug}/clients/list");

        if (response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            response.AssertHeader("HX-Redirect", $"/{TenantSlug}/login?returnUrl=%2F{TenantSlug}%2Fclients%2Flist");
            return;
        }

        Assert.True(
            response.StatusCode == System.Net.HttpStatusCode.Found ||
            response.StatusCode == System.Net.HttpStatusCode.Redirect,
            $"Unexpected status: {response.StatusCode}");
    }

    [Fact]
    public async Task ClientsCreate_WhenUnauthenticated_HtmxRedirectsToLogin()
    {
        var response = await _tenantClient
            .AsHtmxRequest()
            .HtmxPostAsync($"/{TenantSlug}/clients/create", new Dictionary<string, string>
            {
                ["Name"] = "Test Client"
            });

        if (response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            response.AssertHeader("HX-Redirect", $"/{TenantSlug}/login?returnUrl=%2F{TenantSlug}%2Fclients%2Fcreate");
            return;
        }

        Assert.True(
            response.StatusCode == System.Net.HttpStatusCode.Found ||
            response.StatusCode == System.Net.HttpStatusCode.Redirect,
            $"Unexpected status: {response.StatusCode}");
    }
}
