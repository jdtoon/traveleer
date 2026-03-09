using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Inventory.DTOs;
using saas.Modules.Inventory.Entities;
using saas.Modules.Inventory.Services;
using saas.Modules.Settings.Entities;
using Xunit;

namespace saas.Tests.Modules.Inventory;

public class InventoryServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TenantDbContext _db = null!;
    private InventoryService _service = null!;
    private Destination _makkah = null!;
    private Supplier _hotelSupplier = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TenantDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _makkah = new Destination { Name = "Makkah", SortOrder = 10, IsActive = true, CreatedAt = DateTime.UtcNow };
        _hotelSupplier = new Supplier { Name = "Al Haram Hotels", IsActive = true, CreatedAt = DateTime.UtcNow };
        var transportSupplier = new Supplier { Name = "Desert Transfers", IsActive = true, CreatedAt = DateTime.UtcNow };

        _db.Destinations.Add(_makkah);
        _db.Suppliers.AddRange(_hotelSupplier, transportSupplier);
        _db.InventoryItems.AddRange(
            new InventoryItem
            {
                Name = "Grand Haram Hotel",
                Kind = InventoryItemKind.Hotel,
                Description = "Premium stay near the Haram",
                BaseCost = 18000m,
                Rating = 5,
                Destination = _makkah,
                Supplier = _hotelSupplier,
                CreatedAt = DateTime.UtcNow
            },
            new InventoryItem
            {
                Name = "Desert Explorer",
                Kind = InventoryItemKind.Excursion,
                Description = "Red dune safari",
                BaseCost = 950m,
                Supplier = transportSupplier,
                CreatedAt = DateTime.UtcNow
            });
        await _db.SaveChangesAsync();

        _service = new InventoryService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task GetListAsync_WithTypeFilter_ReturnsMatchingKind()
    {
        var result = await _service.GetListAsync(nameof(InventoryItemKind.Hotel));

        Assert.Single(result.Items);
        Assert.Equal(InventoryItemKind.Hotel, result.Items[0].Kind);
    }

    [Fact]
    public async Task GetListAsync_WithSearch_MatchesDestinationAndSupplier()
    {
        var byDestination = await _service.GetListAsync(search: "makkah");
        var bySupplier = await _service.GetListAsync(search: "desert");

        Assert.Single(byDestination.Items);
        Assert.Equal("Grand Haram Hotel", byDestination.Items[0].Name);
        Assert.Single(bySupplier.Items);
        Assert.Equal("Desert Explorer", bySupplier.Items[0].Name);
    }

    [Fact]
    public async Task CreateEmptyAsync_LoadsDestinationAndSupplierOptions()
    {
        var dto = await _service.CreateEmptyAsync();

        Assert.Contains(dto.DestinationOptions, x => x.Label == "Makkah");
        Assert.Contains(dto.SupplierOptions, x => x.Label == "Al Haram Hotels");
    }

    [Fact]
    public async Task CreateAsync_TrimsValuesAndPersistsRelationships()
    {
        await _service.CreateAsync(new InventoryDto
        {
            Name = "  Skyline Suites  ",
            Kind = InventoryItemKind.Hotel,
            Description = "  Rooftop city hotel  ",
            BaseCost = 4200m,
            Address = "  Main Street  ",
            ImageUrl = "  https://example.test/hotel.jpg  ",
            Rating = 4,
            DestinationId = _makkah.Id,
            SupplierId = _hotelSupplier.Id
        });

        var created = await _db.InventoryItems.SingleAsync(x => x.Name == "Skyline Suites");
        Assert.Equal("Rooftop city hotel", created.Description);
        Assert.Equal("Main Street", created.Address);
        Assert.Equal("https://example.test/hotel.jpg", created.ImageUrl);
        Assert.Equal(_makkah.Id, created.DestinationId);
        Assert.Equal(_hotelSupplier.Id, created.SupplierId);
    }

    [Fact]
    public async Task UpdateAsync_WhenKindChangesFromHotel_ClearsRating()
    {
        var hotel = await _db.InventoryItems.SingleAsync(x => x.Kind == InventoryItemKind.Hotel);

        await _service.UpdateAsync(hotel.Id, new InventoryDto
        {
            Id = hotel.Id,
            Name = hotel.Name,
            Kind = InventoryItemKind.Transfer,
            Description = hotel.Description,
            BaseCost = hotel.BaseCost,
            Rating = 5
        });

        Assert.Null((await _db.InventoryItems.SingleAsync(x => x.Id == hotel.Id)).Rating);
    }

    [Fact]
    public async Task DeleteAsync_RemovesItem()
    {
        var item = await _db.InventoryItems.SingleAsync(x => x.Name == "Desert Explorer");

        await _service.DeleteAsync(item.Id);

        Assert.False(await _db.InventoryItems.AnyAsync(x => x.Id == item.Id));
    }
}
