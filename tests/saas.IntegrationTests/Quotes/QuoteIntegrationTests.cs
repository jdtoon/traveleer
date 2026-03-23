using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using saas.Modules.Clients.Entities;
using saas.Modules.Quotes.DTOs;
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
        await response.AssertContainsAsync("Search by reference, client, email, or product");
        await response.AssertContainsAsync("hx-get=\"/demo/quotes/list\"");
        await response.AssertContainsAsync("hx-target=\"#quote-list\"");
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
    public async Task QuoteListPartial_RendersClientDetailLinks()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/quotes/list");

        response.AssertSuccess();
        await response.AssertContainsAsync($"hx-get=\"/{TenantSlug}/clients/details/");
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
    public async Task QuoteBuilderPage_RendersTemplateAndDisplayControls()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/quotes/new");

        response.AssertSuccess();
        await response.AssertContainsAsync("Template & Display");
        await response.AssertContainsAsync("Show property images when available");
        await response.AssertContainsAsync("Show default meal plan badges");
        await response.AssertContainsAsync("Compact");
    }

    [Fact]
    public async Task QuoteBuilderPage_RendersRateCardSearchControl()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/quotes/new");

        response.AssertSuccess();
        await response.AssertContainsAsync("Find rate cards");
        await response.AssertContainsAsync("id=\"rate-card-search\"");
        await response.AssertContainsAsync("data-rate-card-search=");
    }

    [Fact]
    public async Task QuotePreview_WhenTemplateSettingsProvided_ReflectsLayoutAndToggles()
    {
        var rateCardId = await SeedRateCardAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/quotes/preview?ClientName=Preview%20Client&OutputCurrencyCode=USD&SelectedRateCardIds={rateCardId}&MarkupPercentage=10&TemplateLayout=list&ShowImages=true&ShowMealPlan=true&ShowFooter=false&ShowRoomDescriptions=true");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("Layout: List");
        await response.AssertContainsAsync("Generous room layout for family groups.");
        await response.AssertDoesNotContainAsync("Subject to supplier reconfirmation.");
    }

    [Fact]
    public async Task QuotePreview_WhenTravelFilterEnabledWithoutDates_UsesIntentionalCopy()
    {
        var rateCardId = await SeedRateCardAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/quotes/preview?ClientName=Preview%20Client&OutputCurrencyCode=USD&SelectedRateCardIds={rateCardId}&MarkupPercentage=10&FilterByTravelDates=true");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("Travel filter is enabled for Travel dates not set yet.");
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
            ["TemplateLayout"] = "compact",
            ["ShowImages"] = "true",
            ["ShowMealPlan"] = "true",
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
        Assert.Equal("compact", quote.TemplateLayout);
        Assert.True(quote.ShowImages);
        Assert.True(quote.ShowMealPlan);
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
    public async Task QuoteVersionDetails_WhenRequested_RendersSnapshotModal()
    {
        var quoteId = await SeedQuoteAsync();
        var versionId = await SeedQuoteVersionAsync(quoteId, 2, "SAR", 2);

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/quotes/versions/{quoteId}/{versionId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("dialog.modal");
        await response.AssertContainsAsync("snapshot v2");
        await response.AssertContainsAsync("SAR");
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
        await response.AssertContainsAsync("Linked booking");
        await response.AssertContainsAsync("View booking");
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
        var roomType = await db.RoomTypes.OrderBy(x => x.SortOrder).FirstAsync();
        roomType.Description = "Generous room layout for family groups.";
        var mealPlan = await db.MealPlans.OrderBy(x => x.SortOrder).FirstAsync();

        if (existing is not null)
        {
            var inventory = await db.InventoryItems.FirstAsync(x => x.Id == existing.InventoryItemId);
            inventory.Description = "Seafront stay with concierge-led arrival support.";
            inventory.ImageUrl = "/favicon.svg";
            inventory.Rating = 5;
            existing.DefaultMealPlanId = mealPlan.Id;
            await db.SaveChangesAsync();
            return existing.Id;
        }

        var destination = new Destination { Name = "QA Destination", SortOrder = 99, IsActive = true, CreatedAt = DateTime.UtcNow };
        var supplier = new Supplier { Name = "QA Supplier", IsActive = true, CreatedAt = DateTime.UtcNow };
        var hotel = new InventoryItem
        {
            Name = "Grand QA Hotel",
            Kind = InventoryItemKind.Hotel,
            Description = "Seafront stay with concierge-led arrival support.",
            ImageUrl = "/favicon.svg",
            Rating = 5,
            BaseCost = 1200m,
            Destination = destination,
            Supplier = supplier,
            CreatedAt = DateTime.UtcNow
        };
        var rateCard = new RateCard
        {
            Name = "QA Quote Contract",
            InventoryItem = hotel,
            DefaultMealPlanId = mealPlan.Id,
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

    private async Task<Guid> SeedQuoteVersionAsync(Guid quoteId, int versionNumber, string outputCurrencyCode, int rateCardCount)
    {
        await using var db = OpenTenantDb();
        var quote = await db.Quotes.SingleAsync(x => x.Id == quoteId);
        var selectedRateCardIds = await db.QuoteRateCards
            .Where(x => x.QuoteId == quoteId)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.RateCardId)
            .Take(rateCardCount)
            .ToListAsync();

        var version = new QuoteVersion
        {
            QuoteId = quoteId,
            VersionNumber = versionNumber,
            SnapshotJson = JsonSerializer.Serialize(new QuoteVersionSnapshotDto
            {
                ClientName = quote.ClientName,
                ClientEmail = quote.ClientEmail,
                OutputCurrencyCode = outputCurrencyCode,
                MarkupPercentage = 12m + versionNumber,
                GroupBy = "ratecard",
                TravelStartDate = new DateOnly(2026, 10, 10),
                TravelEndDate = new DateOnly(2026, 10, 15),
                FilterByTravelDates = true,
                Notes = $"Snapshot {versionNumber}",
                SelectedRateCardIds = selectedRateCardIds
            }),
            CreatedAt = DateTime.UtcNow.AddMinutes(-versionNumber),
            CreatedBy = "qa@acacia.test"
        };

        db.QuoteVersions.Add(version);
        await db.SaveChangesAsync();
        return version.Id;
    }

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }
}
