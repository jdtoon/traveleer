using saas.IntegrationTests.Fixtures;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Settings;

public class SettingsIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public SettingsIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    [Fact]
    public async Task SettingsPage_RendersFullLayout()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/settings");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
        await response.AssertElementExistsAsync("#modal-container");
        await response.AssertContainsAsync("Settings");
    }

    [Fact]
    public async Task RoomTypesPartial_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/settings/room-types");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("Double Room");
    }

    [Fact]
    public async Task CurrenciesPartial_RendersSeededCurrencies()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/settings/currencies");

        response.AssertSuccess();
        await response.AssertContainsAsync("South African Rand");
        await response.AssertContainsAsync("US Dollar");
    }

    [Fact]
    public async Task UsersPartial_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/settings/users");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("admin@demo.local");
    }

    [Fact]
    public async Task RoomTypeNewPartial_RendersModalForm()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/settings/room-types/new");

        response.AssertSuccess();
        await response.AssertElementExistsAsync("dialog.modal");
        await response.AssertContainsAsync("New Room Type");
    }

    [Fact]
    public async Task CreateRoomType_OnValidSubmit_ShowsSuccessToast()
    {
        var uniqueCode = $"LX{Guid.NewGuid():N}"[..6].ToUpperInvariant();
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/settings/room-types/new");
        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Code"] = uniqueCode,
            ["Name"] = "Luxury Suite",
            ["SortOrder"] = "999",
            ["IsActive"] = "true"
        });

        response.AssertSuccess();
        response.AssertToast("Room type created.");
    }

    [Fact]
    public async Task CreateRoomType_OnInvalidSubmit_RerendersForm()
    {
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/settings/room-types/new");
        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Code"] = "",
            ["Name"] = ""
        });

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("dialog.modal");
    }

    [Fact]
    public async Task SettingsPage_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/{TenantSlug}/settings");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }
}
