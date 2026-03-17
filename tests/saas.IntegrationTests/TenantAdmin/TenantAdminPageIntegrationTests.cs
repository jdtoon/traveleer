using saas.IntegrationTests.Fixtures;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.TenantAdmin;

public class TenantAdminPageIntegrationTests : IClassFixture<AppFixture>
{
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public TenantAdminPageIntegrationTests(AppFixture fixture)
    {
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    [Fact]
    public async Task TenantBillingPage_RendersFullLayout()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/admin/billing");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
        await response.AssertContainsAsync("Billing");
    }

    [Fact]
    public async Task TenantSettingsPage_RendersFullLayout()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/admin/settings");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
        await response.AssertContainsAsync("Settings");
    }
}