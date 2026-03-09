using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Settings.DTOs;
using saas.Modules.Settings.Entities;
using saas.Modules.Settings.Services;
using Xunit;

namespace saas.Tests.Modules.Settings;

public class SettingsServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TenantDbContext _db = null!;
    private SettingsService _service = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TenantDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _db.RoomTypes.Add(new RoomType { Code = "DBL", Name = "Double Room", SortOrder = 20, CreatedAt = DateTime.UtcNow });
        _db.Currencies.AddRange(
            new Currency { Code = "ZAR", Name = "Rand", Symbol = "R", ExchangeRate = 1m, IsBaseCurrency = true, CreatedAt = DateTime.UtcNow },
            new Currency { Code = "USD", Name = "Dollar", Symbol = "$", ExchangeRate = 0.055m, DefaultMarkup = 10m, CreatedAt = DateTime.UtcNow });
        _db.RateCategories.Add(new RateCategory { ForType = InventoryType.Transfer, Code = "VAN", Name = "Van", Capacity = 12, SortOrder = 10, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        _service = new SettingsService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task GetRoomTypesAsync_ReturnsOrderedItems()
    {
        _db.RoomTypes.Add(new RoomType { Code = "SGL", Name = "Single Room", SortOrder = 10, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _service.GetRoomTypesAsync();

        Assert.Equal("SGL", result[0].Code);
        Assert.Equal("DBL", result[1].Code);
    }

    [Fact]
    public async Task RoomTypeCodeExistsAsync_IsCaseInsensitiveAfterNormalization()
    {
        var exists = await _service.RoomTypeCodeExistsAsync(" dbl ");

        Assert.True(exists);
    }

    [Fact]
    public async Task CreateRoomTypeAsync_TrimsAndUppercasesCode()
    {
        await _service.CreateRoomTypeAsync(new RoomTypeDto
        {
            Code = " ste ",
            Name = "  Suite  ",
            Description = "  Premium room  ",
            SortOrder = 30,
            IsActive = true
        });

        var created = await _db.RoomTypes.SingleAsync(x => x.Code == "STE");
        Assert.Equal("Suite", created.Name);
        Assert.Equal("Premium room", created.Description);
    }

    [Fact]
    public async Task UpdateCurrencyAsync_ClearsPreviousBaseCurrency()
    {
        var usd = await _db.Currencies.SingleAsync(x => x.Code == "USD");

        await _service.UpdateCurrencyAsync(usd.Id, new CurrencyDto
        {
            Id = usd.Id,
            Code = "USD",
            Name = "US Dollar",
            Symbol = "$",
            ExchangeRate = 0.06m,
            DefaultMarkup = 12m,
            RoundingRule = RoundingRule.Nearest10,
            IsBaseCurrency = true,
            IsActive = true
        });

        Assert.True((await _db.Currencies.SingleAsync(x => x.Code == "USD")).IsBaseCurrency);
        Assert.False((await _db.Currencies.SingleAsync(x => x.Code == "ZAR")).IsBaseCurrency);
    }

    [Fact]
    public async Task DeleteCurrencyAsync_BaseCurrencyThrows()
    {
        var zar = await _db.Currencies.SingleAsync(x => x.Code == "ZAR");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteCurrencyAsync(zar.Id));
    }

    [Fact]
    public async Task SetBaseCurrencyAsync_SetsOnlyOneBase()
    {
        var usd = await _db.Currencies.SingleAsync(x => x.Code == "USD");

        await _service.SetBaseCurrencyAsync(usd.Id);

        Assert.Single(await _db.Currencies.Where(x => x.IsBaseCurrency).ToListAsync());
        Assert.True((await _db.Currencies.SingleAsync(x => x.Code == "USD")).IsBaseCurrency);
    }

    [Fact]
    public async Task CreateDestinationAsync_NormalizesCountryCode()
    {
        await _service.CreateDestinationAsync(new DestinationDto
        {
            Name = "Jeddah",
            CountryCode = " sa ",
            CountryName = "Saudi Arabia",
            Region = "Middle East",
            SortOrder = 10,
            IsActive = true
        });

        var created = await _db.Destinations.SingleAsync(x => x.Name == "Jeddah");
        Assert.Equal("SA", created.CountryCode);
    }

    [Fact]
    public async Task CreateRateCategoryAsync_ClearsCapacityForNonTransfer()
    {
        await _service.CreateRateCategoryAsync(new RateCategoryDto
        {
            ForType = InventoryType.Flight,
            Code = "BUS",
            Name = "Business",
            Capacity = 5,
            SortOrder = 20,
            IsActive = true
        });

        var created = await _db.RateCategories.SingleAsync(x => x.Code == "BUS");
        Assert.Null(created.Capacity);
    }

    [Fact]
    public async Task GetRateCategoryGroupsAsync_ReturnsAllConfiguredTypes()
    {
        var groups = await _service.GetRateCategoryGroupsAsync();

        Assert.Equal(4, groups.Count);
        Assert.Contains(groups, x => x.Type == InventoryType.Transfer && x.Items.Any());
    }
}
