using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
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
        _doubleRoom = new RoomType { Code = "DBL", Name = "Double", SortOrder = 10, IsActive = true, CreatedAt = DateTime.UtcNow };
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
        _db.RoomTypes.Add(_doubleRoom);
        _db.RateCards.AddRange(_rateCard, _secondRateCard);
        await _db.SaveChangesAsync();

        _numberingService = new QuoteNumberingService(_db);
        _service = new QuoteService(_db, _numberingService);
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
}
