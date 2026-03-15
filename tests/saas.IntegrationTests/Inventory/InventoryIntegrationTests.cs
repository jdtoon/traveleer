using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using saas.Modules.Inventory.Entities;
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
        await response.AssertContainsAsync("hx-get=\"/demo/inventory/list\"");
        await response.AssertContainsAsync("hx-target=\"#inventory-list\"");
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
        var uniqueName = $"Inv-DB-{Guid.NewGuid():N}"[..20];

        var page = await _client.GetAsync($"/{TenantSlug}/inventory");
        page.AssertSuccess();
        await page.AssertElementExistsAsync("#inventory-list");

        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/inventory/new");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = uniqueName,
            ["Kind"] = "Hotel",
            ["BaseCost"] = "12500",
            ["Rating"] = "4"
        });

        response.AssertSuccess();
        response.AssertToast("Inventory item created.");

        await using var db = OpenTenantDb();
        var item = await db.InventoryItems.SingleAsync(i => i.Name == uniqueName);
        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Equal(InventoryItemKind.Hotel, item.Kind);
        Assert.Equal(12500m, item.BaseCost);
        Assert.Equal(4, item.Rating);
        Assert.NotNull(item.CreatedAt);
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

    // ── Layer 4: Database Verification ──

    [Fact]
    public async Task EditInventoryItem_OnValidSubmit_UpdatesDatabase()
    {
        var uniqueName = $"Edit-Inv-{Guid.NewGuid():N}"[..18];
        var createForm = await _client.HtmxGetAsync($"/{TenantSlug}/inventory/new");
        await _client.SubmitFormAsync(createForm, "form", new Dictionary<string, string>
        {
            ["Name"] = uniqueName,
            ["Kind"] = "Hotel",
            ["BaseCost"] = "5000",
            ["Rating"] = "3"
        });

        Guid itemId;
        await using (var db = OpenTenantDb())
        {
            var created = await db.InventoryItems.SingleAsync(i => i.Name == uniqueName);
            itemId = created.Id;
        }

        var editForm = await _client.HtmxGetAsync($"/{TenantSlug}/inventory/edit/{itemId}");
        editForm.AssertSuccess();

        var updatedName = $"Upd-Inv-{Guid.NewGuid():N}"[..18];
        var response = await _client.SubmitFormAsync(editForm, "form", new Dictionary<string, string>
        {
            ["Name"] = updatedName,
            ["Kind"] = "Hotel",
            ["BaseCost"] = "7500",
            ["Rating"] = "5"
        });

        response.AssertSuccess();
        response.AssertToast("Inventory item updated.");

        await using var verifyDb = OpenTenantDb();
        var updated = await verifyDb.InventoryItems.SingleAsync(i => i.Id == itemId);
        Assert.Equal(updatedName, updated.Name);
        Assert.Equal(7500m, updated.BaseCost);
        Assert.Equal(5, updated.Rating);
    }

    [Fact]
    public async Task DeleteInventoryItem_RemovesFromDatabase()
    {
        var uniqueName = $"Del-Inv-{Guid.NewGuid():N}"[..18];
        var createForm = await _client.HtmxGetAsync($"/{TenantSlug}/inventory/new");
        await _client.SubmitFormAsync(createForm, "form", new Dictionary<string, string>
        {
            ["Name"] = uniqueName,
            ["Kind"] = "Excursion",
            ["BaseCost"] = "200"
        });

        Guid itemId;
        await using (var db = OpenTenantDb())
        {
            var created = await db.InventoryItems.SingleAsync(i => i.Name == uniqueName);
            itemId = created.Id;
        }

        var confirmResponse = await _client.HtmxGetAsync($"/{TenantSlug}/inventory/delete-confirm/{itemId}");
        confirmResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(confirmResponse, "form", new Dictionary<string, string>());

        response.AssertSuccess();
        response.AssertToast("Inventory item deleted.");

        await using var verifyDb = OpenTenantDb();
        Assert.False(await verifyDb.InventoryItems.AnyAsync(i => i.Id == itemId));
    }

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }

    // ── Transfer Management Tests ──

    [Fact]
    public async Task CreateTransferItem_PersistsTransportFields()
    {
        var uniqueName = $"Xfer-{Guid.NewGuid():N}"[..18];
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/inventory/new");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = uniqueName,
            ["Kind"] = "Transfer",
            ["BaseCost"] = "450",
            ["PickupLocation"] = "OR Tambo Airport",
            ["DropoffLocation"] = "Sandton Hotel",
            ["VehicleType"] = "Minibus",
            ["MaxPassengers"] = "12",
            ["IncludesMeetAndGreet"] = "true",
            ["TransferDurationMinutes"] = "45"
        });

        response.AssertSuccess();
        response.AssertToast("Inventory item created.");

        await using var db = OpenTenantDb();
        var item = await db.InventoryItems.SingleAsync(i => i.Name == uniqueName);
        Assert.Equal(InventoryItemKind.Transfer, item.Kind);
        Assert.Equal("OR Tambo Airport", item.PickupLocation);
        Assert.Equal("Sandton Hotel", item.DropoffLocation);
        Assert.Equal("Minibus", item.VehicleType);
        Assert.Equal(12, item.MaxPassengers);
        Assert.True(item.IncludesMeetAndGreet);
        Assert.Equal(45, item.TransferDurationMinutes);
    }

    [Fact]
    public async Task CreateHotelItem_IgnoresTransportFields()
    {
        var uniqueName = $"Hotel-{Guid.NewGuid():N}"[..18];
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/inventory/new");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = uniqueName,
            ["Kind"] = "Hotel",
            ["BaseCost"] = "5000",
            ["PickupLocation"] = "Should be ignored",
            ["DropoffLocation"] = "Should be ignored",
            ["VehicleType"] = "Sedan"
        });

        response.AssertSuccess();

        await using var db = OpenTenantDb();
        var item = await db.InventoryItems.SingleAsync(i => i.Name == uniqueName);
        Assert.Null(item.PickupLocation);
        Assert.Null(item.DropoffLocation);
        Assert.Null(item.VehicleType);
    }

    [Fact]
    public async Task TransferListPartial_ShowsTransportDetails()
    {
        var uniqueName = $"XferList-{Guid.NewGuid():N}"[..18];
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/inventory/new");
        await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = uniqueName,
            ["Kind"] = "Transfer",
            ["BaseCost"] = "200",
            ["PickupLocation"] = "Airport Terminal",
            ["DropoffLocation"] = "Beach Resort"
        });

        var listResponse = await _client.HtmxGetAsync($"/{TenantSlug}/inventory/list?type=Transfer&search={Uri.EscapeDataString(uniqueName)}");
        listResponse.AssertSuccess();
        await listResponse.AssertContainsAsync("Airport Terminal");
        await listResponse.AssertContainsAsync("Beach Resort");
    }

    [Fact]
    public async Task EditTransferItem_UpdatesTransportFields()
    {
        var uniqueName = $"XferEdit-{Guid.NewGuid():N}"[..18];
        var createForm = await _client.HtmxGetAsync($"/{TenantSlug}/inventory/new");
        await _client.SubmitFormAsync(createForm, "form", new Dictionary<string, string>
        {
            ["Name"] = uniqueName,
            ["Kind"] = "Transfer",
            ["BaseCost"] = "300",
            ["PickupLocation"] = "Old Pickup",
            ["DropoffLocation"] = "Old Dropoff"
        });

        Guid itemId;
        await using (var db = OpenTenantDb())
        {
            var created = await db.InventoryItems.SingleAsync(i => i.Name == uniqueName);
            itemId = created.Id;
        }

        var editForm = await _client.HtmxGetAsync($"/{TenantSlug}/inventory/edit/{itemId}");
        editForm.AssertSuccess();
        await editForm.AssertContainsAsync("Old Pickup");

        var response = await _client.SubmitFormAsync(editForm, "form", new Dictionary<string, string>
        {
            ["Name"] = uniqueName,
            ["Kind"] = "Transfer",
            ["BaseCost"] = "350",
            ["PickupLocation"] = "New Pickup",
            ["DropoffLocation"] = "New Dropoff",
            ["VehicleType"] = "Luxury SUV",
            ["MaxPassengers"] = "6"
        });

        response.AssertSuccess();
        response.AssertToast("Inventory item updated.");

        await using var verifyDb = OpenTenantDb();
        var updated = await verifyDb.InventoryItems.SingleAsync(i => i.Id == itemId);
        Assert.Equal("New Pickup", updated.PickupLocation);
        Assert.Equal("New Dropoff", updated.DropoffLocation);
        Assert.Equal("Luxury SUV", updated.VehicleType);
        Assert.Equal(6, updated.MaxPassengers);
    }

    [Fact]
    public async Task EditForm_PrePopulatesTransportFields()
    {
        var uniqueName = $"XferPre-{Guid.NewGuid():N}"[..18];
        var createForm = await _client.HtmxGetAsync($"/{TenantSlug}/inventory/new");
        await _client.SubmitFormAsync(createForm, "form", new Dictionary<string, string>
        {
            ["Name"] = uniqueName,
            ["Kind"] = "Transfer",
            ["BaseCost"] = "500",
            ["PickupLocation"] = "Check Airport",
            ["DropoffLocation"] = "Check Hotel",
            ["VehicleType"] = "Sedan"
        });

        Guid itemId;
        await using (var db = OpenTenantDb())
        {
            var created = await db.InventoryItems.SingleAsync(i => i.Name == uniqueName);
            itemId = created.Id;
        }

        var editForm = await _client.HtmxGetAsync($"/{TenantSlug}/inventory/edit/{itemId}");
        editForm.AssertSuccess();
        await editForm.AssertContainsAsync("Check Airport");
        await editForm.AssertContainsAsync("Check Hotel");
        await editForm.AssertContainsAsync("Sedan");
    }
}
