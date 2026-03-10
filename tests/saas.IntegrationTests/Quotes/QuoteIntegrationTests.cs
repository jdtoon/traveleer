using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using saas.Modules.Clients.Entities;
using saas.Modules.Inventory.Entities;
using saas.Modules.Quotes.Entities;
using saas.Modules.RateCards.Entities;
using saas.Modules.Settings.Entities;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Quotes;

public class QuoteIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public QuoteIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    [Fact]
    public async Task QuotesPage_RendersFullLayout()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/quotes");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
        await response.AssertElementExistsAsync("#quote-list");
        await response.AssertElementExistsAsync("#modal-container");
    }

    [Fact]
    public async Task QuoteListPartial_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/quotes/list");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("table, div.rounded-box");
    }

    [Fact]
    public async Task QuoteBuilderPage_UserCanLoadPreview()
    {
        var rateCardId = await SeedRateCardAsync();
        var page = await _client.GetAsync($"/{TenantSlug}/quotes/new");
        page.AssertSuccess();
        await page.AssertContainsAsync("New Quote");
        await page.AssertElementExistsAsync("#quote-builder-form");

        var preview = await _client.HtmxGetAsync($"/{TenantSlug}/quotes/preview?ClientName=Preview%20Client&OutputCurrencyCode=USD&SelectedRateCardIds={rateCardId}&MarkupPercentage=10");
        preview.AssertSuccess();
        await preview.AssertPartialViewAsync();
        await preview.AssertContainsAsync("Quote Preview");
        await preview.AssertContainsAsync("Grand QA Hotel");
    }

    [Fact]
    public async Task CreateQuote_OnValidSubmit_PersistsQuoteAndRedirectsToDetails()
    {
        var rateCardId = await SeedRateCardAsync();
        var clientId = await GetClientIdAsync();
        var builder = await _client.GetAsync($"/{TenantSlug}/quotes/new");
        builder.AssertSuccess();

        var response = await _client.SubmitFormAsync(builder, "form#quote-builder-form", new Dictionary<string, string>
        {
            ["ClientId"] = clientId.ToString(),
            ["ClientName"] = "Acacia Travel Group",
            ["OutputCurrencyCode"] = "USD",
            ["MarkupPercentage"] = "12",
            ["SelectedRateCardIds"] = rateCardId.ToString(),
            ["TravelStartDate"] = "2026-10-10",
            ["TravelEndDate"] = "2026-10-15",
            ["FilterByTravelDates"] = "true"
        });

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);

        await using var db = OpenTenantDb();
        var quote = await db.Quotes
            .Include(x => x.QuoteRateCards)
            .OrderByDescending(x => x.CreatedAt)
            .FirstAsync(x => x.ClientId == clientId && x.OutputCurrencyCode == "USD");
        Assert.Equal(QuoteStatus.Draft, quote.Status);
        Assert.Equal("USD", quote.OutputCurrencyCode);
        Assert.Single(quote.QuoteRateCards);
    }

    [Fact]
    public async Task CreateQuote_OnInvalidSubmit_ReturnsBuilderWithErrors()
    {
        var rateCardId = await SeedRateCardAsync();
        var builder = await _client.GetAsync($"/{TenantSlug}/quotes/new");
        builder.AssertSuccess();

        var response = await _client.SubmitFormAsync(builder, "form#quote-builder-form", new Dictionary<string, string>
        {
            ["ClientName"] = "",
            ["OutputCurrencyCode"] = "USD",
            ["SelectedRateCardIds"] = rateCardId.ToString(),
            ["TravelStartDate"] = "2026-12-20",
            ["TravelEndDate"] = "2026-12-10"
        });

        response.AssertSuccess();
        await response.AssertContainsAsync("New Quote");
        await response.AssertContainsAsync("Client name is required.");
        await response.AssertContainsAsync("Travel end date must be on or after the start date.");
    }

    [Fact]
    public async Task QuoteDetails_StatusUpdate_EmitsRefreshTriggers()
    {
        var quoteId = await SeedQuoteAsync();
        var details = await _client.GetAsync($"/{TenantSlug}/quotes/details/{quoteId}");
        details.AssertSuccess();
        await details.AssertContainsAsync("Edit Builder");

        var response = await _client.SubmitFormAsync(details, $"form[hx-post='/{TenantSlug}/quotes/status/{quoteId}']", new Dictionary<string, string>
        {
            ["status"] = QuoteStatus.Accepted.ToString()
        });

        response.AssertSuccess();
        response.AssertToast("Quote status updated.");
        response.AssertTrigger("quotes.refresh");
        response.AssertTrigger("quotes.details.refresh");

        await using var db = OpenTenantDb();
        var quote = await db.Quotes.SingleAsync(x => x.Id == quoteId);
        Assert.Equal(QuoteStatus.Accepted, quote.Status);
    }

    [Fact]
    public async Task QuoteDetails_WhenAcceptedAndNotConverted_ShowsConvertAction()
    {
        var quoteId = await SeedQuoteAsync(status: QuoteStatus.Accepted);

        var response = await _client.GetAsync($"/{TenantSlug}/quotes/details/{quoteId}");

        response.AssertSuccess();
        await response.AssertContainsAsync("Convert To Booking");
    }

    [Fact]
    public async Task QuoteDetails_WhenBookingExists_ShowsViewBookingAction()
    {
        var quoteId = await SeedQuoteAsync(status: QuoteStatus.Accepted);

        await using (var db = OpenTenantDb())
        {
            db.Bookings.Add(new saas.Modules.Bookings.Entities.Booking
            {
                QuoteId = quoteId,
                BookingRef = $"BK-Q-{Guid.NewGuid():N}"[..13],
                ClientId = await db.Clients.OrderBy(x => x.Name).Select(x => x.Id).FirstAsync(),
                CostCurrencyCode = "USD",
                SellingCurrencyCode = "USD",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/{TenantSlug}/quotes/details/{quoteId}");

        response.AssertSuccess();
        await response.AssertContainsAsync("View Booking");
    }

    [Fact]
    public async Task QuoteDetails_UserCanConvertAcceptedQuoteToBooking()
    {
        var quoteId = await SeedQuoteAsync(status: QuoteStatus.Accepted);
        var details = await _client.GetAsync($"/{TenantSlug}/quotes/details/{quoteId}");
        details.AssertSuccess();

        var response = await _client.SubmitFormAsync(details, $"form[hx-post='/{TenantSlug}/bookings/convert-from-quote/{quoteId}']", new Dictionary<string, string>());

        response.AssertSuccess();
        response.AssertToast("Booking created from quote.");

        await using var db = OpenTenantDb();
        var booking = await db.Bookings.Include(x => x.Items).SingleAsync(x => x.QuoteId == quoteId);
        Assert.Single(booking.Items);
        Assert.Equal("USD", booking.SellingCurrencyCode);
    }

    [Fact]
    public async Task QuotesPage_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/{TenantSlug}/quotes");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }

    private async Task<Guid> GetClientIdAsync()
    {
        await using var db = OpenTenantDb();
        return await db.Clients.OrderBy(x => x.Name).Select(x => x.Id).FirstAsync();
    }

    private async Task<Guid> SeedRateCardAsync()
    {
        await using var db = OpenTenantDb();
        var existing = await db.RateCards.FirstOrDefaultAsync(x => x.Name == "QA Quote Contract");
        if (existing is not null)
        {
            return existing.Id;
        }

        var destination = new Destination { Name = "QA Destination", SortOrder = 99, IsActive = true, CreatedAt = DateTime.UtcNow };
        var supplier = new Supplier { Name = "QA Supplier", IsActive = true, CreatedAt = DateTime.UtcNow };
        var hotel = new InventoryItem
        {
            Name = "Grand QA Hotel",
            Kind = InventoryItemKind.Hotel,
            BaseCost = 1200m,
            Destination = destination,
            Supplier = supplier,
            CreatedAt = DateTime.UtcNow
        };
        var roomType = await db.RoomTypes.OrderBy(x => x.SortOrder).FirstAsync();
        var rateCard = new RateCard
        {
            Name = "QA Quote Contract",
            InventoryItem = hotel,
            ContractCurrencyCode = "USD",
            Status = RateCardStatus.Active,
            CreatedAt = DateTime.UtcNow,
            Seasons =
            {
                new RateSeason
                {
                    Name = "October",
                    StartDate = new DateOnly(2026, 10, 1),
                    EndDate = new DateOnly(2026, 10, 31),
                    SortOrder = 10,
                    Rates =
                    {
                        new RoomRate
                        {
                            RoomTypeId = roomType.Id,
                            WeekdayRate = 200m,
                            WeekendRate = 240m,
                            IsIncluded = true
                        }
                    }
                }
            }
        };

        db.RateCards.Add(rateCard);
        await db.SaveChangesAsync();
        return rateCard.Id;
    }

    private async Task<Guid> SeedQuoteAsync(QuoteStatus status = QuoteStatus.Draft)
    {
        await using var db = OpenTenantDb();
        var client = await db.Clients.OrderBy(x => x.Name).FirstAsync();
        var rateCardId = await SeedRateCardAsync();

        var quote = new Quote
        {
            ReferenceNumber = $"QT-TEST-{Guid.NewGuid():N}"[..13],
            ClientId = client.Id,
            ClientName = client.Name,
            ClientEmail = client.Email,
            OutputCurrencyCode = "USD",
            MarkupPercentage = 10m,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            QuoteRateCards =
            {
                new QuoteRateCard
                {
                    RateCardId = rateCardId,
                    SortOrder = 1
                }
            }
        };

        db.Quotes.Add(quote);
        await db.SaveChangesAsync();
        return quote.Id;
    }

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }
}
