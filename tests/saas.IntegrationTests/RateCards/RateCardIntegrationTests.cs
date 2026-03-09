using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using saas.Modules.Inventory.Entities;
using saas.Modules.RateCards.Entities;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.RateCards;

public class RateCardIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public RateCardIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    [Fact]
    public async Task RateCardsPage_RendersFullLayout()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/rate-cards");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
        await response.AssertElementExistsAsync("#modal-container");
        await response.AssertElementExistsAsync("#rate-card-list");
    }

    [Fact]
    public async Task RateCardListPartial_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/rate-cards/list");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("table, div.rounded-box");
    }

    [Fact]
    public async Task RateCardNewPartial_RendersModalForm()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/rate-cards/new");

        response.AssertSuccess();
        await response.AssertElementExistsAsync("dialog.modal");
        await response.AssertContainsAsync("New Rate Card");
    }

    [Fact]
    public async Task RateCardsPage_UserCanCreateRateCard()
    {
        var hotelId = await GetFirstHotelIdAsync();
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/rate-cards/new");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = $"QA Contract {Guid.NewGuid():N}"[..18],
            ["InventoryItemId"] = hotelId.ToString(),
            ["ContractCurrencyCode"] = "USD"
        });

        response.AssertSuccess();
        response.AssertToast("Rate card created.");
    }

    [Fact]
    public async Task CreateRateCard_OnInvalidSubmit_ReturnsFormWithErrors()
    {
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/rate-cards/new");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = "",
            ["InventoryItemId"] = ""
        });

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("dialog.modal");
        await response.AssertContainsAsync("Rate card name is required.");
    }

    [Fact]
    public async Task RateCardDetails_UserCanCreateSeasonAndUpdateRate()
    {
        var rateCardId = await SeedRateCardAsync();

        var page = await _client.GetAsync($"/{TenantSlug}/rate-cards/details/{rateCardId}");
        page.AssertSuccess();
        await page.AssertElementExistsAsync("#rate-card-grid");

        var seasonForm = await _client.HtmxGetAsync($"/{TenantSlug}/rate-cards/seasons/new/{rateCardId}");
        seasonForm.AssertSuccess();
        await seasonForm.AssertContainsAsync("Add Season");

        var seasonResponse = await _client.SubmitFormAsync(seasonForm, "form", new Dictionary<string, string>
        {
            ["Name"] = "Peak Umrah",
            ["StartDate"] = "2026-11-01",
            ["EndDate"] = "2026-11-30",
            ["IsBlackout"] = "false"
        });

        seasonResponse.AssertSuccess();
        seasonResponse.AssertToast("Season added.");

        var details = await GetRateCardDetailsAsync(rateCardId);
        Assert.NotNull(details);
        var seasonId = details!.Seasons.Single().Id;
        var roomTypeId = details.Seasons.Single().Rates.First().RoomTypeId;

        var gridForm = await _client.HtmxGetAsync($"/{TenantSlug}/rate-cards/grid/{rateCardId}");
        gridForm.AssertSuccess();

        var updateResponse = await _client.SubmitFormAsync(gridForm, "form", new Dictionary<string, string>
        {
            ["RateCardId"] = rateCardId.ToString(),
            ["RateSeasonId"] = seasonId.ToString(),
            ["RoomTypeId"] = roomTypeId.ToString(),
            ["WeekdayRate"] = "2200",
            ["WeekendRate"] = "2500",
            ["IsIncluded"] = "true"
        });

        updateResponse.AssertSuccess();
        updateResponse.AssertToast("Rate updated.");

        var grid = await _client.HtmxGetAsync($"/{TenantSlug}/rate-cards/grid/{rateCardId}");
        grid.AssertSuccess();
        await grid.AssertContainsAsync("2200.00");
    }

    [Fact]
    public async Task RateCardActivate_WhenSeasonExists_UpdatesStatus()
    {
        var rateCardId = await SeedRateCardAsync();
        await SeedSeasonAsync(rateCardId);

        var detailsPage = await _client.GetAsync($"/{TenantSlug}/rate-cards/details/{rateCardId}");
        detailsPage.AssertSuccess();

        var response = await _client.SubmitFormAsync(detailsPage, $"form[hx-post='/{TenantSlug}/rate-cards/activate/{rateCardId}']", new Dictionary<string, string>());

        response.AssertSuccess();
        response.AssertToast("Rate card activated.");

        await using var db = OpenTenantDb();
        var card = await db.RateCards.SingleAsync(x => x.Id == rateCardId);
        Assert.Equal(RateCardStatus.Active, card.Status);
    }

    [Fact]
    public async Task RateCardsPage_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/{TenantSlug}/rate-cards");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }

    private async Task<Guid> GetFirstHotelIdAsync()
    {
        await using var db = OpenTenantDb();
        return await db.InventoryItems.Where(x => x.Kind == InventoryItemKind.Hotel).OrderBy(x => x.Name).Select(x => x.Id).FirstAsync();
    }

    private async Task<Guid> SeedRateCardAsync()
    {
        await using var db = OpenTenantDb();
        var hotelId = await db.InventoryItems.Where(x => x.Kind == InventoryItemKind.Hotel).OrderBy(x => x.Name).Select(x => x.Id).FirstAsync();
        var rateCard = new RateCard
        {
            Name = $"Contract {Guid.NewGuid():N}"[..18],
            InventoryItemId = hotelId,
            ContractCurrencyCode = "USD",
            Status = RateCardStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };
        db.RateCards.Add(rateCard);
        await db.SaveChangesAsync();
        return rateCard.Id;
    }

    private async Task SeedSeasonAsync(Guid rateCardId)
    {
        await using var db = OpenTenantDb();
        var roomTypeIds = await db.RoomTypes.OrderBy(x => x.SortOrder).Select(x => x.Id).ToListAsync();
        var season = new RateSeason
        {
            RateCardId = rateCardId,
            Name = "Existing Season",
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 31),
            SortOrder = 10
        };
        foreach (var roomTypeId in roomTypeIds)
        {
            season.Rates.Add(new RoomRate
            {
                RoomTypeId = roomTypeId,
                WeekdayRate = 1500m,
                WeekendRate = 1800m,
                IsIncluded = true
            });
        }
        db.RateSeasons.Add(season);
        await db.SaveChangesAsync();
    }

    private async Task<RateCard?> GetRateCardDetailsAsync(Guid rateCardId)
    {
        await using var db = OpenTenantDb();
        return await db.RateCards
            .Include(x => x.Seasons)
                .ThenInclude(x => x.Rates)
            .FirstOrDefaultAsync(x => x.Id == rateCardId);
    }

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }
}
