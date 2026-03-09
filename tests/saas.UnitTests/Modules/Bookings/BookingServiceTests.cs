using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Bookings.DTOs;
using saas.Modules.Bookings.Entities;
using saas.Modules.Bookings.Services;
using saas.Modules.Clients.Entities;
using saas.Modules.Inventory.Entities;
using saas.Modules.Settings.Entities;
using Xunit;

namespace saas.Tests.Modules.Bookings;

public class BookingServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TenantDbContext _db = null!;
    private BookingService _service = null!;
    private Client _client = null!;
    private InventoryItem _hotel = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TenantDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        var supplier = new Supplier { Name = "Al Haram Hotels", IsActive = true, CreatedAt = DateTime.UtcNow };
        var destination = new Destination { Name = "Makkah", IsActive = true, SortOrder = 10, CreatedAt = DateTime.UtcNow };
        _client = new Client { Name = "Acacia Travel Group", Email = "hello@test.com", CreatedAt = DateTime.UtcNow };
        _hotel = new InventoryItem
        {
            Name = "Grand Haram Hotel",
            Kind = InventoryItemKind.Hotel,
            BaseCost = 500m,
            Supplier = supplier,
            Destination = destination,
            CreatedAt = DateTime.UtcNow
        };

        _db.Clients.Add(_client);
        _db.InventoryItems.Add(_hotel);
        _db.Currencies.AddRange(
            new Currency { Code = "USD", Name = "US Dollar", Symbol = "$", ExchangeRate = 1m, IsBaseCurrency = true, IsActive = true, CreatedAt = DateTime.UtcNow },
            new Currency { Code = "SAR", Name = "Saudi Riyal", Symbol = "SAR", ExchangeRate = 3.75m, IsBaseCurrency = false, IsActive = true, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        _service = new BookingService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task CreateEmptyAsync_LoadsClientAndCurrencyOptions()
    {
        var dto = await _service.CreateEmptyAsync();

        Assert.Contains(dto.ClientOptions, x => x.Label == _client.Name);
        Assert.Contains("USD", dto.CurrencyOptions);
    }

    [Fact]
    public async Task CreateAsync_GeneratesIncrementingBookingReference()
    {
        var firstId = await _service.CreateAsync(new BookingFormDto
        {
            ClientId = _client.Id,
            SellingCurrencyCode = "USD",
            Pax = 2
        });

        var secondId = await _service.CreateAsync(new BookingFormDto
        {
            ClientId = _client.Id,
            SellingCurrencyCode = "USD",
            Pax = 4
        });

        var first = await _db.Bookings.SingleAsync(x => x.Id == firstId);
        var second = await _db.Bookings.SingleAsync(x => x.Id == secondId);
        Assert.EndsWith("0001", first.BookingRef);
        Assert.EndsWith("0002", second.BookingRef);
    }

    [Fact]
    public async Task AddItemAsync_UsesInventoryBaseCostAndRecalculatesTotals()
    {
        var bookingId = await _service.CreateAsync(new BookingFormDto
        {
            ClientId = _client.Id,
            SellingCurrencyCode = "USD",
            Pax = 2
        });

        await _service.AddItemAsync(bookingId, new BookingItemFormDto
        {
            BookingId = bookingId,
            InventoryItemId = _hotel.Id,
            ServiceDate = new DateOnly(2026, 4, 10),
            EndDate = new DateOnly(2026, 4, 13),
            Quantity = 2,
            Pax = 2,
            SellingPrice = 650m,
            SupplierReference = "SUP-001"
        });

        var booking = await _db.Bookings.Include(x => x.Items).SingleAsync(x => x.Id == bookingId);
        var item = Assert.Single(booking.Items);
        Assert.Equal(500m, item.CostPrice);
        Assert.Equal(650m, item.SellingPrice);
        Assert.Equal(3, item.Nights);
        Assert.Equal(1000m, booking.TotalCost);
        Assert.Equal(1300m, booking.TotalSelling);
        Assert.Equal(300m, booking.TotalProfit);
    }

    [Fact]
    public async Task UpdateItemStatusAsync_WhenAllItemsConfirmed_ConfirmsBooking()
    {
        var bookingId = await _service.CreateAsync(new BookingFormDto
        {
            ClientId = _client.Id,
            SellingCurrencyCode = "USD",
            Pax = 2
        });

        await _service.AddItemAsync(bookingId, new BookingItemFormDto
        {
            BookingId = bookingId,
            InventoryItemId = _hotel.Id,
            ServiceDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2026, 5, 3),
            Quantity = 1,
            Pax = 2,
            SellingPrice = 600m
        });

        var booking = await _db.Bookings.Include(x => x.Items).SingleAsync(x => x.Id == bookingId);
        var item = booking.Items.Single();

        await _service.UpdateItemStatusAsync(bookingId, item.Id, SupplierStatus.Requested);
        await _service.UpdateItemStatusAsync(bookingId, item.Id, SupplierStatus.Confirmed);

        var updated = await _db.Bookings.Include(x => x.Items).SingleAsync(x => x.Id == bookingId);
        Assert.Equal(BookingStatus.Confirmed, updated.Status);
        Assert.NotNull(updated.ConfirmedAt);
        Assert.Equal(SupplierStatus.Confirmed, updated.Items.Single().SupplierStatus);
    }

    [Fact]
    public async Task GetListAsync_SearchMatchesBookingReferenceAndClient()
    {
        var bookingId = await _service.CreateAsync(new BookingFormDto
        {
            ClientId = _client.Id,
            ClientReference = "VIP-OPS",
            SellingCurrencyCode = "USD",
            Pax = 3
        });

        var booking = await _db.Bookings.SingleAsync(x => x.Id == bookingId);

        var byRef = await _service.GetListAsync(search: booking.BookingRef);
        var byClient = await _service.GetListAsync(search: _client.Name);

        Assert.Single(byRef.Items);
        Assert.Single(byClient.Items);
        Assert.Equal(bookingId, byRef.Items[0].Id);
        Assert.Equal(bookingId, byClient.Items[0].Id);
    }
}
