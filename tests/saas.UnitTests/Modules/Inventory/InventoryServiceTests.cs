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

    [Fact]
    public async Task CreateAsync_TransferItem_PersistsTransportFields()
    {
        await _service.CreateAsync(new InventoryDto
        {
            Name = "Airport Shuttle",
            Kind = InventoryItemKind.Transfer,
            BaseCost = 450m,
            PickupLocation = "  OR Tambo International Airport  ",
            DropoffLocation = "  Sandton City Hotel  ",
            VehicleType = "Minibus",
            MaxPassengers = 12,
            IncludesMeetAndGreet = true,
            TransferDurationMinutes = 45
        });

        var created = await _db.InventoryItems.SingleAsync(x => x.Name == "Airport Shuttle");
        Assert.Equal("OR Tambo International Airport", created.PickupLocation);
        Assert.Equal("Sandton City Hotel", created.DropoffLocation);
        Assert.Equal("Minibus", created.VehicleType);
        Assert.Equal(12, created.MaxPassengers);
        Assert.True(created.IncludesMeetAndGreet);
        Assert.Equal(45, created.TransferDurationMinutes);
    }

    [Fact]
    public async Task CreateAsync_NonTransferItem_ClearsTransportFields()
    {
        await _service.CreateAsync(new InventoryDto
        {
            Name = "Hotel With Transport Data",
            Kind = InventoryItemKind.Hotel,
            BaseCost = 5000m,
            PickupLocation = "Should be cleared",
            DropoffLocation = "Should be cleared",
            VehicleType = "Sedan",
            MaxPassengers = 4,
            IncludesMeetAndGreet = true,
            TransferDurationMinutes = 30
        });

        var created = await _db.InventoryItems.SingleAsync(x => x.Name == "Hotel With Transport Data");
        Assert.Null(created.PickupLocation);
        Assert.Null(created.DropoffLocation);
        Assert.Null(created.VehicleType);
        Assert.Null(created.MaxPassengers);
        Assert.False(created.IncludesMeetAndGreet);
        Assert.Null(created.TransferDurationMinutes);
    }

    [Fact]
    public async Task UpdateAsync_TransferToHotel_ClearsTransportFields()
    {
        await _service.CreateAsync(new InventoryDto
        {
            Name = "Temp Transfer",
            Kind = InventoryItemKind.Transfer,
            BaseCost = 300m,
            PickupLocation = "Airport",
            DropoffLocation = "Hotel",
            VehicleType = "Sedan",
            MaxPassengers = 4,
            IncludesMeetAndGreet = true,
            TransferDurationMinutes = 30
        });

        var item = await _db.InventoryItems.SingleAsync(x => x.Name == "Temp Transfer");
        await _service.UpdateAsync(item.Id, new InventoryDto
        {
            Id = item.Id,
            Name = "Temp Transfer",
            Kind = InventoryItemKind.Hotel,
            BaseCost = 300m,
            PickupLocation = "Airport",
            DropoffLocation = "Hotel"
        });

        var updated = await _db.InventoryItems.SingleAsync(x => x.Id == item.Id);
        Assert.Null(updated.PickupLocation);
        Assert.Null(updated.DropoffLocation);
        Assert.Null(updated.VehicleType);
        Assert.Null(updated.MaxPassengers);
        Assert.False(updated.IncludesMeetAndGreet);
        Assert.Null(updated.TransferDurationMinutes);
    }

    [Fact]
    public async Task GetListAsync_TransferItem_ProjectsTransportFields()
    {
        await _service.CreateAsync(new InventoryDto
        {
            Name = "City Transfer",
            Kind = InventoryItemKind.Transfer,
            BaseCost = 200m,
            PickupLocation = "CBD Station",
            DropoffLocation = "Beach Resort",
            VehicleType = "Luxury SUV",
            MaxPassengers = 6,
            IncludesMeetAndGreet = false,
            TransferDurationMinutes = 20
        });

        var result = await _service.GetListAsync(nameof(InventoryItemKind.Transfer));
        var item = Assert.Single(result.Items);
        Assert.Equal("CBD Station", item.PickupLocation);
        Assert.Equal("Beach Resort", item.DropoffLocation);
        Assert.Equal("Luxury SUV", item.VehicleType);
        Assert.Equal(6, item.MaxPassengers);
        Assert.False(item.IncludesMeetAndGreet);
        Assert.Equal(20, item.TransferDurationMinutes);
    }

    [Fact]
    public async Task GetAsync_TransferItem_IncludesTransportFields()
    {
        await _service.CreateAsync(new InventoryDto
        {
            Name = "Lodge Transfer",
            Kind = InventoryItemKind.Transfer,
            BaseCost = 350m,
            PickupLocation = "Kruger Gate",
            DropoffLocation = "Safari Lodge",
            VehicleType = "Van",
            MaxPassengers = 8,
            IncludesMeetAndGreet = true,
            TransferDurationMinutes = 90
        });

        var item = await _db.InventoryItems.SingleAsync(x => x.Name == "Lodge Transfer");
        var dto = await _service.GetAsync(item.Id);

        Assert.NotNull(dto);
        Assert.Equal("Kruger Gate", dto!.PickupLocation);
        Assert.Equal("Safari Lodge", dto.DropoffLocation);
        Assert.Equal("Van", dto.VehicleType);
        Assert.Equal(8, dto.MaxPassengers);
        Assert.True(dto.IncludesMeetAndGreet);
        Assert.Equal(90, dto.TransferDurationMinutes);
    }
}
