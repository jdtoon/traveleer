using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using saas.Modules.Bookings.Entities;
using saas.Modules.Clients.Entities;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Bookings;

public class PaymentLinkIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public PaymentLinkIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    // ── Admin: List Payment Links ──

    [Fact]
    public async Task PaymentLinkList_RendersPartial()
    {
        var bookingId = await SeedBookingAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/payment-links/{bookingId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("Online Payment Links");
        await response.AssertContainsAsync("No payment links created yet.");
    }

    [Fact]
    public async Task PaymentLinkList_WithLinks_ShowsTable()
    {
        var (bookingId, _) = await SeedBookingWithLinkAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/payment-links/{bookingId}");

        response.AssertSuccess();
        await response.AssertContainsAsync("1,500.00");
        await response.AssertContainsAsync("Pending");
        await response.AssertContainsAsync("View Link");
    }

    // ── Admin: Create Payment Link ──

    [Fact]
    public async Task NewPaymentLink_RendersModalForm()
    {
        var bookingId = await SeedBookingAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/payment-links/new/{bookingId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("dialog.modal");
        await response.AssertContainsAsync("Create Payment Link");
        await response.AssertContainsAsync("Amount");
        await response.AssertContainsAsync("Description");
        await response.AssertContainsAsync("Link expires in");
    }

    [Fact]
    public async Task CreatePaymentLink_OnValidSubmit_PersistsAndReturnsToast()
    {
        var bookingId = await SeedBookingAsync();
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/payment-links/new/{bookingId}");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Amount"] = "2000.00",
            ["Description"] = "Safari deposit",
            ["ExpiryDays"] = "14",
            ["CurrencyCode"] = "USD"
        });

        response.AssertSuccess();
        response.AssertToast("Payment link created.");
        response.AssertTrigger("bookings.paymentlinks.refresh");

        await using var db = OpenTenantDb();
        var link = await db.PaymentLinks.SingleAsync(l => l.BookingId == bookingId && l.Description == "Safari deposit");
        Assert.Equal(2000m, link.Amount);
        Assert.Equal("USD", link.CurrencyCode);
        Assert.Equal(PaymentLinkStatus.Pending, link.Status);
        Assert.NotEmpty(link.Token);
        Assert.Equal(TenantSlug, link.TenantSlug);
        Assert.True(link.ExpiresAt > DateTime.UtcNow.AddDays(13));
    }

    [Fact]
    public async Task CreatePaymentLink_InvalidAmount_ReturnsFormWithErrors()
    {
        var bookingId = await SeedBookingAsync();
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/payment-links/new/{bookingId}");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Amount"] = "0",
            ["Description"] = "Test",
            ["ExpiryDays"] = "7",
            ["CurrencyCode"] = "USD"
        });

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("dialog.modal");
    }

    // ── Admin: Cancel Payment Link ──

    [Fact]
    public async Task CancelPaymentLink_SetsStatusCancelled()
    {
        var (bookingId, linkId) = await SeedBookingWithLinkAsync();

        var listResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/payment-links/{bookingId}");
        listResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(listResponse, $"form[hx-post='/{TenantSlug}/bookings/payment-links/cancel/{linkId}']", new Dictionary<string, string>());

        response.AssertSuccess();
        response.AssertToast("Payment link cancelled.");
        response.AssertTrigger("bookings.paymentlinks.refresh");

        await using var db = OpenTenantDb();
        var link = await db.PaymentLinks.FindAsync(linkId);
        Assert.Equal(PaymentLinkStatus.Cancelled, link!.Status);
    }

    // ── Public: Payment Page ──

    [Fact]
    public async Task PublicPaymentPage_ValidToken_RendersPaymentPage()
    {
        var (_, _, token) = await SeedBookingWithLinkReturnTokenAsync();
        var publicClient = _fixture.CreateClient();

        var response = await publicClient.GetAsync($"/pay/{TenantSlug}/{token}");

        response.AssertSuccess();
        await response.AssertContainsAsync("Payment Request");
        await response.AssertContainsAsync("1,500.00");
        await response.AssertContainsAsync("Test deposit");
    }

    [Fact]
    public async Task PublicPaymentPage_InvalidToken_ShowsExpired()
    {
        var publicClient = _fixture.CreateClient();

        var response = await publicClient.GetAsync($"/pay/{TenantSlug}/invalid-token-xyz");

        response.AssertSuccess();
        await response.AssertContainsAsync("Payment Link Unavailable");
    }

    [Fact]
    public async Task PublicPaymentPage_ExpiredLink_ShowsExpired()
    {
        var (_, linkId, token) = await SeedBookingWithLinkReturnTokenAsync();

        // Expire the link
        await using (var db = OpenTenantDb())
        {
            var link = await db.PaymentLinks.FindAsync(linkId);
            link!.ExpiresAt = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }

        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/pay/{TenantSlug}/{token}");

        response.AssertSuccess();
        await response.AssertContainsAsync("Payment Link Unavailable");
    }

    [Fact]
    public async Task PublicCheckout_MarksPaidAndCreatesBookingPayment()
    {
        var (bookingId, _, token) = await SeedBookingWithLinkReturnTokenAsync();
        var publicClient = _fixture.CreateClient();

        var response = await publicClient.PostAsync($"/pay/{TenantSlug}/{token}/checkout", new Dictionary<string, string>());

        // Should redirect to success page
        response.AssertStatus(System.Net.HttpStatusCode.Redirect);

        await using var db = OpenTenantDb();
        var link = await db.PaymentLinks.FirstAsync(l => l.Token == token);
        Assert.Equal(PaymentLinkStatus.Paid, link.Status);
        Assert.NotNull(link.PaidAt);

        var payment = await db.BookingPayments.FirstOrDefaultAsync(p => p.BookingId == bookingId && p.PaymentMethod == PaymentMethod.Online);
        Assert.NotNull(payment);
        Assert.Equal(1500m, payment.Amount);
    }

    [Fact]
    public async Task PublicSuccess_ShowsConfirmation()
    {
        var (_, _, token) = await SeedBookingWithLinkReturnTokenAsync();
        var publicClient = _fixture.CreateClient();

        // Pay first
        await publicClient.PostAsync($"/pay/{TenantSlug}/{token}/checkout", new Dictionary<string, string>());

        var response = await publicClient.GetAsync($"/pay/{TenantSlug}/{token}/success");

        response.AssertSuccess();
        await response.AssertContainsAsync("Payment Successful");
        await response.AssertContainsAsync("1,500.00");
    }

    [Fact]
    public async Task PublicCancel_ShowsCancelPage()
    {
        var (_, _, token) = await SeedBookingWithLinkReturnTokenAsync();
        var publicClient = _fixture.CreateClient();

        var response = await publicClient.GetAsync($"/pay/{TenantSlug}/{token}/cancel");

        response.AssertSuccess();
        await response.AssertContainsAsync("Payment Cancelled");
        await response.AssertContainsAsync("Return to Payment");
    }

    // ── Auth: Unauthenticated admin routes should redirect ──

    [Fact]
    public async Task AdminRoutes_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var bookingId = await SeedBookingAsync();

        var response = await publicClient.GetAsync($"/{TenantSlug}/bookings/payment-links/{bookingId}");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }

    // --- helpers ---

    private async Task<Guid> SeedBookingAsync()
    {
        await using var db = OpenTenantDb();
        var clientId = await db.Clients.OrderBy(x => x.Name).Select(x => x.Id).FirstAsync();
        var booking = new Booking
        {
            BookingRef = $"BK-PL-{Guid.NewGuid():N}"[..13],
            ClientId = clientId,
            Pax = 2,
            TravelStartDate = new DateOnly(2026, 6, 1),
            TravelEndDate = new DateOnly(2026, 6, 5),
            CostCurrencyCode = "USD",
            SellingCurrencyCode = "USD",
            TotalSelling = 5000m,
            CreatedAt = DateTime.UtcNow
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    private async Task<(Guid BookingId, Guid LinkId)> SeedBookingWithLinkAsync()
    {
        await using var db = OpenTenantDb();
        var clientId = await db.Clients.OrderBy(x => x.Name).Select(x => x.Id).FirstAsync();
        var booking = new Booking
        {
            BookingRef = $"BK-PLK-{Guid.NewGuid():N}"[..13],
            ClientId = clientId,
            Pax = 2,
            TravelStartDate = new DateOnly(2026, 7, 1),
            TravelEndDate = new DateOnly(2026, 7, 5),
            CostCurrencyCode = "USD",
            SellingCurrencyCode = "USD",
            TotalSelling = 5000m,
            CreatedAt = DateTime.UtcNow
        };
        var link = new PaymentLink
        {
            BookingId = booking.Id,
            ClientId = clientId,
            Amount = 1500m,
            CurrencyCode = "USD",
            Token = $"test-link-{Guid.NewGuid():N}",
            Description = "Test deposit",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedByUserId = "test-user",
            TenantSlug = TenantSlug,
            CreatedAt = DateTime.UtcNow
        };
        db.Bookings.Add(booking);
        db.PaymentLinks.Add(link);
        await db.SaveChangesAsync();
        return (booking.Id, link.Id);
    }

    private async Task<(Guid BookingId, Guid LinkId, string Token)> SeedBookingWithLinkReturnTokenAsync()
    {
        await using var db = OpenTenantDb();
        var clientId = await db.Clients.OrderBy(x => x.Name).Select(x => x.Id).FirstAsync();
        var booking = new Booking
        {
            BookingRef = $"BK-PLT-{Guid.NewGuid():N}"[..13],
            ClientId = clientId,
            Pax = 2,
            TravelStartDate = new DateOnly(2026, 8, 1),
            TravelEndDate = new DateOnly(2026, 8, 5),
            CostCurrencyCode = "USD",
            SellingCurrencyCode = "USD",
            TotalSelling = 5000m,
            CreatedAt = DateTime.UtcNow
        };
        var token = $"pay-test-{Guid.NewGuid():N}";
        var link = new PaymentLink
        {
            BookingId = booking.Id,
            ClientId = clientId,
            Amount = 1500m,
            CurrencyCode = "USD",
            Token = token,
            Description = "Test deposit",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedByUserId = "test-user",
            TenantSlug = TenantSlug,
            CreatedAt = DateTime.UtcNow
        };
        db.Bookings.Add(booking);
        db.PaymentLinks.Add(link);
        await db.SaveChangesAsync();
        return (booking.Id, link.Id, token);
    }

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }
}
