using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Inventory.Entities;
using saas.Modules.RateCards.DTOs;
using saas.Modules.RateCards.Entities;
using saas.Modules.RateCards.Services;
using saas.Modules.Settings.Entities;
using Xunit;

namespace saas.Tests.Modules.RateCards;

public class RateCardServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TenantDbContext _db = null!;
    private RateCardService _service = null!;
    private InventoryItem _hotel = null!;
    private RoomType _doubleRoom = null!;
    private RoomType _familyRoom = null!;

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
        var supplier = new Supplier { Name = "Al Haram Hotels", IsActive = true, CreatedAt = DateTime.UtcNow };
        var mealPlan = new MealPlan { Code = "BB", Name = "Bed & Breakfast", SortOrder = 10, IsActive = true, CreatedAt = DateTime.UtcNow };
        _doubleRoom = new RoomType { Code = "DBL", Name = "Double Room", SortOrder = 10, IsActive = true, CreatedAt = DateTime.UtcNow };
        _familyRoom = new RoomType { Code = "FAM", Name = "Family Room", SortOrder = 20, IsActive = true, CreatedAt = DateTime.UtcNow };
        _hotel = new InventoryItem
        {
            Name = "Grand Haram Hotel",
            Kind = InventoryItemKind.Hotel,
            BaseCost = 18000m,
            Destination = destination,
            Supplier = supplier,
            CreatedAt = DateTime.UtcNow
        };

        _db.InventoryItems.Add(_hotel);
        _db.RoomTypes.AddRange(_doubleRoom, _familyRoom);
        _db.MealPlans.Add(mealPlan);
        _db.Currencies.AddRange(
            new Currency { Code = "USD", Name = "US Dollar", Symbol = "$", ExchangeRate = 1m, IsBaseCurrency = true, IsActive = true, CreatedAt = DateTime.UtcNow },
            new Currency { Code = "SAR", Name = "Saudi Riyal", Symbol = "SAR", ExchangeRate = 3.75m, IsBaseCurrency = false, IsActive = true, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        _service = new RateCardService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task CreateEmptyAsync_LoadsHotelInventoryAndMasterData()
    {
        var dto = await _service.CreateEmptyAsync();

        Assert.Contains(dto.InventoryOptions, x => x.Label.Contains(_hotel.Name, StringComparison.Ordinal));
        Assert.Contains(dto.MealPlanOptions, x => x.Label.Contains("BB", StringComparison.Ordinal));
        Assert.Contains("USD", dto.CurrencyOptions);
    }

    [Fact]
    public async Task CreateSeasonAsync_CreatesDefaultRateRowsForActiveRoomTypes()
    {
        var rateCardId = await CreateRateCardAsync();

        await _service.CreateSeasonAsync(rateCardId, new RateSeasonFormDto
        {
            RateCardId = rateCardId,
            Name = "Peak Season",
            StartDate = new DateOnly(2026, 12, 1),
            EndDate = new DateOnly(2026, 12, 31)
        });

        var season = await _db.RateSeasons.Include(x => x.Rates).SingleAsync(x => x.RateCardId == rateCardId);
        Assert.Equal(2, season.Rates.Count);
        Assert.All(season.Rates, x => Assert.True(x.IsIncluded));
    }

    [Fact]
    public async Task CreateSeasonAsync_WhenDatesOverlap_Throws()
    {
        var rateCardId = await CreateRateCardAsync();

        await _service.CreateSeasonAsync(rateCardId, new RateSeasonFormDto
        {
            RateCardId = rateCardId,
            Name = "Spring",
            StartDate = new DateOnly(2026, 3, 1),
            EndDate = new DateOnly(2026, 3, 31)
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateSeasonAsync(rateCardId, new RateSeasonFormDto
        {
            RateCardId = rateCardId,
            Name = "Overlap",
            StartDate = new DateOnly(2026, 3, 15),
            EndDate = new DateOnly(2026, 4, 5)
        }));

        Assert.Equal("Season dates overlap with an existing season.", error.Message);
    }

    [Fact]
    public async Task ActivateAsync_ArchivesOtherActiveCardsForSameHotel()
    {
        var firstId = await CreateRateCardAsync("2026 Contract");
        var secondId = await CreateRateCardAsync("2027 Contract");

        await _service.CreateSeasonAsync(firstId, new RateSeasonFormDto
        {
            RateCardId = firstId,
            Name = "Season A",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 1, 31)
        });
        await _service.CreateSeasonAsync(secondId, new RateSeasonFormDto
        {
            RateCardId = secondId,
            Name = "Season B",
            StartDate = new DateOnly(2026, 2, 1),
            EndDate = new DateOnly(2026, 2, 28)
        });

        await _service.ActivateAsync(firstId);
        await _service.ActivateAsync(secondId);

        var cards = await _db.RateCards.OrderBy(x => x.Name).ToListAsync();
        Assert.Equal(RateCardStatus.Archived, cards.Single(x => x.Id == firstId).Status);
        Assert.Equal(RateCardStatus.Active, cards.Single(x => x.Id == secondId).Status);
    }

    [Fact]
    public async Task DuplicateAsync_CopiesSeasonsAndRatesAsDraft()
    {
        var rateCardId = await CreateRateCardAsync();
        await _service.CreateSeasonAsync(rateCardId, new RateSeasonFormDto
        {
            RateCardId = rateCardId,
            Name = "Umrah Window",
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 20)
        });

        var season = await _db.RateSeasons.SingleAsync(x => x.RateCardId == rateCardId);
        await _service.UpdateRateAsync(new RateCardRateUpdateDto
        {
            RateCardId = rateCardId,
            RateSeasonId = season.Id,
            RoomTypeId = _doubleRoom.Id,
            WeekdayRate = 2100m,
            WeekendRate = 2400m,
            IsIncluded = true
        });

        var duplicateId = await _service.DuplicateAsync(rateCardId);
        var duplicate = await _db.RateCards.Include(x => x.Seasons).ThenInclude(x => x.Rates).SingleAsync(x => x.Id == duplicateId);

        Assert.Equal(RateCardStatus.Draft, duplicate.Status);
        Assert.Contains("(Copy)", duplicate.Name, StringComparison.Ordinal);
        Assert.Single(duplicate.Seasons);
        Assert.Equal(2, duplicate.Seasons.Single().Rates.Count);
        Assert.Contains(duplicate.Seasons.Single().Rates, x => x.RoomTypeId == _doubleRoom.Id && x.WeekdayRate == 2100m);
    }

    private async Task<Guid> CreateRateCardAsync(string name = "Main Contract")
    {
        return await _service.CreateAsync(new RateCardFormDto
        {
            Name = name,
            InventoryItemId = _hotel.Id,
            ContractCurrencyCode = "USD"
        });
    }
}
