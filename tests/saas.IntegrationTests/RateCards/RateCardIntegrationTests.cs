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
        await response.AssertContainsAsync("Export All JSON");
        await response.AssertContainsAsync("Import JSON");
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
        await SeedTemplateAsync();
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/rate-cards/new");

        response.AssertSuccess();
        await response.AssertElementExistsAsync("dialog.modal");
        await response.AssertContainsAsync("New Rate Card");
        await response.AssertContainsAsync("Starting template");
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
    public async Task RateCardsPage_UserCanCreateExcursionRateCard()
    {
        var excursionId = await GetFirstExcursionIdAsync();
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/rate-cards/new");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = $"QA Excursion {Guid.NewGuid():N}"[..18],
            ["InventoryItemId"] = excursionId.ToString(),
            ["ContractCurrencyCode"] = "USD"
        });

        response.AssertSuccess();
        response.AssertToast("Rate card created.");
    }

    [Fact]
    public async Task RateCardsPage_UserCanCreateRateCardFromTemplate()
    {
        var hotelId = await GetFirstHotelIdAsync();
        var templateId = await SeedTemplateAsync();
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/rate-cards/new");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = $"QA Template {Guid.NewGuid():N}"[..18],
            ["InventoryItemId"] = hotelId.ToString(),
            ["ContractCurrencyCode"] = "USD",
            ["TemplateId"] = templateId.ToString(),
            ["ValidFrom"] = "2027-01-01"
        });

        response.AssertSuccess();
        response.AssertToast("Rate card created.");

        await using var db = OpenTenantDb();
        var created = await db.RateCards
            .Include(x => x.Seasons)
            .OrderByDescending(x => x.CreatedAt)
            .FirstAsync(x => x.InventoryItemId == hotelId && x.Name.StartsWith("QA Template"));
        Assert.Equal(2, created.Seasons.Count);
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
        await page.AssertElementExistsAsync($"a[href='/{TenantSlug}/rate-cards']");
        await page.AssertContainsAsync("Back to Rate Cards");

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
            ["RoomTypeId"] = roomTypeId!.Value.ToString(),
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
    public async Task RateCardDetails_RendersInventoryDetailLinks()
    {
        var rateCardId = await SeedRateCardAsync();

        await using var db = OpenTenantDb();
        var rateCard = await db.RateCards
            .AsNoTracking()
            .Include(rc => rc.InventoryItem)
            .FirstAsync(rc => rc.Id == rateCardId);

        var response = await _client.GetAsync($"/{TenantSlug}/rate-cards/details/{rateCardId}");

        response.AssertSuccess();
        await response.AssertContainsAsync($"href=\"/{TenantSlug}/inventory?search={Uri.EscapeDataString(rateCard.InventoryItem!.Name)}\"");
    }

    [Fact]
    public async Task ExcursionRateCardDetails_UserCanUpdateRateCategory()
    {
        var rateCardId = await SeedExcursionRateCardAsync();
        await SeedExcursionSeasonAsync(rateCardId);

        var page = await _client.GetAsync($"/{TenantSlug}/rate-cards/details/{rateCardId}");
        page.AssertSuccess();
        await page.AssertContainsAsync("Excursion");

        var details = await GetRateCardDetailsAsync(rateCardId);
        Assert.NotNull(details);
        var seasonId = details!.Seasons.Single().Id;
        var categoryId = details.Seasons.Single().Rates.First().RateCategoryId;

        var grid = await _client.HtmxGetAsync($"/{TenantSlug}/rate-cards/grid/{rateCardId}");
        grid.AssertSuccess();
        await grid.AssertContainsAsync("Adult");

        var updateResponse = await _client.SubmitFormAsync(grid, "form", new Dictionary<string, string>
        {
            ["RateCardId"] = rateCardId.ToString(),
            ["RateSeasonId"] = seasonId.ToString(),
            ["RateCategoryId"] = categoryId!.Value.ToString(),
            ["WeekdayRate"] = "480",
            ["WeekendRate"] = "560",
            ["IsIncluded"] = "true"
        });

        updateResponse.AssertSuccess();
        updateResponse.AssertToast("Rate updated.");

        var refreshed = await _client.HtmxGetAsync($"/{TenantSlug}/rate-cards/grid/{rateCardId}");
        refreshed.AssertSuccess();
        await refreshed.AssertContainsAsync("480.00");
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
    public async Task RateCardDetails_UserCanSaveTemplateFromExistingCard()
    {
        var rateCardId = await SeedRateCardAsync();
        await SeedSeasonAsync(rateCardId);

        var modal = await _client.HtmxGetAsync($"/{TenantSlug}/rate-cards/templates/save/{rateCardId}");

        modal.AssertSuccess();
        await modal.AssertPartialViewAsync();
        await modal.AssertContainsAsync("Save As Template");

        var response = await _client.SubmitFormAsync(modal, "form", new Dictionary<string, string>
        {
            ["Name"] = $"Saved Template {Guid.NewGuid():N}"[..24],
            ["Description"] = "Saved from integration test"
        });

        response.AssertSuccess();
        response.AssertToast("Template saved.");

        await using var db = OpenTenantDb();
        Assert.True(await db.RateCardTemplates.AnyAsync(x => x.Description == "Saved from integration test"));
    }

    [Fact]
    public async Task RateCardDetails_RendersExportAndImportActions()
    {
        var rateCardId = await SeedRateCardAsync();
        await SeedSeasonAsync(rateCardId);

        var response = await _client.GetAsync($"/{TenantSlug}/rate-cards/details/{rateCardId}");

        response.AssertSuccess();
        await response.AssertContainsAsync("Export JSON");
        await response.AssertContainsAsync("Export CSV");
        await response.AssertContainsAsync("Import CSV");
    }

    [Fact]
    public async Task RateCardExportJson_ReturnsJsonFile()
    {
        var rateCardId = await SeedRateCardAsync();
        await SeedSeasonAsync(rateCardId);

        var response = await _client.GetAsync($"/{TenantSlug}/rate-cards/export/json/{rateCardId}");

        response.AssertSuccess();
        await response.AssertContainsAsync("\"rateCard\"");
        await response.AssertContainsAsync("\"seasons\"");
    }

    [Fact]
    public async Task RateCardExportCsv_ReturnsCsvFile()
    {
        var rateCardId = await SeedRateCardAsync();
        await SeedSeasonAsync(rateCardId);

        var response = await _client.GetAsync($"/{TenantSlug}/rate-cards/export/csv/{rateCardId}");

        response.AssertSuccess();
        await response.AssertContainsAsync("SeasonName,RoomTypeCode,RoomTypeName,RateCategoryCode,RateCategoryName,WeekdayRate,WeekendRate,IsIncluded");
        await response.AssertContainsAsync("Existing Season");
    }

    [Fact]
    public async Task RateCardImportCsvModal_RendersUploadFlow()
    {
        var rateCardId = await SeedRateCardAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/rate-cards/import/csv/{rateCardId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("Import Rates From CSV");
        await response.AssertContainsAsync("Download CSV Template");
    }

    [Fact]
    public async Task RateCardsExportAllJson_ReturnsBundleFile()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/rate-cards/export/json");

        response.AssertSuccess();
        await response.AssertContainsAsync("\"rateCards\"");
        await response.AssertContainsAsync("\"exportVersion\"");
    }

    [Fact]
    public async Task RateCardsImportJsonModal_RendersUploadFlow()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/rate-cards/import/json");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("Import Rate Cards");
        await response.AssertContainsAsync("Preview Import");
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

    private async Task<Guid> GetFirstExcursionIdAsync()
    {
        await using var db = OpenTenantDb();
        return await db.InventoryItems.Where(x => x.Kind == InventoryItemKind.Excursion).OrderBy(x => x.Name).Select(x => x.Id).FirstAsync();
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

    private async Task<Guid> SeedExcursionRateCardAsync()
    {
        await using var db = OpenTenantDb();
        var excursionId = await db.InventoryItems.Where(x => x.Kind == InventoryItemKind.Excursion).OrderBy(x => x.Name).Select(x => x.Id).FirstAsync();
        var rateCard = new RateCard
        {
            Name = $"Excursion {Guid.NewGuid():N}"[..18],
            InventoryItemId = excursionId,
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

    private async Task SeedExcursionSeasonAsync(Guid rateCardId)
    {
        await using var db = OpenTenantDb();
        var categoryIds = await db.RateCategories.Where(x => x.ForType == Modules.Settings.Entities.InventoryType.Excursion).OrderBy(x => x.SortOrder).Select(x => x.Id).ToListAsync();
        var season = new RateSeason
        {
            RateCardId = rateCardId,
            Name = "Excursion Season",
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 31),
            SortOrder = 10
        };
        foreach (var categoryId in categoryIds)
        {
            season.Rates.Add(new RoomRate
            {
                RateCategoryId = categoryId,
                WeekdayRate = 200m,
                WeekendRate = 250m,
                IsIncluded = true
            });
        }
        db.RateSeasons.Add(season);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedTemplateAsync()
    {
        await using var db = OpenTenantDb();
        var existing = await db.RateCardTemplates.FirstOrDefaultAsync(x => x.Name == "QA Hotel Template");
        if (existing is not null)
        {
            return existing.Id;
        }

        var template = new RateCardTemplate
        {
            Name = "QA Hotel Template",
            ForKind = InventoryItemKind.Hotel,
            Description = "Reusable QA hotel windows.",
            SeasonsJson = "[{\"name\":\"Peak\",\"monthStart\":10,\"dayStart\":1,\"monthEnd\":10,\"dayEnd\":31,\"sortOrder\":10},{\"name\":\"Late\",\"monthStart\":11,\"dayStart\":1,\"monthEnd\":11,\"dayEnd\":30,\"sortOrder\":20}]",
            IsSystemTemplate = true,
            CreatedAt = DateTime.UtcNow
        };

        db.RateCardTemplates.Add(template);
        await db.SaveChangesAsync();
        return template.Id;
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
