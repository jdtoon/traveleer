using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Itineraries.DTOs;
using saas.Modules.Itineraries.Entities;

namespace saas.Modules.Itineraries.Services;

public interface IItineraryService
{
    Task<List<ItineraryListItemDto>> GetListAsync(string? status = null, string? search = null);
    Task<ItineraryFormDto> CreateEmptyAsync();
    Task<ItineraryFormDto?> GetFormAsync(Guid id);
    Task<Guid> CreateAsync(ItineraryFormDto dto);
    Task UpdateAsync(Guid id, ItineraryFormDto dto);
    Task DeleteAsync(Guid id);
    Task<ItineraryDetailsDto?> GetDetailsAsync(Guid id);
    Task PublishAsync(Guid id);
    Task ArchiveAsync(Guid id);
    Task<string> GenerateShareTokenAsync(Guid id);
    Task<ItineraryDetailsDto?> GetByShareTokenAsync(string token);

    // Days
    Task<List<ItineraryDayDto>> GetDaysAsync(Guid itineraryId);
    Task<ItineraryDayDto> CreateEmptyDayAsync(Guid itineraryId);
    Task<ItineraryDayDto?> GetDayFormAsync(Guid dayId);
    Task CreateDayAsync(ItineraryDayDto dto);
    Task UpdateDayAsync(Guid dayId, ItineraryDayDto dto);
    Task DeleteDayAsync(Guid dayId);

    // Items
    Task<ItineraryItemDto> CreateEmptyItemAsync(Guid dayId);
    Task<ItineraryItemDto?> GetItemFormAsync(Guid itemId);
    Task CreateItemAsync(ItineraryItemDto dto);
    Task UpdateItemAsync(Guid itemId, ItineraryItemDto dto);
    Task DeleteItemAsync(Guid itemId);
}

public class ItineraryService : IItineraryService
{
    private readonly TenantDbContext _db;

    public ItineraryService(TenantDbContext db)
    {
        _db = db;
    }

    public async Task<List<ItineraryListItemDto>> GetListAsync(string? status = null, string? search = null)
    {
        var query = _db.Set<Itinerary>().AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ItineraryStatus>(status, true, out var parsedStatus))
            query = query.Where(i => i.Status == parsedStatus);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(i => i.Title.ToLower().Contains(term));
        }

        return await query
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new ItineraryListItemDto
            {
                Id = i.Id,
                Title = i.Title,
                ClientName = i.Client != null ? i.Client.Name : null,
                BookingRef = i.Booking != null ? i.Booking.BookingRef : null,
                Status = i.Status,
                TravelStartDate = i.TravelStartDate,
                TravelEndDate = i.TravelEndDate,
                DayCount = i.Days.Count
            })
            .ToListAsync();
    }

    public async Task<ItineraryFormDto> CreateEmptyAsync()
    {
        return new ItineraryFormDto
        {
            ClientOptions = await GetClientOptionsAsync(),
            BookingOptions = await GetBookingOptionsAsync()
        };
    }

    public async Task<ItineraryFormDto?> GetFormAsync(Guid id)
    {
        var entity = await _db.Set<Itinerary>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        if (entity is null) return null;

        return new ItineraryFormDto
        {
            Id = entity.Id,
            Title = entity.Title,
            BookingId = entity.BookingId,
            ClientId = entity.ClientId,
            TravelStartDate = entity.TravelStartDate,
            TravelEndDate = entity.TravelEndDate,
            Notes = entity.Notes,
            PublicNotes = entity.PublicNotes,
            ClientOptions = await GetClientOptionsAsync(),
            BookingOptions = await GetBookingOptionsAsync()
        };
    }

    public async Task<Guid> CreateAsync(ItineraryFormDto dto)
    {
        var entity = new Itinerary
        {
            Title = dto.Title.Trim(),
            BookingId = dto.BookingId,
            ClientId = dto.ClientId,
            TravelStartDate = dto.TravelStartDate,
            TravelEndDate = dto.TravelEndDate,
            Notes = Normalize(dto.Notes),
            PublicNotes = Normalize(dto.PublicNotes)
        };
        _db.Set<Itinerary>().Add(entity);
        await _db.SaveChangesAsync();
        return entity.Id;
    }

    public async Task UpdateAsync(Guid id, ItineraryFormDto dto)
    {
        var entity = await _db.Set<Itinerary>().FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new InvalidOperationException($"Itinerary {id} was not found.");

        entity.Title = dto.Title.Trim();
        entity.BookingId = dto.BookingId;
        entity.ClientId = dto.ClientId;
        entity.TravelStartDate = dto.TravelStartDate;
        entity.TravelEndDate = dto.TravelEndDate;
        entity.Notes = Normalize(dto.Notes);
        entity.PublicNotes = Normalize(dto.PublicNotes);

        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var entity = await _db.Set<Itinerary>()
            .Include(i => i.Days).ThenInclude(d => d.Items)
            .FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new InvalidOperationException($"Itinerary {id} was not found.");

        foreach (var day in entity.Days)
            _db.Set<ItineraryItem>().RemoveRange(day.Items);
        _db.Set<ItineraryDay>().RemoveRange(entity.Days);
        _db.Set<Itinerary>().Remove(entity);
        await _db.SaveChangesAsync();
    }

    public async Task<ItineraryDetailsDto?> GetDetailsAsync(Guid id)
    {
        var entity = await _db.Set<Itinerary>().AsNoTracking()
            .Include(i => i.Client)
            .Include(i => i.Booking)
            .Include(i => i.Days.OrderBy(d => d.SortOrder))
                .ThenInclude(d => d.Items.OrderBy(item => item.SortOrder))
            .FirstOrDefaultAsync(i => i.Id == id);

        if (entity is null) return null;

        return new ItineraryDetailsDto
        {
            Id = entity.Id,
            Title = entity.Title,
            ClientName = entity.Client?.Name,
            ClientId = entity.ClientId,
            BookingRef = entity.Booking?.BookingRef,
            BookingId = entity.BookingId,
            Status = entity.Status,
            TravelStartDate = entity.TravelStartDate,
            TravelEndDate = entity.TravelEndDate,
            CoverImageUrl = entity.CoverImageUrl,
            Notes = entity.Notes,
            PublicNotes = entity.PublicNotes,
            ShareToken = entity.ShareToken,
            SharedAt = entity.SharedAt,
            PublishedAt = entity.PublishedAt,
            CreatedAt = entity.CreatedAt,
            Days = entity.Days.Select(d => new ItineraryDayDto
            {
                Id = d.Id,
                ItineraryId = d.ItineraryId,
                DayNumber = d.DayNumber,
                Date = d.Date,
                Title = d.Title,
                Description = d.Description,
                SortOrder = d.SortOrder,
                Items = d.Items.Select(item => new ItineraryItemDto
                {
                    Id = item.Id,
                    ItineraryDayId = item.ItineraryDayId,
                    Title = item.Title,
                    Description = item.Description,
                    InventoryItemId = item.InventoryItemId,
                    BookingItemId = item.BookingItemId,
                    StartTime = item.StartTime,
                    EndTime = item.EndTime,
                    ImageUrl = item.ImageUrl,
                    SortOrder = item.SortOrder,
                    ItemKind = item.ItemKind
                }).ToList()
            }).ToList()
        };
    }

    public async Task PublishAsync(Guid id)
    {
        var entity = await _db.Set<Itinerary>().FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new InvalidOperationException($"Itinerary {id} was not found.");

        entity.Status = ItineraryStatus.Published;
        entity.PublishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task ArchiveAsync(Guid id)
    {
        var entity = await _db.Set<Itinerary>().FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new InvalidOperationException($"Itinerary {id} was not found.");

        entity.Status = ItineraryStatus.Archived;
        await _db.SaveChangesAsync();
    }

    public async Task<string> GenerateShareTokenAsync(Guid id)
    {
        var entity = await _db.Set<Itinerary>().FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new InvalidOperationException($"Itinerary {id} was not found.");

        if (string.IsNullOrEmpty(entity.ShareToken))
        {
            entity.ShareToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            entity.SharedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return entity.ShareToken;
    }

    public async Task<ItineraryDetailsDto?> GetByShareTokenAsync(string token)
    {
        var entity = await _db.Set<Itinerary>().AsNoTracking()
            .Include(i => i.Client)
            .Include(i => i.Days.OrderBy(d => d.SortOrder))
                .ThenInclude(d => d.Items.OrderBy(item => item.SortOrder))
            .FirstOrDefaultAsync(i => i.ShareToken == token && i.Status == ItineraryStatus.Published);

        if (entity is null) return null;

        return new ItineraryDetailsDto
        {
            Id = entity.Id,
            Title = entity.Title,
            ClientName = entity.Client?.Name,
            Status = entity.Status,
            TravelStartDate = entity.TravelStartDate,
            TravelEndDate = entity.TravelEndDate,
            CoverImageUrl = entity.CoverImageUrl,
            PublicNotes = entity.PublicNotes,
            Days = entity.Days.Select(d => new ItineraryDayDto
            {
                Id = d.Id,
                ItineraryId = d.ItineraryId,
                DayNumber = d.DayNumber,
                Date = d.Date,
                Title = d.Title,
                Description = d.Description,
                SortOrder = d.SortOrder,
                Items = d.Items.Select(item => new ItineraryItemDto
                {
                    Id = item.Id,
                    ItineraryDayId = item.ItineraryDayId,
                    Title = item.Title,
                    Description = item.Description,
                    StartTime = item.StartTime,
                    EndTime = item.EndTime,
                    ImageUrl = item.ImageUrl,
                    SortOrder = item.SortOrder,
                    ItemKind = item.ItemKind
                }).ToList()
            }).ToList()
        };
    }

    // ========== DAYS ==========

    public async Task<List<ItineraryDayDto>> GetDaysAsync(Guid itineraryId)
    {
        return await _db.Set<ItineraryDay>().AsNoTracking()
            .Where(d => d.ItineraryId == itineraryId)
            .OrderBy(d => d.SortOrder)
            .Select(d => new ItineraryDayDto
            {
                Id = d.Id,
                ItineraryId = d.ItineraryId,
                DayNumber = d.DayNumber,
                Date = d.Date,
                Title = d.Title,
                Description = d.Description,
                SortOrder = d.SortOrder,
                Items = d.Items.OrderBy(item => item.SortOrder).Select(item => new ItineraryItemDto
                {
                    Id = item.Id,
                    ItineraryDayId = item.ItineraryDayId,
                    Title = item.Title,
                    Description = item.Description,
                    StartTime = item.StartTime,
                    EndTime = item.EndTime,
                    ImageUrl = item.ImageUrl,
                    SortOrder = item.SortOrder,
                    ItemKind = item.ItemKind
                }).ToList()
            })
            .ToListAsync();
    }

    public async Task<ItineraryDayDto> CreateEmptyDayAsync(Guid itineraryId)
    {
        var maxDay = await _db.Set<ItineraryDay>()
            .Where(d => d.ItineraryId == itineraryId)
            .MaxAsync(d => (int?)d.DayNumber) ?? 0;

        var maxSort = await _db.Set<ItineraryDay>()
            .Where(d => d.ItineraryId == itineraryId)
            .MaxAsync(d => (int?)d.SortOrder) ?? 0;

        var itinerary = await _db.Set<Itinerary>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == itineraryId);
        var nextDay = maxDay + 1;
        DateOnly? date = itinerary?.TravelStartDate?.AddDays(nextDay - 1);

        return new ItineraryDayDto
        {
            ItineraryId = itineraryId,
            DayNumber = nextDay,
            Date = date,
            Title = $"Day {nextDay}",
            SortOrder = maxSort + 10
        };
    }

    public async Task<ItineraryDayDto?> GetDayFormAsync(Guid dayId)
    {
        return await _db.Set<ItineraryDay>().AsNoTracking()
            .Where(d => d.Id == dayId)
            .Select(d => new ItineraryDayDto
            {
                Id = d.Id,
                ItineraryId = d.ItineraryId,
                DayNumber = d.DayNumber,
                Date = d.Date,
                Title = d.Title,
                Description = d.Description,
                SortOrder = d.SortOrder
            })
            .FirstOrDefaultAsync();
    }

    public async Task CreateDayAsync(ItineraryDayDto dto)
    {
        _db.Set<ItineraryDay>().Add(new ItineraryDay
        {
            ItineraryId = dto.ItineraryId,
            DayNumber = dto.DayNumber,
            Date = dto.Date,
            Title = dto.Title.Trim(),
            Description = Normalize(dto.Description),
            SortOrder = dto.SortOrder
        });
        await _db.SaveChangesAsync();
    }

    public async Task UpdateDayAsync(Guid dayId, ItineraryDayDto dto)
    {
        var entity = await _db.Set<ItineraryDay>().FirstOrDefaultAsync(d => d.Id == dayId)
            ?? throw new InvalidOperationException($"Itinerary day {dayId} was not found.");

        entity.DayNumber = dto.DayNumber;
        entity.Date = dto.Date;
        entity.Title = dto.Title.Trim();
        entity.Description = Normalize(dto.Description);
        entity.SortOrder = dto.SortOrder;

        await _db.SaveChangesAsync();
    }

    public async Task DeleteDayAsync(Guid dayId)
    {
        var entity = await _db.Set<ItineraryDay>()
            .Include(d => d.Items)
            .FirstOrDefaultAsync(d => d.Id == dayId)
            ?? throw new InvalidOperationException($"Itinerary day {dayId} was not found.");

        _db.Set<ItineraryItem>().RemoveRange(entity.Items);
        _db.Set<ItineraryDay>().Remove(entity);
        await _db.SaveChangesAsync();
    }

    // ========== ITEMS ==========

    public async Task<ItineraryItemDto> CreateEmptyItemAsync(Guid dayId)
    {
        var maxSort = await _db.Set<ItineraryItem>()
            .Where(item => item.ItineraryDayId == dayId)
            .MaxAsync(item => (int?)item.SortOrder) ?? 0;

        return new ItineraryItemDto
        {
            ItineraryDayId = dayId,
            SortOrder = maxSort + 10,
            InventoryOptions = await GetInventoryOptionsAsync()
        };
    }

    public async Task<ItineraryItemDto?> GetItemFormAsync(Guid itemId)
    {
        var dto = await _db.Set<ItineraryItem>().AsNoTracking()
            .Where(item => item.Id == itemId)
            .Select(item => new ItineraryItemDto
            {
                Id = item.Id,
                ItineraryDayId = item.ItineraryDayId,
                Title = item.Title,
                Description = item.Description,
                InventoryItemId = item.InventoryItemId,
                BookingItemId = item.BookingItemId,
                StartTime = item.StartTime,
                EndTime = item.EndTime,
                ImageUrl = item.ImageUrl,
                SortOrder = item.SortOrder,
                ItemKind = item.ItemKind
            })
            .FirstOrDefaultAsync();

        if (dto is not null)
            dto.InventoryOptions = await GetInventoryOptionsAsync();

        return dto;
    }

    public async Task CreateItemAsync(ItineraryItemDto dto)
    {
        _db.Set<ItineraryItem>().Add(new ItineraryItem
        {
            ItineraryDayId = dto.ItineraryDayId,
            InventoryItemId = dto.InventoryItemId,
            BookingItemId = dto.BookingItemId,
            Title = dto.Title.Trim(),
            Description = Normalize(dto.Description),
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            ImageUrl = Normalize(dto.ImageUrl),
            SortOrder = dto.SortOrder,
            ItemKind = dto.ItemKind
        });
        await _db.SaveChangesAsync();
    }

    public async Task UpdateItemAsync(Guid itemId, ItineraryItemDto dto)
    {
        var entity = await _db.Set<ItineraryItem>().FirstOrDefaultAsync(item => item.Id == itemId)
            ?? throw new InvalidOperationException($"Itinerary item {itemId} was not found.");

        entity.InventoryItemId = dto.InventoryItemId;
        entity.BookingItemId = dto.BookingItemId;
        entity.Title = dto.Title.Trim();
        entity.Description = Normalize(dto.Description);
        entity.StartTime = dto.StartTime;
        entity.EndTime = dto.EndTime;
        entity.ImageUrl = Normalize(dto.ImageUrl);
        entity.SortOrder = dto.SortOrder;
        entity.ItemKind = dto.ItemKind;

        await _db.SaveChangesAsync();
    }

    public async Task DeleteItemAsync(Guid itemId)
    {
        var entity = await _db.Set<ItineraryItem>().FirstOrDefaultAsync(item => item.Id == itemId)
            ?? throw new InvalidOperationException($"Itinerary item {itemId} was not found.");
        _db.Set<ItineraryItem>().Remove(entity);
        await _db.SaveChangesAsync();
    }

    // ========== HELPERS ==========

    private async Task<List<ClientOptionDto>> GetClientOptionsAsync()
        => await _db.Clients.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new ClientOptionDto { Id = c.Id, Name = c.Name })
            .ToListAsync();

    private async Task<List<BookingOptionDto>> GetBookingOptionsAsync()
        => await _db.Bookings.AsNoTracking()
            .Include(b => b.Client)
            .OrderByDescending(b => b.CreatedAt)
            .Take(50)
            .Select(b => new BookingOptionDto
            {
                Id = b.Id,
                BookingRef = b.BookingRef,
                ClientName = b.Client != null ? b.Client.Name : null
            })
            .ToListAsync();

    private async Task<List<InventoryOptionDto>> GetInventoryOptionsAsync()
        => await _db.InventoryItems.AsNoTracking()
            .OrderBy(i => i.Name)
            .Select(i => new InventoryOptionDto { Id = i.Id, Name = i.Name, Kind = i.Kind })
            .ToListAsync();

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
