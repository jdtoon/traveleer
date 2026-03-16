using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Branding.Entities;
using saas.Modules.Clients.Entities;
using saas.Modules.Inventory.Entities;
using saas.Modules.Quotes.DTOs;
using saas.Modules.Quotes.Entities;
using saas.Modules.Quotes.Services;
using saas.Modules.RateCards.Entities;
using saas.Modules.Settings.Entities;
using Xunit;

namespace saas.Tests.Modules.Quotes;

public class QuoteServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TenantDbContext _db = null!;
    private QuoteService _service = null!;
    private QuoteNumberingService _numberingService = null!;
    private Client _client = null!;
    private RateCard _rateCard = null!;
    private RateCard _secondRateCard = null!;
    private RoomType _doubleRoom = null!;
    private MealPlan _bedBreakfast = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TenantDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        var destination = new Destination { Name = "Makkah", SortOrder = 10, IsActive = true, CreatedAt = DateTime.UtcNow };
        var supplier = new Supplier { Name = "Haram Supplier", IsActive = true, CreatedAt = DateTime.UtcNow };
        _doubleRoom = new RoomType { Code = "DBL", Name = "Double", Description = "Spacious double room with city-facing windows.", SortOrder = 10, IsActive = true, CreatedAt = DateTime.UtcNow };
        _bedBreakfast = new MealPlan { Code = "BB", Name = "Bed & Breakfast", SortOrder = 10, IsActive = true, CreatedAt = DateTime.UtcNow };
        var usd = new Currency { Code = "USD", Name = "US Dollar", Symbol = "$", ExchangeRate = 1m, DefaultMarkup = 12m, IsBaseCurrency = true, IsActive = true, CreatedAt = DateTime.UtcNow };
        var sar = new Currency { Code = "SAR", Name = "Saudi Riyal", Symbol = "SAR", ExchangeRate = 3.75m, DefaultMarkup = 10m, IsBaseCurrency = false, IsActive = true, CreatedAt = DateTime.UtcNow };

        _client = new Client
        {
            Name = "Acacia Travel Group",
            Email = "quotes@acacia.test",
            Phone = "+27 11 555 9999",
            CreatedAt = DateTime.UtcNow
        };

        var hotel = new InventoryItem
        {
            Name = "Grand Haram Hotel",
            Kind = InventoryItemKind.Hotel,
            Description = "Five-star property overlooking the Haram precinct.",
            ImageUrl = "/favicon.svg",
            Rating = 5,
            BaseCost = 18000m,
            Destination = destination,
            Supplier = supplier,
            CreatedAt = DateTime.UtcNow
        };

        var secondHotel = new InventoryItem
        {
            Name = "Mina View Suites",
            Kind = InventoryItemKind.Hotel,
            BaseCost = 15000m,
            Destination = destination,
            Supplier = supplier,
            CreatedAt = DateTime.UtcNow
        };

        _rateCard = new RateCard
        {
            Name = "Main Contract",
            InventoryItem = hotel,
            DefaultMealPlan = _bedBreakfast,
            ContractCurrencyCode = "SAR",
            Status = RateCardStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        _rateCard.Seasons.Add(new RateSeason
        {
            Name = "Peak",
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 31),
            SortOrder = 10,
            Rates =
            {
                new RoomRate
                {
                    RoomType = _doubleRoom,
                    WeekdayRate = 375m,
                    WeekendRate = 450m,
                    IsIncluded = true
                }
            }
        });
        _rateCard.Seasons.Add(new RateSeason
        {
            Name = "Late Season",
            StartDate = new DateOnly(2026, 11, 1),
            EndDate = new DateOnly(2026, 11, 30),
            SortOrder = 20,
            Rates =
            {
                new RoomRate
                {
                    RoomType = _doubleRoom,
                    WeekdayRate = 300m,
                    WeekendRate = 350m,
                    IsIncluded = true
                }
            }
        });

        _secondRateCard = new RateCard
        {
            Name = "Secondary Contract",
            InventoryItem = secondHotel,
            ContractCurrencyCode = "USD",
            Status = RateCardStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };
        _secondRateCard.Seasons.Add(new RateSeason
        {
            Name = "All Year",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            SortOrder = 10,
            Rates =
            {
                new RoomRate
                {
                    RoomType = _doubleRoom,
                    WeekdayRate = 200m,
                    WeekendRate = 220m,
                    IsIncluded = true
                }
            }
        });

        _db.Clients.Add(_client);
        _db.Currencies.AddRange(usd, sar);
        _db.MealPlans.Add(_bedBreakfast);
        _db.RoomTypes.Add(_doubleRoom);
        _db.RateCards.AddRange(_rateCard, _secondRateCard);
        await _db.SaveChangesAsync();

        _numberingService = new QuoteNumberingService(_db);
        _service = new QuoteService(_db, _numberingService, new saas.Infrastructure.Services.UserNameResolver(_db));
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task CreateAsync_GeneratesIncrementingQuoteReferenceAndPreservesSelectedOrder()
    {
        var firstId = await _service.CreateAsync(new QuoteBuilderDto
        {
            ClientId = _client.Id,
            ClientName = _client.Name,
            OutputCurrencyCode = "USD",
            SelectedRateCardIds = [_secondRateCard.Id, _rateCard.Id]
        });

        var secondId = await _service.CreateAsync(new QuoteBuilderDto
        {
            ClientId = _client.Id,
            ClientName = _client.Name,
            OutputCurrencyCode = "USD",
            SelectedRateCardIds = [_rateCard.Id]
        });

        var first = await _db.Quotes.Include(x => x.QuoteRateCards).SingleAsync(x => x.Id == firstId);
        var second = await _db.Quotes.SingleAsync(x => x.Id == secondId);

        Assert.EndsWith("0001", first.ReferenceNumber);
        Assert.EndsWith("0002", second.ReferenceNumber);
        Assert.Equal(new[] { _secondRateCard.Id, _rateCard.Id }, first.QuoteRateCards.OrderBy(x => x.SortOrder).Select(x => x.RateCardId).ToArray());
    }

    [Fact]
    public async Task BuildPreviewAsync_AppliesTravelDateFilteringAndCurrencyMarkup()
    {
        var preview = await _service.BuildPreviewAsync(new QuoteBuilderDto
        {
            ClientName = "Preview Client",
            OutputCurrencyCode = "USD",
            MarkupPercentage = 10m,
            FilterByTravelDates = true,
            TravelStartDate = new DateOnly(2026, 10, 10),
            TravelEndDate = new DateOnly(2026, 10, 15),
            SelectedRateCardIds = [_rateCard.Id]
        });

        var item = Assert.Single(preview.Items);
        var season = Assert.Single(item.Seasons);
        var rate = Assert.Single(season.Rates);

        Assert.Equal("Peak", season.Name);
        Assert.Equal(110m, rate.WeekdayRate);
        Assert.Equal(132m, rate.WeekendRate);
    }

    [Fact]
    public async Task PopulateOptionsAsync_MarksPreviouslySelectedRateCards()
    {
        var dto = new QuoteBuilderDto
        {
            SelectedRateCardIds = [_rateCard.Id]
        };

        await _service.PopulateOptionsAsync(dto);

        Assert.Equal(2, dto.AvailableRateCards.Count);
        Assert.Contains(dto.AvailableRateCards, x => x.Id == _rateCard.Id && x.IsSelected);
        Assert.Contains(dto.AvailableRateCards, x => x.Id == _secondRateCard.Id && !x.IsSelected);
    }

    [Fact]
    public async Task CreateAsync_UsesClientSnapshotWhenFormFieldsAreBlank()
    {
        var quoteId = await _service.CreateAsync(new QuoteBuilderDto
        {
            ClientId = _client.Id,
            ClientName = string.Empty,
            ClientEmail = string.Empty,
            ClientPhone = string.Empty,
            OutputCurrencyCode = "USD",
            SelectedRateCardIds = [_rateCard.Id]
        });

        var quote = await _db.Quotes.SingleAsync(x => x.Id == quoteId);
        Assert.Equal(_client.Name, quote.ClientName);
        Assert.Equal(_client.Email, quote.ClientEmail);
        Assert.Equal(_client.Phone, quote.ClientPhone);
    }

    [Fact]
    public async Task CreateEmptyAsync_UsesBrandingDefaultsAndNumbering()
    {
        _db.BrandingSettings.Add(new BrandingSettings
        {
            AgencyName = "Acacia Journeys",
            QuotePrefix = "ACJ",
            QuoteNumberFormat = "{PREFIX}-{YEAR2}-{SEQ:3}",
            NextQuoteSequence = 1,
            QuoteResetSequenceYearly = false,
            DefaultQuoteValidityDays = 21,
            DefaultQuoteMarkupPercentage = 18m
        });
        await _db.SaveChangesAsync();

        var empty = await _service.CreateEmptyAsync();
        var quoteId = await _service.CreateAsync(new QuoteBuilderDto
        {
            ClientId = _client.Id,
            ClientName = _client.Name,
            OutputCurrencyCode = "USD",
            SelectedRateCardIds = [_rateCard.Id]
        });

        var quote = await _db.Quotes.SingleAsync(x => x.Id == quoteId);

        Assert.Equal(18m, empty.MarkupPercentage);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(21)), empty.ValidUntil);
        Assert.Equal($"ACJ-{(DateTime.UtcNow.Year % 100):D2}-001", quote.ReferenceNumber);
    }

    [Fact]
    public async Task CreateAsync_CreatesInitialVersionSnapshot()
    {
        var quoteId = await _service.CreateAsync(new QuoteBuilderDto
        {
            ClientId = _client.Id,
            ClientName = _client.Name,
            OutputCurrencyCode = "USD",
            SelectedRateCardIds = [_rateCard.Id]
        });

        var versions = await _db.QuoteVersions
            .Where(x => x.QuoteId == quoteId)
            .OrderBy(x => x.VersionNumber)
            .ToListAsync();

        var version = Assert.Single(versions);
        Assert.Equal(1, version.VersionNumber);

        var history = await _service.GetVersionHistoryAsync(quoteId);
        Assert.NotNull(history);
        Assert.Single(history!.Versions);
        Assert.Equal("USD", history.Versions[0].OutputCurrencyCode);
    }

    [Fact]
    public async Task UpdateAsync_AppendsNewVersionSnapshot()
    {
        var quoteId = await _service.CreateAsync(new QuoteBuilderDto
        {
            ClientId = _client.Id,
            ClientName = _client.Name,
            OutputCurrencyCode = "USD",
            MarkupPercentage = 10m,
            SelectedRateCardIds = [_rateCard.Id]
        });

        await _service.UpdateAsync(quoteId, new QuoteBuilderDto
        {
            ClientId = _client.Id,
            ClientName = _client.Name,
            OutputCurrencyCode = "SAR",
            MarkupPercentage = 18m,
            SelectedRateCardIds = [_rateCard.Id, _secondRateCard.Id],
            Notes = "Repriced with second contract"
        });

        var history = await _service.GetVersionHistoryAsync(quoteId);
        Assert.NotNull(history);
        Assert.Equal(2, history!.Versions.Count);
        Assert.Equal(2, history.Versions[0].VersionNumber);
        Assert.True(history.Versions[0].IsCurrent);

        var versionDetails = await _service.GetVersionDetailsAsync(quoteId, history.Versions[0].Id);
        Assert.NotNull(versionDetails);
        Assert.Equal("SAR", versionDetails!.Snapshot.OutputCurrencyCode);
        Assert.Equal(18m, versionDetails.Snapshot.MarkupPercentage);
        Assert.Equal("grid", versionDetails.Snapshot.TemplateLayout);
        Assert.Equal(2, versionDetails.Snapshot.SelectedRateCardIds.Count);
        Assert.Equal("Repriced with second contract", versionDetails.Snapshot.Notes);
    }

    [Fact]
    public async Task CreateAndPreview_PersistAndReflectTemplateDisplaySettings()
    {
        _db.BrandingSettings.Add(new BrandingSettings
        {
            AgencyName = "Acacia Journeys",
            PdfFooterText = "Subject to supplier reconfirmation."
        });
        await _db.SaveChangesAsync();

        var quoteId = await _service.CreateAsync(new QuoteBuilderDto
        {
            ClientId = _client.Id,
            ClientName = _client.Name,
            OutputCurrencyCode = "USD",
            MarkupPercentage = 14m,
            TemplateLayout = "compact",
            ShowImages = true,
            ShowMealPlan = true,
            ShowFooter = false,
            ShowRoomDescriptions = true,
            SelectedRateCardIds = [_rateCard.Id]
        });

        var quote = await _db.Quotes.SingleAsync(x => x.Id == quoteId);
        Assert.Equal("compact", quote.TemplateLayout);
        Assert.True(quote.ShowImages);
        Assert.True(quote.ShowMealPlan);
        Assert.False(quote.ShowFooter);
        Assert.True(quote.ShowRoomDescriptions);

        var preview = await _service.BuildPreviewAsync(quoteId);
        Assert.NotNull(preview);
        Assert.Equal("compact", preview!.TemplateLayout);
        Assert.False(preview.ShowFooter);
        Assert.Null(preview.FooterText);

        var item = Assert.Single(preview.Items);
        Assert.Equal("/favicon.svg", item.ImageUrl);
        Assert.Equal("BB", item.MealPlanCode);
        Assert.Equal("Bed & Breakfast", item.MealPlanName);
        Assert.Equal("Spacious double room with city-facing windows.", Assert.Single(item.RoomTypes).Description);
    }
}




