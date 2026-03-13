using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Infrastructure;
using saas.Modules.Inventory.Entities;
using saas.Modules.RateCards.DTOs;
using saas.Modules.RateCards.Entities;
using saas.Modules.RateCards.Services;
using saas.Modules.Settings.Entities;
using Xunit;

namespace saas.Tests.Modules.RateCards;

public class RateCardImportExportServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TenantDbContext _db = null!;
    private RateCardImportExportService _service = null!;
    private FakeCacheService _cache = null!;
    private Guid _rateCardId;
    private Guid _seasonId;
    private Guid _doubleRoomId;
    private Guid _excursionRateCardId;
    private Guid _adultCategoryId;

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
        var roomDouble = new RoomType { Code = "DBL", Name = "Double Room", SortOrder = 10, IsActive = true, CreatedAt = DateTime.UtcNow };
        var roomFamily = new RoomType { Code = "FAM", Name = "Family Room", SortOrder = 20, IsActive = true, CreatedAt = DateTime.UtcNow };
        var adultCategory = new RateCategory { ForType = InventoryType.Excursion, Code = "ADT", Name = "Adult", SortOrder = 10, IsActive = true, CreatedAt = DateTime.UtcNow };
        var childCategory = new RateCategory { ForType = InventoryType.Excursion, Code = "CHD", Name = "Child", SortOrder = 20, IsActive = true, CreatedAt = DateTime.UtcNow };
        _doubleRoomId = roomDouble.Id;
        _adultCategoryId = adultCategory.Id;

        var hotel = new InventoryItem
        {
            Name = "Grand Haram Hotel",
            Kind = InventoryItemKind.Hotel,
            Destination = destination,
            Supplier = supplier,
            CreatedAt = DateTime.UtcNow
        };
        var excursion = new InventoryItem
        {
            Name = "Makkah Heritage Tour",
            Kind = InventoryItemKind.Excursion,
            Destination = destination,
            Supplier = supplier,
            CreatedAt = DateTime.UtcNow
        };

        var rateCard = new RateCard
        {
            Name = "Export Contract",
            InventoryItem = hotel,
            ContractCurrencyCode = "USD",
            Status = RateCardStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };

        var season = new RateSeason
        {
            Name = "Existing Season",
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 31),
            SortOrder = 10,
            Rates =
            [
                new RoomRate { RoomType = roomDouble, WeekdayRate = 1500m, WeekendRate = 1800m, IsIncluded = true },
                new RoomRate { RoomType = roomFamily, WeekdayRate = 2200m, WeekendRate = 2400m, IsIncluded = false }
            ]
        };

        rateCard.Seasons.Add(season);

        var excursionRateCard = new RateCard
        {
            Name = "Excursion Export Contract",
            InventoryItem = excursion,
            ContractCurrencyCode = "USD",
            Status = RateCardStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            Seasons =
            [
                new RateSeason
                {
                    Name = "Weekend Tours",
                    StartDate = new DateOnly(2026, 9, 1),
                    EndDate = new DateOnly(2026, 9, 30),
                    SortOrder = 10,
                    Rates =
                    [
                        new RoomRate { RateCategory = adultCategory, WeekdayRate = 450m, WeekendRate = 500m, IsIncluded = true },
                        new RoomRate { RateCategory = childCategory, WeekdayRate = 250m, WeekendRate = 300m, IsIncluded = true }
                    ]
                }
            ]
        };

        _db.RateCards.AddRange(rateCard, excursionRateCard);
        await _db.SaveChangesAsync();

        _rateCardId = rateCard.Id;
        _seasonId = season.Id;
        _excursionRateCardId = excursionRateCard.Id;
        _cache = new FakeCacheService();
        _service = new RateCardImportExportService(_db, _cache);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ExportJsonAsync_IncludesSeasonAndRoomRateData()
    {
        var export = await _service.ExportJsonAsync(_rateCardId, "qa@example.test");

        Assert.Equal("qa@example.test", export.ExportedBy);
        Assert.Equal("Export Contract", export.RateCard.Name);
        Assert.Single(export.RateCard.Seasons);
        Assert.Equal(2, export.RateCard.Seasons.Single().Rates.Count);
        Assert.Contains(export.RateCard.Seasons.Single().Rates, x => x.RoomTypeCode == "DBL" && x.WeekdayRate == 1500m);
    }

    [Fact]
    public async Task ExportCsvAsync_ReturnsExpectedHeaderAndRows()
    {
        var csv = await _service.ExportCsvAsync(_rateCardId);

        Assert.Contains("SeasonName,RoomTypeCode,RoomTypeName,RateCategoryCode,RateCategoryName,WeekdayRate,WeekendRate,IsIncluded", csv, StringComparison.Ordinal);
        Assert.Contains("Existing Season,DBL,Double Room,,,1500.00,1800.00,true", csv, StringComparison.Ordinal);
        Assert.Contains("Existing Season,FAM,Family Room,,,2200.00,2400.00,false", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportJsonAsync_ForExcursion_UsesRateCategories()
    {
        var export = await _service.ExportJsonAsync(_excursionRateCardId);

        Assert.Equal(InventoryItemKind.Excursion, export.RateCard.InventoryKind);
        Assert.Contains(export.RateCard.Seasons.Single().Rates, x => x.RateCategoryCode == "ADT" && x.WeekdayRate == 450m);
    }

    [Fact]
    public async Task ExportCsvAsync_ForExcursion_WritesRateCategoryColumns()
    {
        var csv = await _service.ExportCsvAsync(_excursionRateCardId);

        Assert.Contains("Weekend Tours,,,ADT,Adult,450.00,500.00,true", csv, StringComparison.Ordinal);
        Assert.Contains("Weekend Tours,,,CHD,Child,250.00,300.00,true", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAllJsonAsync_ReturnsBundleWithCards()
    {
        var export = await _service.ExportAllJsonAsync("qa@example.test");

        Assert.Equal("qa@example.test", export.ExportedBy);
        Assert.NotEmpty(export.RateCards);
        Assert.Contains(export.RateCards, x => x.Name == "Export Contract");
    }

    [Fact]
    public async Task PreviewCsvImportAsync_ValidatesRowsAndCachesSession()
    {
        var csv = string.Join('\n',
            "SeasonName,RoomTypeCode,WeekdayRate,WeekendRate,IsIncluded",
            "Existing Season,DBL,1999.50,2222.75,false",
            "Missing Season,FAM,1200,,true");

        var preview = await _service.PreviewCsvImportAsync(_rateCardId, csv);

        Assert.True(preview.CanImport);
        Assert.Equal(1, preview.ValidRowCount);
        Assert.Equal(1, preview.InvalidRowCount);
        Assert.NotNull(preview.ImportToken);
        Assert.Contains(preview.Rows, x => x.IsValid && x.RoomTypeCode == "DBL");
        Assert.Contains(preview.Warnings, x => x.Contains("Missing Season", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteCsvImportAsync_UpdatesMatchingRoomRates()
    {
        var csv = string.Join('\n',
            "SeasonName,RoomTypeCode,WeekdayRate,WeekendRate,IsIncluded",
            "Existing Season,DBL,1999.50,2222.75,false");

        var preview = await _service.PreviewCsvImportAsync(_rateCardId, csv);
        var result = await _service.ExecuteCsvImportAsync(_rateCardId, preview.ImportToken!);

        Assert.Equal(1, result.ImportedRowCount);

        var updated = await _db.RoomRates.SingleAsync(x => x.RateSeasonId == _seasonId && x.RoomTypeId == _doubleRoomId);
        Assert.Equal(1999.50m, updated.WeekdayRate);
        Assert.Equal(2222.75m, updated.WeekendRate);
        Assert.False(updated.IsIncluded);
    }

    [Fact]
    public async Task PreviewCsvImportAsync_ForExcursion_UsesRateCategoryColumns()
    {
        var seasonId = await _db.RateSeasons.Where(x => x.RateCardId == _excursionRateCardId).Select(x => x.Id).SingleAsync();
        var csv = string.Join('\n',
            "SeasonName,RateCategoryCode,WeekdayRate,WeekendRate,IsIncluded",
            "Weekend Tours,ADT,555.00,650.00,true");

        var preview = await _service.PreviewCsvImportAsync(_excursionRateCardId, csv);

        Assert.True(preview.CanImport);
        Assert.Single(preview.Rows);
        Assert.Equal(_adultCategoryId, preview.Rows.Single().RateCategoryId);

        var result = await _service.ExecuteCsvImportAsync(_excursionRateCardId, preview.ImportToken!);
        Assert.Equal(1, result.ImportedRowCount);

        var updated = await _db.RoomRates.SingleAsync(x => x.RateSeasonId == seasonId && x.RateCategoryId == _adultCategoryId);
        Assert.Equal(555m, updated.WeekdayRate);
        Assert.Equal(650m, updated.WeekendRate);
    }

    [Fact]
    public async Task ExecuteJsonImportAsync_ForExcursion_CreatesCategoryBasedDraft()
    {
        var payload = new RateCardJsonExportBundleDto
        {
            RateCards =
            [
                new RateCardJsonExportCardDto
                {
                    Name = "Imported Excursion Contract",
                    InventoryKind = InventoryItemKind.Excursion,
                    InventoryItemName = "Imported Excursion",
                    DestinationName = "Makkah",
                    ContractCurrencyCode = "USD",
                    Seasons =
                    [
                        new RateCardJsonExportSeasonDto
                        {
                            Name = "Peak Excursions",
                            StartDate = new DateOnly(2027, 3, 1),
                            EndDate = new DateOnly(2027, 3, 31),
                            SortOrder = 10,
                            Rates =
                            [
                                new RateCardJsonExportRateDto { RateCategoryCode = "ADT", RateCategoryName = "Adult", WeekdayRate = 700m, WeekendRate = 800m, IsIncluded = true }
                            ]
                        }
                    ]
                }
            ]
        };

        var preview = await _service.PreviewJsonImportAsync(System.Text.Json.JsonSerializer.Serialize(payload));
        var result = await _service.ExecuteJsonImportAsync(preview.ImportToken!);

        Assert.Equal(1, result.ImportedCount);

        var imported = await _db.RateCards.Include(x => x.InventoryItem).Include(x => x.Seasons).ThenInclude(x => x.Rates).SingleAsync(x => x.Name == "Imported Excursion Contract");
        Assert.Equal(InventoryItemKind.Excursion, imported.InventoryItem!.Kind);
        Assert.Contains(imported.Seasons.Single().Rates, x => x.RateCategoryId == _adultCategoryId && x.WeekdayRate == 700m);
    }

    [Fact]
    public async Task PreviewJsonImportAsync_DetectsDuplicateAndMissingReferences()
    {
        var payload = new RateCardJsonExportBundleDto
        {
            ExportedBy = "qa@example.test",
            RateCards =
            [
                new RateCardJsonExportCardDto
                {
                    Name = "Export Contract",
                    InventoryItemName = "Grand Haram Hotel",
                    DestinationName = "Makkah",
                    ContractCurrencyCode = "USD",
                    Seasons =
                    [
                        new RateCardJsonExportSeasonDto
                        {
                            Name = "Existing Season",
                            StartDate = new DateOnly(2026, 8, 1),
                            EndDate = new DateOnly(2026, 8, 31),
                            SortOrder = 10,
                            Rates =
                            [
                                new RateCardJsonExportRateDto { RoomTypeCode = "DBL", RoomTypeName = "Double Room", WeekdayRate = 1234m, WeekendRate = 1500m, IsIncluded = true }
                            ]
                        }
                    ]
                },
                new RateCardJsonExportCardDto
                {
                    Name = "Imported New Contract",
                    InventoryItemName = "Imported Hotel",
                    DestinationName = "Madinah",
                    DefaultMealPlanCode = "HB",
                    DefaultMealPlanName = "Half Board",
                    ContractCurrencyCode = "USD",
                    Seasons =
                    [
                        new RateCardJsonExportSeasonDto
                        {
                            Name = "Ramadan",
                            StartDate = new DateOnly(2027, 2, 1),
                            EndDate = new DateOnly(2027, 2, 28),
                            SortOrder = 10,
                            Rates =
                            [
                                new RateCardJsonExportRateDto { RoomTypeCode = "STE", RoomTypeName = "Suite", WeekdayRate = 3000m, WeekendRate = 3500m, IsIncluded = true }
                            ]
                        }
                    ]
                }
            ]
        };

        var preview = await _service.PreviewJsonImportAsync(System.Text.Json.JsonSerializer.Serialize(payload));

        Assert.True(preview.CanImport);
        Assert.Equal(2, preview.Items.Count);
        Assert.Contains(preview.Items, x => x.IsDuplicate && x.Name == "Export Contract");
        Assert.Contains(preview.Items, x => !x.IsDuplicate && x.CreatesDestination && x.CreatesInventoryItem && x.Name == "Imported New Contract");
    }

    [Fact]
    public async Task ExecuteJsonImportAsync_CreatesNewDraftAndCanReplaceDuplicate()
    {
        var payload = new RateCardJsonExportBundleDto
        {
            RateCards =
            [
                new RateCardJsonExportCardDto
                {
                    Name = "Export Contract",
                    InventoryItemName = "Grand Haram Hotel",
                    DestinationName = "Makkah",
                    ContractCurrencyCode = "USD",
                    Seasons =
                    [
                        new RateCardJsonExportSeasonDto
                        {
                            Name = "Replacement Season",
                            StartDate = new DateOnly(2026, 9, 1),
                            EndDate = new DateOnly(2026, 9, 30),
                            SortOrder = 10,
                            Rates =
                            [
                                new RateCardJsonExportRateDto { RoomTypeCode = "DBL", RoomTypeName = "Double Room", WeekdayRate = 4444m, WeekendRate = 4555m, IsIncluded = false }
                            ]
                        }
                    ]
                },
                new RateCardJsonExportCardDto
                {
                    Name = "Imported New Contract",
                    InventoryItemName = "Imported Hotel",
                    DestinationName = "Madinah",
                    DefaultMealPlanCode = "HB",
                    DefaultMealPlanName = "Half Board",
                    ContractCurrencyCode = "USD",
                    Seasons =
                    [
                        new RateCardJsonExportSeasonDto
                        {
                            Name = "Fresh Season",
                            StartDate = new DateOnly(2027, 1, 1),
                            EndDate = new DateOnly(2027, 1, 31),
                            SortOrder = 10,
                            Rates =
                            [
                                new RateCardJsonExportRateDto { RoomTypeCode = "STE", RoomTypeName = "Suite", WeekdayRate = 3000m, WeekendRate = 3300m, IsIncluded = true }
                            ]
                        }
                    ]
                }
            ]
        };

        var preview = await _service.PreviewJsonImportAsync(System.Text.Json.JsonSerializer.Serialize(payload));
        var result = await _service.ExecuteJsonImportAsync(preview.ImportToken!, new Dictionary<int, string>
        {
            [0] = "replace"
        });

        Assert.Equal(2, result.ImportedCount);

        var replaced = await _db.RateCards.Include(x => x.Seasons).ThenInclude(x => x.Rates).SingleAsync(x => x.Name == "Export Contract");
        Assert.Equal(RateCardStatus.Draft, replaced.Status);
        Assert.Single(replaced.Seasons);
        Assert.Equal("Replacement Season", replaced.Seasons.Single().Name);
        Assert.Contains(replaced.Seasons.Single().Rates, x => x.RoomTypeId == _doubleRoomId && x.WeekdayRate == 4444m && x.WeekendRate == 4555m && !x.IsIncluded);

        var imported = await _db.RateCards.Include(x => x.InventoryItem).Include(x => x.Seasons).ThenInclude(x => x.Rates).SingleAsync(x => x.Name == "Imported New Contract");
        Assert.Equal(RateCardStatus.Draft, imported.Status);
        Assert.Equal("Imported Hotel", imported.InventoryItem!.Name);
        Assert.Single(imported.Seasons);
        Assert.True(await _db.Destinations.AnyAsync(x => x.Name == "Madinah"));
        Assert.True(await _db.MealPlans.AnyAsync(x => x.Code == "HB"));
        Assert.True(await _db.RoomTypes.AnyAsync(x => x.Code == "STE"));
    }

    private sealed class FakeCacheService : ICacheService
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
            => Task.FromResult(_values.TryGetValue(key, out var value) ? value as T : null);

        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default) where T : class
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
