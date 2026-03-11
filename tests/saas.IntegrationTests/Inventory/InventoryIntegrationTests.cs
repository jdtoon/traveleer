using saas.IntegrationTests.Fixtures;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Inventory;

public class InventoryIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public InventoryIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    [Fact]
    public async Task InventoryPage_RendersFullLayout()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/inventory");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
        await response.AssertElementExistsAsync("#modal-container");
        await response.AssertElementExistsAsync("#inventory-list");
    }

    [Fact]
    public async Task InventoryListPartial_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/inventory/list");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("article.card, div.rounded-box");
    }

    [Fact]
    public async Task InventoryListPartial_TypeFilter_Works()
    {
        var excursionName = $"Excursion {Guid.NewGuid():N}"[..18];
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/inventory/new");
        formResponse.AssertSuccess();

        var createResponse = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = excursionName,
            ["Kind"] = "Excursion",
            ["BaseCost"] = "850"
        });
        createResponse.AssertSuccess();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/inventory/list?type=Excursion&search={Uri.EscapeDataString(excursionName)}");

        response.AssertSuccess();
        await response.AssertContainsAsync(excursionName);
    }

    [Fact]
    public async Task InventoryNewPartial_RendersModalForm()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/inventory/new");

        response.AssertSuccess();
        await response.AssertElementExistsAsync("dialog.modal");
        await response.AssertContainsAsync("New Inventory Item");
    }

    [Fact]
    public async Task InventoryPage_UserCanOpenCreateFormAndCreateItem()
    {
        var page = await _client.GetAsync($"/{TenantSlug}/inventory");
        page.AssertSuccess();
        await page.AssertElementExistsAsync("#inventory-list");

        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/inventory/new");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = $"Inventory Test {Guid.NewGuid():N}"[..20],
            ["Kind"] = "Hotel",
            ["BaseCost"] = "12500",
            ["Rating"] = "4"
        });

        response.AssertSuccess();
        response.AssertToast("Inventory item created.");
    }

    [Fact]
    public async Task CreateInventory_OnInvalidSubmit_ReturnsFormWithErrors()
    {
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/inventory/new");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = "",
            ["Kind"] = "Hotel"
        });

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("dialog.modal");
        await response.AssertContainsAsync("Name is required.");
    }

    [Fact]
    public async Task InventoryPage_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/{TenantSlug}/inventory");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }
}
