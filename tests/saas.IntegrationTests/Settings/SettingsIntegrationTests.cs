using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
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
    public async Task CreateRoomType_OnValidSubmit_PersistsToDatabase()
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

        await using var db = OpenTenantDb();
        var roomType = await db.RoomTypes.SingleAsync(r => r.Code == uniqueCode);
        Assert.NotEqual(Guid.Empty, roomType.Id);
        Assert.Equal("Luxury Suite", roomType.Name);
        Assert.Equal(999, roomType.SortOrder);
        Assert.True(roomType.IsActive);
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

    // ── Layer 4: Database Verification ──

    [Fact]
    public async Task CreateDestination_OnValidSubmit_PersistsToDatabase()
    {
        var uniqueName = $"Dest-{Guid.NewGuid():N}"[..16];
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/settings/destinations/new");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = uniqueName,
            ["CountryCode"] = "ZA",
            ["CountryName"] = "South Africa",
            ["Region"] = "Western Cape",
            ["IsActive"] = "true"
        });

        response.AssertSuccess();
        response.AssertToast("Destination created.");

        await using var db = OpenTenantDb();
        var dest = await db.Destinations.SingleAsync(d => d.Name == uniqueName);
        Assert.Equal("ZA", dest.CountryCode);
        Assert.Equal("South Africa", dest.CountryName);
        Assert.Equal("Western Cape", dest.Region);
        Assert.True(dest.IsActive);
    }

    [Fact]
    public async Task CreateSupplier_OnValidSubmit_PersistsToDatabase()
    {
        var uniqueName = $"Supp-{Guid.NewGuid():N}"[..16];
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/settings/suppliers/new");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = uniqueName,
            ["ContactName"] = "Jane Doe",
            ["ContactEmail"] = $"{uniqueName.ToLowerInvariant()}@supplier.test",
            ["IsActive"] = "true"
        });

        response.AssertSuccess();
        response.AssertToast("Supplier created.");

        await using var db = OpenTenantDb();
        var supplier = await db.Suppliers.SingleAsync(s => s.Name == uniqueName);
        Assert.Equal("Jane Doe", supplier.ContactName);
        Assert.Equal($"{uniqueName.ToLowerInvariant()}@supplier.test", supplier.ContactEmail);
        Assert.True(supplier.IsActive);
    }

    [Fact]
    public async Task DeleteRoomType_RemovesFromDatabase()
    {
        var uniqueCode = $"DL{Guid.NewGuid():N}"[..6].ToUpperInvariant();
        var createForm = await _client.HtmxGetAsync($"/{TenantSlug}/settings/room-types/new");
        await _client.SubmitFormAsync(createForm, "form", new Dictionary<string, string>
        {
            ["Code"] = uniqueCode,
            ["Name"] = "Deletable Room",
            ["SortOrder"] = "998",
            ["IsActive"] = "true"
        });

        Guid roomTypeId;
        await using (var db = OpenTenantDb())
        {
            var created = await db.RoomTypes.SingleAsync(r => r.Code == uniqueCode);
            roomTypeId = created.Id;
        }

        var confirmResponse = await _client.HtmxGetAsync($"/{TenantSlug}/settings/room-types/delete-confirm/{roomTypeId}");
        confirmResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(confirmResponse, "form", new Dictionary<string, string>());

        response.AssertSuccess();
        response.AssertToast("Room type deleted.");

        await using var verifyDb = OpenTenantDb();
        Assert.False(await verifyDb.RoomTypes.AnyAsync(r => r.Id == roomTypeId));
    }

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }
}
