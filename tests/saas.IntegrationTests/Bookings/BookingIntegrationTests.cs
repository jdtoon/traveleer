using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using saas.Modules.Bookings.Entities;
using saas.Modules.Clients.Entities;
using saas.Modules.Inventory.Entities;
using saas.Modules.Settings.Entities;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Bookings;

public class BookingIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public BookingIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    [Fact]
    public async Task BookingsPage_RendersFullLayout()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/bookings");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
        await response.AssertElementExistsAsync("#modal-container");
        await response.AssertElementExistsAsync("#booking-list");
    }

    [Fact]
    public async Task BookingListPartial_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/list");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("table, div.rounded-box");
    }

    [Fact]
    public async Task BookingNewPartial_RendersModalForm()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/new");

        response.AssertSuccess();
        await response.AssertElementExistsAsync("dialog.modal");
        await response.AssertContainsAsync("New Booking");
    }

    [Fact]
    public async Task BookingSummaryPartial_RendersWithoutLayout()
    {
        var (bookingId, _) = await SeedBookingWithItemAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/summary/{bookingId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("Totals");
        await response.AssertContainsAsync("Selling: USD");
    }

    [Fact]
    public async Task BookingsPage_UserCanCreateBooking()
    {
        var clientId = await GetFirstClientIdAsync();
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/new");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["ClientId"] = clientId.ToString(),
            ["ClientReference"] = $"OPS-{Guid.NewGuid():N}"[..12],
            ["TravelStartDate"] = "2026-05-01",
            ["TravelEndDate"] = "2026-05-05",
            ["Pax"] = "4",
            ["SellingCurrencyCode"] = "USD"
        });

        response.AssertSuccess();
        response.AssertToast("Booking created.");
    }

    [Fact]
    public async Task CreateBooking_OnInvalidSubmit_ReturnsFormWithErrors()
    {
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/new");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["ClientId"] = "",
            ["Pax"] = "0"
        });

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("dialog.modal");
        await response.AssertContainsAsync("Client is required.");
    }

    [Fact]
    public async Task BookingDetails_UserCanOpenAddItemFormAndCreateService()
    {
        var bookingId = await SeedBookingAsync();
        var inventory = await GetFirstInventoryAsync();

        var page = await _client.GetAsync($"/{TenantSlug}/bookings/details/{bookingId}");
        page.AssertSuccess();
        await page.AssertElementExistsAsync("#booking-items");

        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/items/new/{bookingId}");
        formResponse.AssertSuccess();
        await formResponse.AssertContainsAsync("Add Service");

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["InventoryItemId"] = inventory.Id.ToString(),
            ["ServiceDate"] = "2026-05-10",
            ["EndDate"] = "2026-05-12",
            ["Quantity"] = "1",
            ["Pax"] = "2",
            ["SellingPrice"] = "900"
        });

        response.AssertSuccess();
        response.AssertToast("Booking service added.");

        var itemsResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/items/{bookingId}");
        itemsResponse.AssertSuccess();
        await itemsResponse.AssertContainsAsync(inventory.Name);

        var summaryResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/summary/{bookingId}");
        summaryResponse.AssertSuccess();
        await summaryResponse.AssertContainsAsync("Selling: USD 900.00");
    }

    [Fact]
    public async Task BookingItemsPartial_RequestSupplierAction_Works()
    {
        var (bookingId, itemId) = await SeedBookingWithItemAsync();
        var itemsResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/items/{bookingId}");
        itemsResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(itemsResponse, $"form[hx-post='/{TenantSlug}/bookings/items/request/{bookingId}/{itemId}']", new Dictionary<string, string>());

        response.AssertSuccess();
        response.AssertToast("Supplier request sent.");
        response.AssertTrigger("bookings.items.refresh");

        await using var db = OpenTenantDb();
        var item = await db.BookingItems.FirstAsync(x => x.Id == itemId);
        Assert.Equal(SupplierStatus.Requested, item.SupplierStatus);
        Assert.NotNull(item.RequestedAt);
    }

    [Fact]
    public async Task BookingItemsPartial_WhenRequested_UserCanSendReminder()
    {
        var (bookingId, itemId) = await SeedBookingWithItemAsync(status: SupplierStatus.Requested);
        var itemsResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/items/{bookingId}");
        itemsResponse.AssertSuccess();
        await itemsResponse.AssertContainsAsync("Send Reminder");

        var response = await _client.SubmitFormAsync(itemsResponse, $"form[hx-post='/{TenantSlug}/bookings/items/remind/{bookingId}/{itemId}']", new Dictionary<string, string>());

        response.AssertSuccess();
        response.AssertToast("Supplier reminder sent.");
        response.AssertTrigger("bookings.items.refresh");

        var refreshedItems = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/items/{bookingId}");
        refreshedItems.AssertSuccess();
        await refreshedItems.AssertContainsAsync("Request sent:");
    }

    [Fact]
    public async Task BookingsPage_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/{TenantSlug}/bookings");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }

    private async Task<Guid> GetFirstClientIdAsync()
    {
        await using var db = OpenTenantDb();
        return await db.Clients.OrderBy(x => x.Name).Select(x => x.Id).FirstAsync();
    }

    private async Task<(Guid Id, string Name)> GetFirstInventoryAsync()
    {
        await using var db = OpenTenantDb();
        return await db.InventoryItems.OrderBy(x => x.Name).Select(x => new ValueTuple<Guid, string>(x.Id, x.Name)).FirstAsync();
    }

    private async Task<Guid> SeedBookingAsync()
    {
        await using var db = OpenTenantDb();
        var clientId = await db.Clients.OrderBy(x => x.Name).Select(x => x.Id).FirstAsync();
        var booking = new Booking
        {
            BookingRef = $"BK-T-{Guid.NewGuid():N}"[..13],
            ClientId = clientId,
            Pax = 2,
            TravelStartDate = new DateOnly(2026, 5, 1),
            TravelEndDate = new DateOnly(2026, 5, 5),
            CostCurrencyCode = "USD",
            SellingCurrencyCode = "USD",
            CreatedAt = DateTime.UtcNow
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    private async Task<(Guid BookingId, Guid ItemId)> SeedBookingWithItemAsync(SupplierStatus status = SupplierStatus.NotRequested)
    {
        await using var db = OpenTenantDb();
        var clientId = await db.Clients.OrderBy(x => x.Name).Select(x => x.Id).FirstAsync();
        var supplier = new Supplier
        {
            Name = $"QA Supplier {Guid.NewGuid():N}"[..18],
            ContactEmail = "ops-supplier@test.local",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var inventory = new InventoryItem
        {
            Name = $"QA Service {Guid.NewGuid():N}"[..18],
            Kind = InventoryItemKind.Hotel,
            BaseCost = 800m,
            Supplier = supplier,
            CreatedAt = DateTime.UtcNow
        };

        db.InventoryItems.Add(inventory);

        var booking = new Booking
        {
            BookingRef = $"BK-R-{Guid.NewGuid():N}"[..13],
            ClientId = clientId,
            Pax = 2,
            TravelStartDate = new DateOnly(2026, 6, 1),
            TravelEndDate = new DateOnly(2026, 6, 4),
            CostCurrencyCode = "USD",
            SellingCurrencyCode = "USD",
            CreatedAt = DateTime.UtcNow
        };
        var item = new BookingItem
        {
            InventoryItemId = inventory.Id,
            SupplierId = inventory.SupplierId,
            ServiceName = inventory.Name,
            ServiceKind = inventory.Kind,
            CostPrice = inventory.BaseCost,
            SellingPrice = inventory.BaseCost + 100m,
            CostCurrencyCode = "USD",
            SellingCurrencyCode = "USD",
            Quantity = 1,
            Pax = 2,
            SupplierStatus = status,
            ServiceDate = new DateOnly(2026, 6, 1),
            EndDate = new DateOnly(2026, 6, 4),
            Nights = 3,
            RequestedAt = status == SupplierStatus.Requested ? DateTime.UtcNow.AddMinutes(-10) : null
        };
        booking.Items.Add(item);
        booking.TotalCost = booking.Items.Sum(x => x.CostPrice * x.Quantity);
        booking.TotalSelling = booking.Items.Sum(x => x.SellingPrice * x.Quantity);
        booking.TotalProfit = booking.TotalSelling - booking.TotalCost;
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return (booking.Id, item.Id);
    }

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }
}
