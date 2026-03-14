using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Bookings.DTOs;
using saas.Modules.Bookings.Entities;
using saas.Modules.Bookings.Services;
using saas.Modules.Branding.Entities;
using saas.Modules.Clients.Entities;
using Xunit;

namespace saas.UnitTests.Modules.Bookings;

public class PaymentLinkServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;
    private readonly PaymentLinkService _service;

    public PaymentLinkServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();
        _service = new PaymentLinkService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<(Guid BookingId, Guid ClientId)> SeedBookingAsync()
    {
        var client = new Client { Name = "Test Client", CreatedAt = DateTime.UtcNow };
        _db.Clients.Add(client);
        var booking = new Booking
        {
            BookingRef = "BK-TEST001",
            ClientId = client.Id,
            Pax = 2,
            TotalSelling = 5000m,
            SellingCurrencyCode = "USD",
            CostCurrencyCode = "USD",
            TravelStartDate = new DateOnly(2026, 6, 1),
            CreatedAt = DateTime.UtcNow
        };
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();
        return (booking.Id, client.Id);
    }

    [Fact]
    public async Task GetByBookingAsync_ReturnsEmptyWhenNoLinks()
    {
        var (bookingId, _) = await SeedBookingAsync();

        var result = await _service.GetByBookingAsync(bookingId);

        Assert.NotNull(result);
        Assert.Empty(result.Links);
        Assert.Equal("BK-TEST001", result.BookingRef);
    }

    [Fact]
    public async Task GetByBookingAsync_ReturnsNull_WhenBookingNotFound()
    {
        var result = await _service.GetByBookingAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_GeneratesTokenAndSavesLink()
    {
        var (bookingId, clientId) = await SeedBookingAsync();
        var dto = new PaymentLinkFormDto
        {
            BookingId = bookingId,
            Amount = 1500m,
            CurrencyCode = "USD",
            Description = "Deposit payment",
            ExpiryDays = 7
        };

        var link = await _service.CreateAsync(bookingId, dto, "user-123", "demo");

        Assert.NotNull(link);
        Assert.NotEmpty(link.Token);
        Assert.Equal(1500m, link.Amount);
        Assert.Equal("USD", link.CurrencyCode);
        Assert.Equal("Deposit payment", link.Description);
        Assert.Equal(PaymentLinkStatus.Pending, link.Status);
        Assert.Equal(clientId, link.ClientId);
        Assert.Equal("user-123", link.CreatedByUserId);
        Assert.Equal("demo", link.TenantSlug);
        Assert.True(link.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenBookingNotFound()
    {
        var dto = new PaymentLinkFormDto { Amount = 100m, CurrencyCode = "USD", ExpiryDays = 7 };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateAsync(Guid.NewGuid(), dto, "user-1", "demo"));
    }

    [Fact]
    public async Task GetByBookingAsync_ReturnsLinks()
    {
        var (bookingId, _) = await SeedBookingAsync();
        var dto = new PaymentLinkFormDto { Amount = 2000m, CurrencyCode = "USD", Description = "Test", ExpiryDays = 7 };
        await _service.CreateAsync(bookingId, dto, "user-1", "demo");

        var result = await _service.GetByBookingAsync(bookingId);

        Assert.NotNull(result);
        Assert.Single(result.Links);
        Assert.Equal(2000m, result.Links[0].Amount);
    }

    [Fact]
    public async Task CancelAsync_SetsStatusToCancelled()
    {
        var (bookingId, _) = await SeedBookingAsync();
        var dto = new PaymentLinkFormDto { Amount = 500m, CurrencyCode = "USD", ExpiryDays = 7 };
        var link = await _service.CreateAsync(bookingId, dto, "user-1", "demo");

        var result = await _service.CancelAsync(link.Id);

        Assert.True(result);
        var updated = await _db.PaymentLinks.FindAsync(link.Id);
        Assert.Equal(PaymentLinkStatus.Cancelled, updated!.Status);
    }

    [Fact]
    public async Task CancelAsync_ReturnsFalse_WhenAlreadyPaid()
    {
        var (bookingId, _) = await SeedBookingAsync();
        var dto = new PaymentLinkFormDto { Amount = 500m, CurrencyCode = "USD", ExpiryDays = 7 };
        var link = await _service.CreateAsync(bookingId, dto, "user-1", "demo");

        // Manually mark as paid
        link.Status = PaymentLinkStatus.Paid;
        await _db.SaveChangesAsync();

        var result = await _service.CancelAsync(link.Id);
        Assert.False(result);
    }

    [Fact]
    public async Task CancelAsync_ReturnsFalse_WhenNotFound()
    {
        var result = await _service.CancelAsync(Guid.NewGuid());
        Assert.False(result);
    }

    [Fact]
    public async Task GetByTokenAsync_ReturnsLinkWithBranding()
    {
        var (bookingId, _) = await SeedBookingAsync();
        _db.BrandingSettings.Add(new BrandingSettings
        {
            AgencyName = "Safari Agency",
            PrimaryColor = "#FF0000",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var dto = new PaymentLinkFormDto { Amount = 3000m, CurrencyCode = "EUR", Description = "Full payment", ExpiryDays = 14 };
        var link = await _service.CreateAsync(bookingId, dto, "user-1", "demo");

        var result = await _service.GetByTokenAsync(link.Token);

        Assert.NotNull(result);
        Assert.Equal(3000m, result.Amount);
        Assert.Equal("EUR", result.CurrencyCode);
        Assert.Equal("Full payment", result.Description);
        Assert.Equal("BK-TEST001", result.BookingRef);
        Assert.Equal("Test Client", result.ClientName);
        Assert.Equal("Safari Agency", result.AgencyName);
        Assert.Equal("#FF0000", result.PrimaryColor);
        Assert.Equal(PaymentLinkStatus.Pending, result.Status);
    }

    [Fact]
    public async Task GetByTokenAsync_ReturnsNull_WhenTokenNotFound()
    {
        var result = await _service.GetByTokenAsync("nonexistent-token");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByTokenAsync_ShowsExpiredStatus_WhenExpired()
    {
        var (bookingId, _) = await SeedBookingAsync();
        var dto = new PaymentLinkFormDto { Amount = 100m, CurrencyCode = "USD", ExpiryDays = 1 };
        var link = await _service.CreateAsync(bookingId, dto, "user-1", "demo");

        // Manually set expiry to past
        link.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();

        var result = await _service.GetByTokenAsync(link.Token);
        Assert.NotNull(result);
        Assert.Equal(PaymentLinkStatus.Expired, result.Status);
    }

    [Fact]
    public async Task MarkAsPaidAsync_SetsStatusAndCreatesPayment()
    {
        var (bookingId, _) = await SeedBookingAsync();
        var dto = new PaymentLinkFormDto { Amount = 2500m, CurrencyCode = "USD", Description = "Deposit", ExpiryDays = 7 };
        var link = await _service.CreateAsync(bookingId, dto, "user-1", "demo");

        var result = await _service.MarkAsPaidAsync(link.Token);

        Assert.True(result);

        var updated = await _db.PaymentLinks.FindAsync(link.Id);
        Assert.Equal(PaymentLinkStatus.Paid, updated!.Status);
        Assert.NotNull(updated.PaidAt);

        // Verify BookingPayment was created
        var payment = await _db.BookingPayments.FirstOrDefaultAsync(p => p.BookingId == bookingId);
        Assert.NotNull(payment);
        Assert.Equal(2500m, payment.Amount);
        Assert.Equal(PaymentMethod.Online, payment.PaymentMethod);
        Assert.Equal(PaymentDirection.Received, payment.Direction);
    }

    [Fact]
    public async Task MarkAsPaidAsync_ReturnsFalse_WhenAlreadyPaid()
    {
        var (bookingId, _) = await SeedBookingAsync();
        var dto = new PaymentLinkFormDto { Amount = 500m, CurrencyCode = "USD", ExpiryDays = 7 };
        var link = await _service.CreateAsync(bookingId, dto, "user-1", "demo");

        await _service.MarkAsPaidAsync(link.Token); // first pay
        var result = await _service.MarkAsPaidAsync(link.Token); // second pay
        Assert.False(result);
    }

    [Fact]
    public async Task MarkAsPaidAsync_ReturnsFalse_WhenExpired()
    {
        var (bookingId, _) = await SeedBookingAsync();
        var dto = new PaymentLinkFormDto { Amount = 500m, CurrencyCode = "USD", ExpiryDays = 1 };
        var link = await _service.CreateAsync(bookingId, dto, "user-1", "demo");

        link.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();

        var result = await _service.MarkAsPaidAsync(link.Token);
        Assert.False(result);
    }

    [Fact]
    public async Task MarkAsPaidAsync_ReturnsFalse_WhenTokenNotFound()
    {
        var result = await _service.MarkAsPaidAsync("nonexistent-token");
        Assert.False(result);
    }

    [Fact]
    public async Task CreateEmptyFormAsync_LoadsCurrency()
    {
        var (bookingId, _) = await SeedBookingAsync();

        var result = await _service.CreateEmptyFormAsync(bookingId);

        Assert.Equal(bookingId, result.BookingId);
        Assert.Equal("USD", result.CurrencyCode);
        Assert.Equal(7, result.ExpiryDays);
    }

    [Fact]
    public async Task GetByBookingAsync_MarksExpiredLinksInMemory()
    {
        var (bookingId, _) = await SeedBookingAsync();
        var dto = new PaymentLinkFormDto { Amount = 100m, CurrencyCode = "USD", ExpiryDays = 1 };
        var link = await _service.CreateAsync(bookingId, dto, "user-1", "demo");

        link.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();

        var result = await _service.GetByBookingAsync(bookingId);
        Assert.NotNull(result);
        Assert.Single(result.Links);
        Assert.Equal(PaymentLinkStatus.Expired, result.Links[0].Status);

        // Verify DB entity still says Pending (not mutated in DB)
        var dbLink = await _db.PaymentLinks.AsNoTracking().FirstAsync(l => l.Id == link.Id);
        Assert.Equal(PaymentLinkStatus.Pending, dbLink.Status);
    }
}
