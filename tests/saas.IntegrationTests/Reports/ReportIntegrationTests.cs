using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using saas.Modules.Bookings.Entities;
using saas.Modules.Clients.Entities;
using saas.Modules.Settings.Entities;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Reports;

public class ReportIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public ReportIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    // ── Layer 1: Full Page Load ──

    [Fact]
    public async Task ReportsPage_RendersFullLayout()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/reports");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
        await response.AssertContainsAsync("Reports");
    }

    [Fact]
    public async Task ReportsPage_WithDateRange_RendersFullLayout()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/reports?range=year");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertContainsAsync("Reports");
    }

    // ── Layer 2: Partial Isolation ──

    [Fact]
    public async Task RevenueMonthlyWidget_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/revenue-monthly?range=year");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task RevenueYtdWidget_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/revenue-ytd");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task BookingsStatusWidget_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/bookings-status?range=month");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task BookingsRecentWidget_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/bookings-recent");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task BookingsRecentWidget_RendersDrilldownLinks()
    {
        var seeded = await SeedReportLinkDataAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/bookings-recent");

        response.AssertSuccess();
        await response.AssertContainsAsync(seeded.BookingRef);
        await response.AssertContainsAsync($"href=\"/{TenantSlug}/bookings/details/{seeded.BookingId}\"");
        await response.AssertContainsAsync($"hx-get=\"/{TenantSlug}/clients/details/{seeded.ClientId}\"");
    }

    [Fact]
    public async Task QuotesConversionWidget_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/quotes-conversion?range=month");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task QuotesPipelineWidget_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/quotes-pipeline?range=month");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task ClientsTopWidget_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/clients-top?range=year");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task ClientsTopWidget_RendersClientDetailLinks()
    {
        var seeded = await SeedReportLinkDataAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/clients-top?range=today");

        response.AssertSuccess();
        await response.AssertContainsAsync(seeded.ClientName);
        await response.AssertContainsAsync($"hx-get=\"/{TenantSlug}/clients/details/{seeded.ClientId}\"");
    }

    [Fact]
    public async Task SuppliersTopWidget_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/suppliers-top?range=year");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task SuppliersTopWidget_RendersSupplierDetailLinks()
    {
        var seeded = await SeedReportLinkDataAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/suppliers-top?range=today");

        response.AssertSuccess();
        await response.AssertContainsAsync(seeded.SupplierName);
        await response.AssertContainsAsync($"href=\"/{TenantSlug}/suppliers/details/{seeded.SupplierId}\"");
    }

    [Fact]
    public async Task ProfitabilitySummaryWidget_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/profitability-summary?range=month");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task ProfitByBookingWidget_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/profitability-by-booking?range=month");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task ProfitByBookingWidget_RendersBookingAndClientDrilldownLinks()
    {
        var seeded = await SeedReportLinkDataAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/profitability-by-booking?range=today");

        response.AssertSuccess();
        await response.AssertContainsAsync(seeded.BookingRef);
        await response.AssertContainsAsync($"href=\"/{TenantSlug}/bookings/details/{seeded.BookingId}\"");
        await response.AssertContainsAsync($"hx-get=\"/{TenantSlug}/clients/details/{seeded.ClientId}\"");
    }

    // ── Layer 4: Database Verification ──

    [Fact]
    public async Task ReportsPage_ContainsWidgetContainers()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/reports");

        response.AssertSuccess();
        await response.AssertElementExistsAsync("#widgets-container");
        await response.AssertContainsAsync("hx-trigger=\"load\"");
    }

    // ── Access Control ──

    [Fact]
    public async Task ReportsPage_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/{TenantSlug}/reports");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }

    private async Task<(Guid BookingId, string BookingRef, Guid ClientId, string ClientName, Guid SupplierId, string SupplierName)> SeedReportLinkDataAsync()
    {
        var clientId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clientName = $"Report Client {suffix}";
        var supplierName = $"Report Supplier {suffix}";
        var bookingRef = $"BK-RPT-{suffix.ToUpperInvariant()}";

        await using var db = OpenTenantDb();

        var priorReportBookings = await db.Bookings
            .Where(b => EF.Functions.Like(b.BookingRef, "BK-RPT-%"))
            .Select(b => b.Id)
            .ToListAsync();

        if (priorReportBookings.Count > 0)
        {
            var priorItems = await db.BookingItems
                .Where(item => priorReportBookings.Contains(item.BookingId))
                .ToListAsync();
            db.BookingItems.RemoveRange(priorItems);

            var priorBookings = await db.Bookings
                .Where(b => priorReportBookings.Contains(b.Id))
                .ToListAsync();
            db.Bookings.RemoveRange(priorBookings);

            var priorClients = await db.Clients
                .Where(c => EF.Functions.Like(c.Name, "Report Client %"))
                .ToListAsync();
            db.Clients.RemoveRange(priorClients);

            var priorSuppliers = await db.Suppliers
                .Where(s => EF.Functions.Like(s.Name, "Report Supplier %"))
                .ToListAsync();
            db.Suppliers.RemoveRange(priorSuppliers);

            await db.SaveChangesAsync();
        }

        db.Clients.Add(new Client
        {
            Id = clientId,
            Name = clientName,
            Email = $"{suffix}@client.test",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "integration-test"
        });

        db.Suppliers.Add(new Supplier
        {
            Id = supplierId,
            Name = supplierName,
            ContactEmail = $"{suffix}@supplier.test",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "integration-test"
        });

        var createdAt = DateTime.UtcNow;
        var bookingIds = new[] { bookingId, Guid.NewGuid(), Guid.NewGuid() };
        const decimal seededSelling = 900m;
        const decimal seededCost = 650m;
        for (var index = 0; index < bookingIds.Length; index++)
        {
            var currentBookingId = bookingIds[index];
            db.Bookings.Add(new Booking
            {
                Id = currentBookingId,
                BookingRef = index == 0 ? bookingRef : $"{bookingRef}-{index + 1}",
                ClientId = clientId,
                Status = BookingStatus.Confirmed,
                TotalSelling = seededSelling,
                TotalCost = seededCost,
                TotalProfit = seededSelling - seededCost,
                SellingCurrencyCode = "USD",
                CostCurrencyCode = "USD",
                CreatedAt = createdAt.AddMinutes(-index),
                CreatedBy = "integration-test"
            });

            db.BookingItems.Add(new BookingItem
            {
                Id = Guid.NewGuid(),
                BookingId = currentBookingId,
                SupplierId = supplierId,
                ServiceName = "Report-linked stay",
                CostPrice = seededCost,
                SellingPrice = seededSelling,
                Quantity = 1
            });
        }

        await db.SaveChangesAsync();

        return (bookingId, bookingRef, clientId, clientName, supplierId, supplierName);
    }
}
