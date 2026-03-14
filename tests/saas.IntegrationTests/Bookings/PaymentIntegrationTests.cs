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

public class PaymentIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public PaymentIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    [Fact]
    public async Task BookingPaymentsPartial_RendersWithoutLayout()
    {
        var bookingId = await SeedBookingAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/payments/{bookingId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("Total Selling");
        await response.AssertContainsAsync("Total Received");
        await response.AssertContainsAsync("Outstanding");
        await response.AssertContainsAsync("No payments recorded yet.");
    }

    [Fact]
    public async Task BookingPaymentsPartial_WithPayments_ShowsTotals()
    {
        var (bookingId, _) = await SeedBookingWithPaymentAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/payments/{bookingId}");

        response.AssertSuccess();
        await response.AssertContainsAsync("Client Payments");
        await response.AssertContainsAsync("500.00");
        await response.AssertContainsAsync("Received");
    }

    [Fact]
    public async Task NewPaymentPartial_RendersModalForm()
    {
        var bookingId = await SeedBookingAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/payments/new/{bookingId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("dialog.modal");
        await response.AssertContainsAsync("Record Payment");
        await response.AssertContainsAsync("Amount");
        await response.AssertContainsAsync("Payment Date");
    }

    [Fact]
    public async Task CreatePayment_OnValidSubmit_PersistsAndReturnsToast()
    {
        var bookingId = await SeedBookingAsync();
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/payments/new/{bookingId}");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Amount"] = "750.00",
            ["PaymentDate"] = "2026-03-10",
            ["PaymentMethod"] = ((int)PaymentMethod.BankTransfer).ToString(),
            ["Direction"] = ((int)PaymentDirection.Received).ToString(),
            ["Reference"] = "INV-PAY-001",
            ["Notes"] = "First payment",
            ["CurrencyCode"] = "USD"
        });

        response.AssertSuccess();
        response.AssertToast("Payment recorded.");
        response.AssertTrigger("bookings.payments.refresh");

        await using var db = OpenTenantDb();
        var payment = await db.Set<BookingPayment>().SingleAsync(p => p.BookingId == bookingId && p.Reference == "INV-PAY-001");
        Assert.Equal(750m, payment.Amount);
        Assert.Equal(new DateOnly(2026, 3, 10), payment.PaymentDate);
        Assert.Equal(PaymentMethod.BankTransfer, payment.PaymentMethod);
        Assert.Equal(PaymentDirection.Received, payment.Direction);
        Assert.Equal("First payment", payment.Notes);
        Assert.Equal("USD", payment.CurrencyCode);
        Assert.NotNull(payment.CreatedAt);
    }

    [Fact]
    public async Task CreatePayment_WithInvalidAmount_ReturnsFormWithErrors()
    {
        var bookingId = await SeedBookingAsync();
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/payments/new/{bookingId}");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Amount"] = "0",
            ["PaymentDate"] = "2026-03-10",
            ["PaymentMethod"] = ((int)PaymentMethod.Cash).ToString(),
            ["Direction"] = ((int)PaymentDirection.Received).ToString(),
            ["CurrencyCode"] = "USD"
        });

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("dialog.modal");
    }

    [Fact]
    public async Task CreatePayment_WithMissingDate_ReturnsFormWithErrors()
    {
        var bookingId = await SeedBookingAsync();
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/payments/new/{bookingId}");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Amount"] = "500",
            ["PaymentDate"] = "",
            ["PaymentMethod"] = ((int)PaymentMethod.Cash).ToString(),
            ["Direction"] = ((int)PaymentDirection.Received).ToString(),
            ["CurrencyCode"] = "USD"
        });

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("dialog.modal");
    }

    [Fact]
    public async Task DeletePayment_RemovesFromDatabase()
    {
        var (bookingId, paymentId) = await SeedBookingWithPaymentAsync();

        var listResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/payments/{bookingId}");
        listResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(listResponse, $"form[hx-post='/{TenantSlug}/bookings/payments/delete/{paymentId}']", new Dictionary<string, string>());

        response.AssertSuccess();
        response.AssertToast("Payment removed.");
        response.AssertTrigger("bookings.payments.refresh");

        await using var db = OpenTenantDb();
        Assert.False(await db.Set<BookingPayment>().AnyAsync(p => p.Id == paymentId));
    }

    [Fact]
    public async Task SupplierPaymentsPartial_RendersWithoutLayout()
    {
        var (_, itemId) = await SeedBookingWithItemAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/items/payments/{itemId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("No supplier payments recorded yet.");
    }

    [Fact]
    public async Task NewSupplierPaymentPartial_RendersModalForm()
    {
        var (_, itemId) = await SeedBookingWithItemAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/items/payments/new/{itemId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("dialog.modal");
        await response.AssertContainsAsync("Record Supplier Payment");
    }

    [Fact]
    public async Task CreateSupplierPayment_OnValidSubmit_PersistsAndReturnsToast()
    {
        var (_, itemId, supplierId) = await SeedBookingWithItemReturnAllAsync();
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/items/payments/new/{itemId}");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Amount"] = "400.00",
            ["PaymentDate"] = "2026-03-12",
            ["PaymentMethod"] = ((int)PaymentMethod.BankTransfer).ToString(),
            ["Direction"] = ((int)PaymentDirection.Paid).ToString(),
            ["Reference"] = "SUP-PAY-001",
            ["Notes"] = "Supplier deposit",
            ["CurrencyCode"] = "USD",
            ["SupplierId"] = supplierId.ToString()
        });

        response.AssertSuccess();
        response.AssertToast("Supplier payment recorded.");
        response.AssertTrigger("bookings.supplier-payments.refresh");

        await using var db = OpenTenantDb();
        var payment = await db.Set<SupplierPayment>().SingleAsync(p => p.BookingItemId == itemId && p.Reference == "SUP-PAY-001");
        Assert.Equal(400m, payment.Amount);
        Assert.Equal(new DateOnly(2026, 3, 12), payment.PaymentDate);
        Assert.Equal(PaymentMethod.BankTransfer, payment.PaymentMethod);
        Assert.Equal(PaymentDirection.Paid, payment.Direction);
        Assert.Equal("Supplier deposit", payment.Notes);
        Assert.NotNull(payment.CreatedAt);
    }

    [Fact]
    public async Task CreateSupplierPayment_WithInvalidAmount_ReturnsFormWithErrors()
    {
        var (_, itemId, supplierId) = await SeedBookingWithItemReturnAllAsync();
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/items/payments/new/{itemId}");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Amount"] = "0",
            ["PaymentDate"] = "2026-03-12",
            ["PaymentMethod"] = ((int)PaymentMethod.Cash).ToString(),
            ["Direction"] = ((int)PaymentDirection.Paid).ToString(),
            ["CurrencyCode"] = "USD",
            ["SupplierId"] = supplierId.ToString()
        });

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("dialog.modal");
    }

    [Fact]
    public async Task PaymentRoutes_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var bookingId = await SeedBookingAsync();

        var response = await publicClient.GetAsync($"/{TenantSlug}/bookings/payments/{bookingId}");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }

    // --- helpers ---

    private async Task<Guid> SeedBookingAsync()
    {
        await using var db = OpenTenantDb();
        var clientId = await db.Clients.OrderBy(x => x.Name).Select(x => x.Id).FirstAsync();
        var booking = new Booking
        {
            BookingRef = $"BK-P-{Guid.NewGuid():N}"[..13],
            ClientId = clientId,
            Pax = 2,
            TravelStartDate = new DateOnly(2026, 6, 1),
            TravelEndDate = new DateOnly(2026, 6, 5),
            CostCurrencyCode = "USD",
            SellingCurrencyCode = "USD",
            TotalSelling = 2000m,
            CreatedAt = DateTime.UtcNow
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    private async Task<(Guid BookingId, Guid PaymentId)> SeedBookingWithPaymentAsync()
    {
        await using var db = OpenTenantDb();
        var clientId = await db.Clients.OrderBy(x => x.Name).Select(x => x.Id).FirstAsync();
        var booking = new Booking
        {
            BookingRef = $"BK-PP-{Guid.NewGuid():N}"[..13],
            ClientId = clientId,
            Pax = 2,
            TravelStartDate = new DateOnly(2026, 7, 1),
            TravelEndDate = new DateOnly(2026, 7, 5),
            CostCurrencyCode = "USD",
            SellingCurrencyCode = "USD",
            TotalSelling = 2000m,
            CreatedAt = DateTime.UtcNow
        };
        var payment = new BookingPayment
        {
            BookingId = booking.Id,
            Amount = 500m,
            CurrencyCode = "USD",
            PaymentDate = new DateOnly(2026, 3, 1),
            PaymentMethod = PaymentMethod.BankTransfer,
            Direction = PaymentDirection.Received,
            Reference = "DEP-001",
            CreatedAt = DateTime.UtcNow
        };
        db.Bookings.Add(booking);
        db.Set<BookingPayment>().Add(payment);
        await db.SaveChangesAsync();
        return (booking.Id, payment.Id);
    }

    private async Task<(Guid BookingId, Guid ItemId)> SeedBookingWithItemAsync()
    {
        await using var db = OpenTenantDb();
        var clientId = await db.Clients.OrderBy(x => x.Name).Select(x => x.Id).FirstAsync();
        var supplier = new Supplier
        {
            Name = $"ZZ-PaySup {Guid.NewGuid():N}"[..18],
            ContactEmail = "pay-supplier@test.local",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var inventory = new InventoryItem
        {
            Name = $"ZZ-PaySvc {Guid.NewGuid():N}"[..18],
            Kind = InventoryItemKind.Hotel,
            BaseCost = 800m,
            Supplier = supplier,
            CreatedAt = DateTime.UtcNow
        };
        db.InventoryItems.Add(inventory);

        var booking = new Booking
        {
            BookingRef = $"BK-PI-{Guid.NewGuid():N}"[..13],
            ClientId = clientId,
            Pax = 2,
            TravelStartDate = new DateOnly(2026, 8, 1),
            TravelEndDate = new DateOnly(2026, 8, 5),
            CostCurrencyCode = "USD",
            SellingCurrencyCode = "USD",
            CreatedAt = DateTime.UtcNow
        };
        var item = new BookingItem
        {
            InventoryItemId = inventory.Id,
            SupplierId = supplier.Id,
            ServiceName = inventory.Name,
            ServiceKind = inventory.Kind,
            CostPrice = 800m,
            SellingPrice = 900m,
            CostCurrencyCode = "USD",
            SellingCurrencyCode = "USD",
            Quantity = 1,
            Pax = 2,
            ServiceDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 5),
            Nights = 4
        };
        booking.Items.Add(item);
        booking.TotalCost = 800m;
        booking.TotalSelling = 900m;
        booking.TotalProfit = 100m;
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return (booking.Id, item.Id);
    }

    private async Task<(Guid BookingId, Guid ItemId, Guid SupplierId)> SeedBookingWithItemReturnAllAsync()
    {
        await using var db = OpenTenantDb();
        var clientId = await db.Clients.OrderBy(x => x.Name).Select(x => x.Id).FirstAsync();
        var supplier = new Supplier
        {
            Name = $"ZZ-PSup {Guid.NewGuid():N}"[..18],
            ContactEmail = "pay-sup@test.local",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var inventory = new InventoryItem
        {
            Name = $"ZZ-PSvc {Guid.NewGuid():N}"[..18],
            Kind = InventoryItemKind.Hotel,
            BaseCost = 600m,
            Supplier = supplier,
            CreatedAt = DateTime.UtcNow
        };
        db.InventoryItems.Add(inventory);

        var booking = new Booking
        {
            BookingRef = $"BK-PS-{Guid.NewGuid():N}"[..13],
            ClientId = clientId,
            Pax = 2,
            TravelStartDate = new DateOnly(2026, 9, 1),
            TravelEndDate = new DateOnly(2026, 9, 5),
            CostCurrencyCode = "USD",
            SellingCurrencyCode = "USD",
            CreatedAt = DateTime.UtcNow
        };
        var item = new BookingItem
        {
            InventoryItemId = inventory.Id,
            SupplierId = supplier.Id,
            ServiceName = inventory.Name,
            ServiceKind = inventory.Kind,
            CostPrice = 600m,
            SellingPrice = 700m,
            CostCurrencyCode = "USD",
            SellingCurrencyCode = "USD",
            Quantity = 1,
            Pax = 2,
            ServiceDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 9, 5),
            Nights = 4
        };
        booking.Items.Add(item);
        booking.TotalCost = 600m;
        booking.TotalSelling = 700m;
        booking.TotalProfit = 100m;
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return (booking.Id, item.Id, supplier.Id);
    }

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }
}
