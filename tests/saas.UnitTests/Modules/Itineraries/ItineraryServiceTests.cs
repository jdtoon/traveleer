using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Itineraries.DTOs;
using saas.Modules.Itineraries.Entities;
using saas.Modules.Itineraries.Services;
using Xunit;

namespace saas.Tests.Modules.Itineraries;

public class ItineraryServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TenantDbContext _db = null!;
    private ItineraryService _service = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TenantDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _db.Set<Itinerary>().Add(new Itinerary
        {
            Title = "Safari Adventure",
            Status = ItineraryStatus.Draft,
            TravelStartDate = new DateOnly(2025, 6, 1),
            TravelEndDate = new DateOnly(2025, 6, 7),
            Notes = "Internal notes",
            PublicNotes = "Welcome to your trip!",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        _service = new ItineraryService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ========== LIST ==========

    [Fact]
    public async Task GetListAsync_ReturnsOrderedByCreatedAtDesc()
    {
        _db.Set<Itinerary>().Add(new Itinerary
        {
            Title = "Beach Getaway",
            Status = ItineraryStatus.Draft,
            CreatedAt = DateTime.UtcNow.AddHours(1)
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetListAsync();

        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Beach Getaway", result.Items[0].Title);
        Assert.Equal("Safari Adventure", result.Items[1].Title);
    }

    [Fact]
    public async Task GetListAsync_WithStatusFilter_ReturnsOnlyMatchingStatus()
    {
        _db.Set<Itinerary>().Add(new Itinerary
        {
            Title = "Published Trip",
            Status = ItineraryStatus.Published,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var drafts = await _service.GetListAsync(status: "Draft");
        Assert.Single(drafts.Items);
        Assert.Equal("Safari Adventure", drafts.Items[0].Title);

        var published = await _service.GetListAsync(status: "Published");
        Assert.Single(published.Items);
        Assert.Equal("Published Trip", published.Items[0].Title);
    }

    [Fact]
    public async Task GetListAsync_WithSearch_FiltersByTitle()
    {
        _db.Set<Itinerary>().Add(new Itinerary
        {
            Title = "Mountain Trek",
            Status = ItineraryStatus.Draft,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetListAsync(search: "safari");
        Assert.Single(result.Items);
        Assert.Equal("Safari Adventure", result.Items[0].Title);
    }

    [Fact]
    public async Task GetListAsync_ReturnsDayCount()
    {
        var itinerary = await _db.Set<Itinerary>().FirstAsync();
        _db.Set<ItineraryDay>().AddRange(
            new ItineraryDay { ItineraryId = itinerary.Id, DayNumber = 1, Title = "Day 1", SortOrder = 10, CreatedAt = DateTime.UtcNow },
            new ItineraryDay { ItineraryId = itinerary.Id, DayNumber = 2, Title = "Day 2", SortOrder = 20, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _service.GetListAsync();
        Assert.Equal(2, result.Items[0].DayCount);
    }

    // ========== CREATE ==========

    [Fact]
    public async Task CreateAsync_TrimsFieldsAndReturnsGuid()
    {
        var id = await _service.CreateAsync(new ItineraryFormDto
        {
            Title = "  City Tour  ",
            Notes = "  internal  ",
            PublicNotes = "  public  ",
            TravelStartDate = new DateOnly(2025, 7, 1)
        });

        Assert.NotEqual(Guid.Empty, id);
        var entity = await _db.Set<Itinerary>().FindAsync(id);
        Assert.NotNull(entity);
        Assert.Equal("City Tour", entity!.Title);
        Assert.Equal("internal", entity.Notes);
        Assert.Equal("public", entity.PublicNotes);
        Assert.Equal(ItineraryStatus.Draft, entity.Status);
    }

    [Fact]
    public async Task CreateAsync_NullableFieldsSetToNullWhenWhitespace()
    {
        var id = await _service.CreateAsync(new ItineraryFormDto
        {
            Title = "Minimal Itinerary",
            Notes = "  ",
            PublicNotes = ""
        });

        var entity = await _db.Set<Itinerary>().FindAsync(id);
        Assert.Null(entity!.Notes);
        Assert.Null(entity.PublicNotes);
    }

    // ========== UPDATE ==========

    [Fact]
    public async Task UpdateAsync_UpdatesAllFields()
    {
        var itinerary = await _db.Set<Itinerary>().FirstAsync();

        await _service.UpdateAsync(itinerary.Id, new ItineraryFormDto
        {
            Title = "Updated Safari",
            Notes = "Updated notes",
            TravelStartDate = new DateOnly(2025, 8, 1),
            TravelEndDate = new DateOnly(2025, 8, 10)
        });

        var updated = await _db.Set<Itinerary>().AsNoTracking().FirstAsync(i => i.Id == itinerary.Id);
        Assert.Equal("Updated Safari", updated.Title);
        Assert.Equal("Updated notes", updated.Notes);
        Assert.Equal(new DateOnly(2025, 8, 1), updated.TravelStartDate);
        Assert.Equal(new DateOnly(2025, 8, 10), updated.TravelEndDate);
    }

    // ========== DELETE ==========

    [Fact]
    public async Task DeleteAsync_CascadesRemovalToDaysAndItems()
    {
        var itinerary = await _db.Set<Itinerary>().FirstAsync();
        var day = new ItineraryDay { ItineraryId = itinerary.Id, DayNumber = 1, Title = "Day 1", SortOrder = 10, CreatedAt = DateTime.UtcNow };
        _db.Set<ItineraryDay>().Add(day);
        await _db.SaveChangesAsync();

        _db.Set<ItineraryItem>().Add(new ItineraryItem { ItineraryDayId = day.Id, Title = "Game Drive", SortOrder = 10, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        await _service.DeleteAsync(itinerary.Id);

        Assert.False(await _db.Set<Itinerary>().AnyAsync(i => i.Id == itinerary.Id));
        Assert.False(await _db.Set<ItineraryDay>().AnyAsync(d => d.ItineraryId == itinerary.Id));
        Assert.False(await _db.Set<ItineraryItem>().AnyAsync());
    }

    // ========== DETAILS ==========

    [Fact]
    public async Task GetDetailsAsync_ReturnsNestedDaysAndItems()
    {
        var itinerary = await _db.Set<Itinerary>().FirstAsync();
        var day = new ItineraryDay { ItineraryId = itinerary.Id, DayNumber = 1, Title = "Arrival Day", SortOrder = 10, CreatedAt = DateTime.UtcNow };
        _db.Set<ItineraryDay>().Add(day);
        await _db.SaveChangesAsync();

        _db.Set<ItineraryItem>().AddRange(
            new ItineraryItem { ItineraryDayId = day.Id, Title = "Airport Transfer", SortOrder = 10, CreatedAt = DateTime.UtcNow },
            new ItineraryItem { ItineraryDayId = day.Id, Title = "Hotel Check-in", SortOrder = 20, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var details = await _service.GetDetailsAsync(itinerary.Id);

        Assert.NotNull(details);
        Assert.Equal("Safari Adventure", details!.Title);
        Assert.Single(details.Days);
        Assert.Equal("Arrival Day", details.Days[0].Title);
        Assert.Equal(2, details.Days[0].Items.Count);
        Assert.Equal("Airport Transfer", details.Days[0].Items[0].Title);
    }

    [Fact]
    public async Task GetDetailsAsync_WhenNotFound_ReturnsNull()
    {
        var result = await _service.GetDetailsAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    // ========== PUBLISH / ARCHIVE ==========

    [Fact]
    public async Task PublishAsync_SetsStatusAndPublishedAt()
    {
        var itinerary = await _db.Set<Itinerary>().FirstAsync();
        Assert.Equal(ItineraryStatus.Draft, itinerary.Status);
        Assert.Null(itinerary.PublishedAt);

        await _service.PublishAsync(itinerary.Id);

        var updated = await _db.Set<Itinerary>().AsNoTracking().FirstAsync(i => i.Id == itinerary.Id);
        Assert.Equal(ItineraryStatus.Published, updated.Status);
        Assert.NotNull(updated.PublishedAt);
    }

    [Fact]
    public async Task ArchiveAsync_SetsStatusToArchived()
    {
        var itinerary = await _db.Set<Itinerary>().FirstAsync();

        await _service.ArchiveAsync(itinerary.Id);

        var updated = await _db.Set<Itinerary>().AsNoTracking().FirstAsync(i => i.Id == itinerary.Id);
        Assert.Equal(ItineraryStatus.Archived, updated.Status);
    }

    // ========== SHARE TOKEN ==========

    [Fact]
    public async Task GenerateShareTokenAsync_CreatesHexToken()
    {
        var itinerary = await _db.Set<Itinerary>().FirstAsync();
        Assert.Null(itinerary.ShareToken);

        var token = await _service.GenerateShareTokenAsync(itinerary.Id);

        Assert.NotEmpty(token);
        Assert.Equal(64, token.Length); // 32 bytes = 64 hex chars
        var updated = await _db.Set<Itinerary>().AsNoTracking().FirstAsync(i => i.Id == itinerary.Id);
        Assert.Equal(token, updated.ShareToken);
        Assert.NotNull(updated.SharedAt);
    }

    [Fact]
    public async Task GenerateShareTokenAsync_ReturnsExistingToken()
    {
        var itinerary = await _db.Set<Itinerary>().FirstAsync();
        var token1 = await _service.GenerateShareTokenAsync(itinerary.Id);
        var token2 = await _service.GenerateShareTokenAsync(itinerary.Id);

        Assert.Equal(token1, token2);
    }

    [Fact]
    public async Task GetByShareTokenAsync_OnlyReturnsPublished()
    {
        var itinerary = await _db.Set<Itinerary>().FirstAsync();
        var token = await _service.GenerateShareTokenAsync(itinerary.Id);

        // Draft — should not return
        var result = await _service.GetByShareTokenAsync(token);
        Assert.Null(result);

        // Publish it
        await _service.PublishAsync(itinerary.Id);
        result = await _service.GetByShareTokenAsync(token);
        Assert.NotNull(result);
        Assert.Equal("Safari Adventure", result!.Title);
    }

    // ========== DAYS ==========

    [Fact]
    public async Task CreateEmptyDayAsync_AutoIncrementsDayNumberAndSortOrder()
    {
        var itinerary = await _db.Set<Itinerary>().FirstAsync();

        var first = await _service.CreateEmptyDayAsync(itinerary.Id);
        Assert.Equal(1, first.DayNumber);
        Assert.Equal(10, first.SortOrder);
        Assert.Equal("Day 1", first.Title);
        Assert.Equal(new DateOnly(2025, 6, 1), first.Date);

        // Create and save it, then check the next one
        await _service.CreateDayAsync(first);

        var second = await _service.CreateEmptyDayAsync(itinerary.Id);
        Assert.Equal(2, second.DayNumber);
        Assert.Equal(20, second.SortOrder);
        Assert.Equal(new DateOnly(2025, 6, 2), second.Date);
    }

    [Fact]
    public async Task CreateDayAsync_PersistsFields()
    {
        var itinerary = await _db.Set<Itinerary>().FirstAsync();

        await _service.CreateDayAsync(new ItineraryDayDto
        {
            ItineraryId = itinerary.Id,
            DayNumber = 1,
            Title = "  Arrival Day  ",
            Description = "  Check-in  ",
            Date = new DateOnly(2025, 6, 1),
            SortOrder = 10
        });

        var day = await _db.Set<ItineraryDay>().FirstAsync();
        Assert.Equal("Arrival Day", day.Title);
        Assert.Equal("Check-in", day.Description);
        Assert.Equal(1, day.DayNumber);
    }

    [Fact]
    public async Task UpdateDayAsync_UpdatesAllFields()
    {
        var itinerary = await _db.Set<Itinerary>().FirstAsync();
        _db.Set<ItineraryDay>().Add(new ItineraryDay
        {
            ItineraryId = itinerary.Id,
            DayNumber = 1,
            Title = "Day 1",
            SortOrder = 10,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var day = await _db.Set<ItineraryDay>().FirstAsync();
        await _service.UpdateDayAsync(day.Id, new ItineraryDayDto
        {
            ItineraryId = itinerary.Id,
            DayNumber = 2,
            Title = "Updated Day",
            Description = "New desc",
            SortOrder = 20
        });

        var updated = await _db.Set<ItineraryDay>().AsNoTracking().FirstAsync(d => d.Id == day.Id);
        Assert.Equal("Updated Day", updated.Title);
        Assert.Equal(2, updated.DayNumber);
        Assert.Equal("New desc", updated.Description);
    }

    [Fact]
    public async Task DeleteDayAsync_CascadesToItems()
    {
        var itinerary = await _db.Set<Itinerary>().FirstAsync();
        var day = new ItineraryDay { ItineraryId = itinerary.Id, DayNumber = 1, Title = "Day 1", SortOrder = 10, CreatedAt = DateTime.UtcNow };
        _db.Set<ItineraryDay>().Add(day);
        await _db.SaveChangesAsync();

        _db.Set<ItineraryItem>().Add(new ItineraryItem { ItineraryDayId = day.Id, Title = "Activity", SortOrder = 10, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        await _service.DeleteDayAsync(day.Id);

        Assert.False(await _db.Set<ItineraryDay>().AnyAsync(d => d.Id == day.Id));
        Assert.False(await _db.Set<ItineraryItem>().AnyAsync());
    }

    // ========== ITEMS ==========

    [Fact]
    public async Task CreateEmptyItemAsync_AutoIncrementsSortOrder()
    {
        var itinerary = await _db.Set<Itinerary>().FirstAsync();
        var day = new ItineraryDay { ItineraryId = itinerary.Id, DayNumber = 1, Title = "Day 1", SortOrder = 10, CreatedAt = DateTime.UtcNow };
        _db.Set<ItineraryDay>().Add(day);
        await _db.SaveChangesAsync();

        var empty = await _service.CreateEmptyItemAsync(day.Id);
        Assert.Equal(day.Id, empty.ItineraryDayId);
        Assert.Equal(10, empty.SortOrder);
    }

    [Fact]
    public async Task CreateItemAsync_PersistsAllFields()
    {
        var itinerary = await _db.Set<Itinerary>().FirstAsync();
        var day = new ItineraryDay { ItineraryId = itinerary.Id, DayNumber = 1, Title = "Day 1", SortOrder = 10, CreatedAt = DateTime.UtcNow };
        _db.Set<ItineraryDay>().Add(day);
        await _db.SaveChangesAsync();

        await _service.CreateItemAsync(new ItineraryItemDto
        {
            ItineraryDayId = day.Id,
            Title = "  Game Drive  ",
            Description = "  Morning safari  ",
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(10, 0),
            ImageUrl = "  https://example.com/safari.jpg  ",
            SortOrder = 10
        });

        var item = await _db.Set<ItineraryItem>().FirstAsync();
        Assert.Equal("Game Drive", item.Title);
        Assert.Equal("Morning safari", item.Description);
        Assert.Equal(new TimeOnly(6, 0), item.StartTime);
        Assert.Equal(new TimeOnly(10, 0), item.EndTime);
        Assert.Equal("https://example.com/safari.jpg", item.ImageUrl);
    }

    [Fact]
    public async Task UpdateItemAsync_UpdatesAllFields()
    {
        var itinerary = await _db.Set<Itinerary>().FirstAsync();
        var day = new ItineraryDay { ItineraryId = itinerary.Id, DayNumber = 1, Title = "Day 1", SortOrder = 10, CreatedAt = DateTime.UtcNow };
        _db.Set<ItineraryDay>().Add(day);
        await _db.SaveChangesAsync();

        _db.Set<ItineraryItem>().Add(new ItineraryItem
        {
            ItineraryDayId = day.Id,
            Title = "Original",
            SortOrder = 10,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var item = await _db.Set<ItineraryItem>().FirstAsync();
        await _service.UpdateItemAsync(item.Id, new ItineraryItemDto
        {
            ItineraryDayId = day.Id,
            Title = "Updated Activity",
            Description = "Updated desc",
            StartTime = new TimeOnly(14, 0),
            SortOrder = 20
        });

        var updated = await _db.Set<ItineraryItem>().AsNoTracking().FirstAsync(i => i.Id == item.Id);
        Assert.Equal("Updated Activity", updated.Title);
        Assert.Equal("Updated desc", updated.Description);
        Assert.Equal(new TimeOnly(14, 0), updated.StartTime);
        Assert.Equal(20, updated.SortOrder);
    }

    [Fact]
    public async Task DeleteItemAsync_RemovesItem()
    {
        var itinerary = await _db.Set<Itinerary>().FirstAsync();
        var day = new ItineraryDay { ItineraryId = itinerary.Id, DayNumber = 1, Title = "Day 1", SortOrder = 10, CreatedAt = DateTime.UtcNow };
        _db.Set<ItineraryDay>().Add(day);
        await _db.SaveChangesAsync();

        _db.Set<ItineraryItem>().Add(new ItineraryItem { ItineraryDayId = day.Id, Title = "To Remove", SortOrder = 10, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var item = await _db.Set<ItineraryItem>().FirstAsync();
        await _service.DeleteItemAsync(item.Id);

        Assert.False(await _db.Set<ItineraryItem>().AnyAsync());
    }
}
