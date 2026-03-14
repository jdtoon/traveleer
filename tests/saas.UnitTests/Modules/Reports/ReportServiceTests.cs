using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Bookings.Entities;
using saas.Modules.Clients.Entities;
using saas.Modules.Quotes.Entities;
using saas.Modules.Reports.Entities;
using saas.Modules.Reports.Services;
using saas.Modules.Settings.Entities;
using saas.Modules.Suppliers.Entities;
using Xunit;

namespace saas.Tests.Modules.Reports;

public class ReportServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TenantDbContext _db = null!;
    private ReportService _service = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TenantDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        // Seed test data
        var client = new Client { Name = "Acme Corp", CreatedAt = DateTime.UtcNow };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        var supplier = new Supplier { Name = "Safari Lodge", IsActive = true, CreatedAt = DateTime.UtcNow };
        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync();

        // Bookings: one confirmed, one cancelled
        _db.Bookings.AddRange(
            new Booking
            {
                BookingRef = "BK-001",
                ClientId = client.Id,
                Status = BookingStatus.Confirmed,
                TotalSelling = 5000m,
                TotalCost = 3000m,
                TotalProfit = 2000m,
                CreatedAt = DateTime.UtcNow
            },
            new Booking
            {
                BookingRef = "BK-002",
                ClientId = client.Id,
                Status = BookingStatus.Cancelled,
                TotalSelling = 2000m,
                TotalCost = 1500m,
                TotalProfit = 500m,
                CreatedAt = DateTime.UtcNow
            });
        await _db.SaveChangesAsync();

        // Add a booking item linked to the supplier
        var booking = await _db.Bookings.FirstAsync(b => b.BookingRef == "BK-001");
        _db.BookingItems.Add(new BookingItem
        {
            BookingId = booking.Id,
            SupplierId = supplier.Id,
            ServiceName = "Game Drive",
            CostPrice = 500m,
            SellingPrice = 800m,
            Quantity = 2,
            Pax = 2
        });
        await _db.SaveChangesAsync();

        // Quotes
        _db.Set<Quote>().AddRange(
            new Quote { ReferenceNumber = "Q-001", ClientName = "Acme Corp", Status = QuoteStatus.Sent, CreatedAt = DateTime.UtcNow },
            new Quote { ReferenceNumber = "Q-002", ClientName = "Acme Corp", Status = QuoteStatus.Accepted, CreatedAt = DateTime.UtcNow },
            new Quote { ReferenceNumber = "Q-003", ClientName = "Other", Status = QuoteStatus.Draft, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        _service = new ReportService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task GetDashboardAsync_ReturnsAllWidgets()
    {
        var dashboard = await _service.GetDashboardAsync("user-1", "month");

        Assert.Equal("month", dashboard.DateRange);
        Assert.Equal(10, dashboard.Widgets.Count);
        Assert.All(dashboard.Widgets, w => Assert.True(w.IsVisible));
    }

    [Fact]
    public async Task GetRevenueMonthlyAsync_ExcludesCancelledBookings()
    {
        var result = await _service.GetRevenueMonthlyAsync("month");

        Assert.Single(result);
        Assert.Equal(5000m, result[0].Total); // Only the confirmed booking
    }

    [Fact]
    public async Task GetBookingsByStatusAsync_GroupsByStatus()
    {
        var result = await _service.GetBookingsByStatusAsync("month");

        Assert.Equal(2, result.Count);
        Assert.Contains(result, b => b.Status == "Confirmed" && b.Count == 1);
        Assert.Contains(result, b => b.Status == "Cancelled" && b.Count == 1);
    }

    [Fact]
    public async Task GetRecentBookingsAsync_ReturnsMostRecent()
    {
        var result = await _service.GetRecentBookingsAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("Acme Corp", result[0].ClientName);
    }

    [Fact]
    public async Task GetQuoteConversionAsync_CalculatesRate()
    {
        // We have 2 non-draft quotes (Sent + Accepted), 1 accepted
        var result = await _service.GetQuoteConversionAsync("year");

        Assert.Equal(2, result.TotalQuotes);
        Assert.Equal(1, result.AcceptedQuotes);
        Assert.Equal(50.0m, result.ConversionRate);
    }

    [Fact]
    public async Task GetQuotePipelineAsync_GroupsByStatus()
    {
        var result = await _service.GetQuotePipelineAsync("year");

        Assert.True(result.Count >= 2); // Draft, Sent, Accepted
    }

    [Fact]
    public async Task GetTopClientsAsync_RanksByTotalValue()
    {
        var result = await _service.GetTopClientsAsync("year");

        Assert.Single(result); // Only one client with non-cancelled bookings
        Assert.Equal("Acme Corp", result[0].Name);
        Assert.Equal(5000m, result[0].TotalValue);
        Assert.Equal(1, result[0].BookingCount);
    }

    [Fact]
    public async Task GetTopSuppliersAsync_RanksByTotalCost()
    {
        var result = await _service.GetTopSuppliersAsync("year");

        Assert.Single(result);
        Assert.Equal("Safari Lodge", result[0].Name);
        Assert.Equal(1000m, result[0].TotalCost); // 500 * 2
    }

    [Fact]
    public async Task GetProfitabilitySummaryAsync_CalculatesMargin()
    {
        var result = await _service.GetProfitabilitySummaryAsync("year");

        Assert.Equal(5000m, result.TotalRevenue);
        Assert.Equal(3000m, result.TotalCost);
        Assert.Equal(2000m, result.TotalProfit);
        Assert.Equal(40.0m, result.MarginPercent);
    }

    [Fact]
    public async Task GetProfitByBookingAsync_ReturnsNonCancelledBookings()
    {
        var result = await _service.GetProfitByBookingAsync("year");

        Assert.Single(result);
        Assert.Equal("BK-001", result[0].BookingRef);
        Assert.Equal(2000m, result[0].Profit);
    }

    [Fact]
    public async Task SavePreferencesAsync_CreatesAndUpdatesPref()
    {
        await _service.SavePreferencesAsync("user-1", new Dictionary<string, bool>
        {
            ["revenue.monthly"] = false,
            ["bookings.status"] = true
        });

        var prefs = await _db.Set<UserReportPreference>()
            .Where(p => p.UserId == "user-1")
            .ToListAsync();

        Assert.Equal(2, prefs.Count);
        Assert.False(prefs.Single(p => p.WidgetKey == "revenue.monthly").IsVisible);
        Assert.True(prefs.Single(p => p.WidgetKey == "bookings.status").IsVisible);

        // Update existing
        await _service.SavePreferencesAsync("user-1", new Dictionary<string, bool>
        {
            ["revenue.monthly"] = true
        });

        var updated = await _db.Set<UserReportPreference>()
            .SingleAsync(p => p.UserId == "user-1" && p.WidgetKey == "revenue.monthly");
        Assert.True(updated.IsVisible);
    }

    [Fact]
    public async Task GetDashboardAsync_RespectsUserPreferences()
    {
        await _service.SavePreferencesAsync("user-2", new Dictionary<string, bool>
        {
            ["revenue.monthly"] = false
        });

        var dashboard = await _service.GetDashboardAsync("user-2", "month");

        var revenueWidget = dashboard.Widgets.Single(w => w.Key == "revenue.monthly");
        Assert.False(revenueWidget.IsVisible);
    }
}
