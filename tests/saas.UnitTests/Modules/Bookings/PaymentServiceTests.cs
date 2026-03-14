using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Bookings.DTOs;
using saas.Modules.Bookings.Entities;
using saas.Modules.Bookings.Services;
using saas.Modules.Clients.Entities;
using saas.Modules.Settings.Entities;
using Xunit;

namespace saas.UnitTests.Modules.Bookings;

public class PaymentServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;
    private readonly PaymentService _service;

    public PaymentServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();
        _service = new PaymentService(_db);
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
            BookingRef = $"BK-{Guid.NewGuid():N}"[..12],
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

    private async Task<(Guid BookingId, Guid ItemId, Guid SupplierId)> SeedBookingWithItemAsync()
    {
        var client = new Client { Name = "Test Client", CreatedAt = DateTime.UtcNow };
        _db.Clients.Add(client);
        var supplier = new Supplier { Name = "Test Supplier", ContactEmail = "s@test.local", IsActive = true, CreatedAt = DateTime.UtcNow };
        _db.Suppliers.Add(supplier);
        var booking = new Booking
        {
            BookingRef = $"BK-{Guid.NewGuid():N}"[..12],
            ClientId = client.Id,
            Pax = 2,
            TotalSelling = 5000m,
            TotalCost = 3000m,
            SellingCurrencyCode = "USD",
            CostCurrencyCode = "USD",
            CreatedAt = DateTime.UtcNow
        };
        var item = new BookingItem
        {
            BookingId = booking.Id,
            SupplierId = supplier.Id,
            ServiceName = "Test Hotel",
            CostPrice = 3000m,
            SellingPrice = 5000m,
            CostCurrencyCode = "USD",
            SellingCurrencyCode = "USD",
            Quantity = 1,
            Pax = 2
        };
        booking.Items.Add(item);
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();
        return (booking.Id, item.Id, supplier.Id);
    }

    [Fact]
    public async Task GetBookingPaymentsAsync_ReturnsEmptyWhenNoPayments()
    {
        var (bookingId, _) = await SeedBookingAsync();

        var result = await _service.GetBookingPaymentsAsync(bookingId);

        Assert.NotNull(result);
        Assert.Empty(result.Payments);
        Assert.Equal(5000m, result.TotalSelling);
        Assert.Equal(0m, result.TotalReceived);
        Assert.Equal(5000m, result.ClientBalance);
    }

    [Fact]
    public async Task GetBookingPaymentsAsync_ReturnsNullForMissingBooking()
    {
        var result = await _service.GetBookingPaymentsAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateBookingPaymentAsync_PersistsPayment()
    {
        var (bookingId, _) = await SeedBookingAsync();

        var paymentId = await _service.CreateBookingPaymentAsync(bookingId, new BookingPaymentFormDto
        {
            BookingId = bookingId,
            Amount = 2000m,
            PaymentDate = new DateOnly(2026, 5, 15),
            PaymentMethod = PaymentMethod.BankTransfer,
            Direction = PaymentDirection.Received,
            Reference = "TXN-001",
            CurrencyCode = "USD"
        });

        var payment = await _db.BookingPayments.FindAsync(paymentId);
        Assert.NotNull(payment);
        Assert.Equal(2000m, payment.Amount);
        Assert.Equal(PaymentDirection.Received, payment.Direction);
        Assert.Equal("TXN-001", payment.Reference);
    }

    [Fact]
    public async Task GetBookingPaymentsAsync_CalculatesBalanceCorrectly()
    {
        var (bookingId, _) = await SeedBookingAsync();

        await _service.CreateBookingPaymentAsync(bookingId, new BookingPaymentFormDto
        {
            BookingId = bookingId,
            Amount = 2000m,
            PaymentDate = new DateOnly(2026, 5, 10),
            Direction = PaymentDirection.Received,
            CurrencyCode = "USD"
        });

        await _service.CreateBookingPaymentAsync(bookingId, new BookingPaymentFormDto
        {
            BookingId = bookingId,
            Amount = 1500m,
            PaymentDate = new DateOnly(2026, 5, 12),
            Direction = PaymentDirection.Received,
            CurrencyCode = "USD"
        });

        var result = await _service.GetBookingPaymentsAsync(bookingId);

        Assert.NotNull(result);
        Assert.Equal(2, result.Payments.Count);
        Assert.Equal(3500m, result.TotalReceived);
        Assert.Equal(1500m, result.ClientBalance);
    }

    [Fact]
    public async Task GetBookingPaymentsAsync_DeductsRefundsFromTotalReceived()
    {
        var (bookingId, _) = await SeedBookingAsync();

        await _service.CreateBookingPaymentAsync(bookingId, new BookingPaymentFormDto
        {
            BookingId = bookingId,
            Amount = 3000m,
            PaymentDate = new DateOnly(2026, 5, 10),
            Direction = PaymentDirection.Received,
            CurrencyCode = "USD"
        });

        await _service.CreateBookingPaymentAsync(bookingId, new BookingPaymentFormDto
        {
            BookingId = bookingId,
            Amount = 500m,
            PaymentDate = new DateOnly(2026, 5, 15),
            Direction = PaymentDirection.Refunded,
            CurrencyCode = "USD"
        });

        var result = await _service.GetBookingPaymentsAsync(bookingId);

        Assert.Equal(2500m, result!.TotalReceived);
        Assert.Equal(2500m, result.ClientBalance);
    }

    [Fact]
    public async Task DeleteBookingPaymentAsync_RemovesPayment()
    {
        var (bookingId, _) = await SeedBookingAsync();
        var paymentId = await _service.CreateBookingPaymentAsync(bookingId, new BookingPaymentFormDto
        {
            BookingId = bookingId,
            Amount = 1000m,
            PaymentDate = new DateOnly(2026, 5, 10),
            Direction = PaymentDirection.Received,
            CurrencyCode = "USD"
        });

        var deleted = await _service.DeleteBookingPaymentAsync(paymentId);

        Assert.True(deleted);
        Assert.False(await _db.BookingPayments.AnyAsync(p => p.Id == paymentId));
    }

    [Fact]
    public async Task DeleteBookingPaymentAsync_ReturnsFalseForMissing()
    {
        var deleted = await _service.DeleteBookingPaymentAsync(Guid.NewGuid());
        Assert.False(deleted);
    }

    [Fact]
    public async Task GetSupplierPaymentsAsync_ReturnsEmptyWhenNoPayments()
    {
        var (_, itemId, _) = await SeedBookingWithItemAsync();

        var result = await _service.GetSupplierPaymentsAsync(itemId);

        Assert.NotNull(result);
        Assert.Empty(result.Payments);
        Assert.Equal(3000m, result.CostPrice);
        Assert.Equal(0m, result.TotalPaid);
        Assert.Equal(3000m, result.SupplierBalance);
    }

    [Fact]
    public async Task CreateSupplierPaymentAsync_PersistsPayment()
    {
        var (_, itemId, supplierId) = await SeedBookingWithItemAsync();

        var paymentId = await _service.CreateSupplierPaymentAsync(itemId, new SupplierPaymentFormDto
        {
            BookingItemId = itemId,
            SupplierId = supplierId,
            Amount = 1500m,
            PaymentDate = new DateOnly(2026, 5, 20),
            PaymentMethod = PaymentMethod.BankTransfer,
            Direction = PaymentDirection.Paid,
            Reference = "SUP-001",
            CurrencyCode = "USD"
        });

        var payment = await _db.SupplierPayments.FindAsync(paymentId);
        Assert.NotNull(payment);
        Assert.Equal(1500m, payment.Amount);
        Assert.Equal(PaymentDirection.Paid, payment.Direction);
        Assert.Equal(supplierId, payment.SupplierId);
    }

    [Fact]
    public async Task GetSupplierPaymentsAsync_CalculatesBalanceCorrectly()
    {
        var (_, itemId, supplierId) = await SeedBookingWithItemAsync();

        await _service.CreateSupplierPaymentAsync(itemId, new SupplierPaymentFormDto
        {
            BookingItemId = itemId,
            SupplierId = supplierId,
            Amount = 2000m,
            PaymentDate = new DateOnly(2026, 5, 20),
            Direction = PaymentDirection.Paid,
            CurrencyCode = "USD"
        });

        var result = await _service.GetSupplierPaymentsAsync(itemId);

        Assert.NotNull(result);
        Assert.Single(result.Payments);
        Assert.Equal(2000m, result.TotalPaid);
        Assert.Equal(1000m, result.SupplierBalance);
    }

    [Fact]
    public async Task GetSupplierPaymentsAsync_DeductsReceivedFromTotalPaid()
    {
        var (_, itemId, supplierId) = await SeedBookingWithItemAsync();

        await _service.CreateSupplierPaymentAsync(itemId, new SupplierPaymentFormDto
        {
            BookingItemId = itemId,
            SupplierId = supplierId,
            Amount = 3000m,
            PaymentDate = new DateOnly(2026, 5, 20),
            Direction = PaymentDirection.Paid,
            CurrencyCode = "USD"
        });

        await _service.CreateSupplierPaymentAsync(itemId, new SupplierPaymentFormDto
        {
            BookingItemId = itemId,
            SupplierId = supplierId,
            Amount = 500m,
            PaymentDate = new DateOnly(2026, 5, 25),
            Direction = PaymentDirection.Received,
            CurrencyCode = "USD"
        });

        var result = await _service.GetSupplierPaymentsAsync(itemId);

        Assert.Equal(2500m, result!.TotalPaid);
        Assert.Equal(500m, result.SupplierBalance);
    }

    [Fact]
    public async Task CreateEmptyBookingPaymentAsync_DefaultsToBookingCurrency()
    {
        var (bookingId, _) = await SeedBookingAsync();

        var dto = await _service.CreateEmptyBookingPaymentAsync(bookingId);

        Assert.Equal(bookingId, dto.BookingId);
        Assert.Equal("USD", dto.CurrencyCode);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), dto.PaymentDate);
    }

    [Fact]
    public async Task CreateEmptySupplierPaymentAsync_ReturnsNullWhenNoSupplier()
    {
        var client = new Client { Name = "Test Client", CreatedAt = DateTime.UtcNow };
        _db.Clients.Add(client);
        var booking = new Booking
        {
            BookingRef = "BK-NO-SUP",
            ClientId = client.Id,
            Pax = 1,
            TotalSelling = 1000m,
            CostCurrencyCode = "USD",
            SellingCurrencyCode = "USD",
            CreatedAt = DateTime.UtcNow
        };
        var item = new BookingItem
        {
            BookingId = booking.Id,
            SupplierId = null,
            ServiceName = "No Supplier Service",
            CostPrice = 500m,
            SellingPrice = 1000m,
            CostCurrencyCode = "USD",
            SellingCurrencyCode = "USD",
            Quantity = 1,
            Pax = 1
        };
        booking.Items.Add(item);
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        var dto = await _service.CreateEmptySupplierPaymentAsync(item.Id);

        Assert.Null(dto);
    }
}
