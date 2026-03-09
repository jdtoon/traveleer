using Microsoft.EntityFrameworkCore;
using saas.Data;
using saas.Data.Tenant;
using saas.Modules.Bookings.DTOs;
using saas.Modules.Bookings.Entities;
using saas.Modules.Inventory.Entities;

namespace saas.Modules.Bookings.Services;

public interface IBookingService
{
    Task<PaginatedList<BookingListItemDto>> GetListAsync(string? status = null, string? search = null, int page = 1, int pageSize = 12);
    Task<BookingFormDto> CreateEmptyAsync();
    Task<Guid> CreateAsync(BookingFormDto dto);
    Task<BookingDetailsDto?> GetDetailsAsync(Guid id);
    Task<BookingItemFormDto> CreateEmptyItemAsync(Guid bookingId);
    Task AddItemAsync(Guid bookingId, BookingItemFormDto dto);
    Task UpdateItemStatusAsync(Guid bookingId, Guid itemId, SupplierStatus newStatus);
}

public class BookingService : IBookingService
{
    private readonly TenantDbContext _db;

    public BookingService(TenantDbContext db)
    {
        _db = db;
    }

    public async Task<PaginatedList<BookingListItemDto>> GetListAsync(string? status = null, string? search = null, int page = 1, int pageSize = 12)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 6, 48);

        var query = _db.Bookings
            .AsNoTracking()
            .Include(x => x.Client)
            .Include(x => x.Items)
            .AsQueryable();

        if (TryParseStatus(status, out var parsedStatus))
        {
            query = query.Where(x => x.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x =>
                x.BookingRef.ToLower().Contains(term) ||
                (x.Client != null && x.Client.Name.ToLower().Contains(term)) ||
                (x.ClientReference != null && x.ClientReference.ToLower().Contains(term)) ||
                (x.LeadGuestName != null && x.LeadGuestName.ToLower().Contains(term)));
        }

        var projected = query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new BookingListItemDto
            {
                Id = x.Id,
                BookingRef = x.BookingRef,
                ClientName = x.Client != null ? x.Client.Name : "Unknown client",
                Status = x.Status,
                TravelStartDate = x.TravelStartDate,
                TravelEndDate = x.TravelEndDate,
                Pax = x.Pax,
                ItemCount = x.Items.Count,
                TotalSelling = x.TotalSelling,
                SellingCurrencyCode = x.SellingCurrencyCode,
                CreatedAt = x.CreatedAt
            });

        return await PaginatedList<BookingListItemDto>.CreateAsync(projected, page, pageSize);
    }

    public async Task<BookingFormDto> CreateEmptyAsync()
    {
        return new BookingFormDto
        {
            ClientOptions = await GetClientOptionsAsync(),
            CurrencyOptions = await GetCurrencyOptionsAsync()
        };
    }

    public async Task<Guid> CreateAsync(BookingFormDto dto)
    {
        var client = await _db.Clients
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == dto.ClientId)
            ?? throw new InvalidOperationException("Client was not found.");

        var currency = await ResolveCurrencyAsync(dto.SellingCurrencyCode);
        var booking = new Booking
        {
            BookingRef = await GenerateNextReferenceAsync(),
            ClientId = client.Id,
            ClientReference = Normalize(dto.ClientReference),
            TravelStartDate = dto.TravelStartDate,
            TravelEndDate = dto.TravelEndDate,
            Pax = dto.Pax,
            LeadGuestName = Normalize(dto.LeadGuestName),
            LeadGuestNationality = Normalize(dto.LeadGuestNationality),
            SellingCurrencyCode = currency,
            CostCurrencyCode = currency,
            InternalNotes = Normalize(dto.InternalNotes),
            SpecialRequests = Normalize(dto.SpecialRequests),
            Status = BookingStatus.Provisional
        };

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();
        return booking.Id;
    }

    public async Task<BookingDetailsDto?> GetDetailsAsync(Guid id)
    {
        return await _db.Bookings
            .AsNoTracking()
            .Include(x => x.Client)
            .Include(x => x.Items)
                .ThenInclude(x => x.Supplier)
            .Where(x => x.Id == id)
            .Select(x => new BookingDetailsDto
            {
                Id = x.Id,
                BookingRef = x.BookingRef,
                Status = x.Status,
                ClientId = x.ClientId,
                ClientName = x.Client != null ? x.Client.Name : "Unknown client",
                ClientReference = x.ClientReference,
                TravelStartDate = x.TravelStartDate,
                TravelEndDate = x.TravelEndDate,
                Pax = x.Pax,
                LeadGuestName = x.LeadGuestName,
                LeadGuestNationality = x.LeadGuestNationality,
                CostCurrencyCode = x.CostCurrencyCode,
                SellingCurrencyCode = x.SellingCurrencyCode,
                TotalCost = x.TotalCost,
                TotalSelling = x.TotalSelling,
                TotalProfit = x.TotalProfit,
                InternalNotes = x.InternalNotes,
                SpecialRequests = x.SpecialRequests,
                CreatedAt = x.CreatedAt,
                ConfirmedAt = x.ConfirmedAt,
                Items = x.Items
                    .OrderBy(item => item.ServiceDate)
                    .ThenBy(item => item.ServiceName)
                    .Select(item => new BookingItemListItemDto
                    {
                        Id = item.Id,
                        ServiceName = item.ServiceName,
                        ServiceKind = item.ServiceKind,
                        Description = item.Description,
                        ServiceDate = item.ServiceDate,
                        EndDate = item.EndDate,
                        Nights = item.Nights,
                        CostPrice = item.CostPrice,
                        SellingPrice = item.SellingPrice,
                        CostCurrencyCode = item.CostCurrencyCode,
                        SellingCurrencyCode = item.SellingCurrencyCode,
                        Quantity = item.Quantity,
                        Pax = item.Pax,
                        SupplierStatus = item.SupplierStatus,
                        SupplierName = item.Supplier != null ? item.Supplier.Name : null,
                        RequestedAt = item.RequestedAt,
                        ConfirmedAt = item.ConfirmedAt,
                        SupplierReference = item.SupplierReference,
                        SupplierNotes = item.SupplierNotes
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync();
    }

    public async Task<BookingItemFormDto> CreateEmptyItemAsync(Guid bookingId)
    {
        _ = await _db.Bookings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == bookingId)
            ?? throw new InvalidOperationException("Booking was not found.");

        return new BookingItemFormDto
        {
            BookingId = bookingId,
            InventoryOptions = await GetInventoryOptionsAsync()
        };
    }

    public async Task AddItemAsync(Guid bookingId, BookingItemFormDto dto)
    {
        var booking = await _db.Bookings
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == bookingId)
            ?? throw new InvalidOperationException("Booking was not found.");

        var inventoryItem = await _db.InventoryItems
            .AsNoTracking()
            .Include(x => x.Supplier)
            .FirstOrDefaultAsync(x => x.Id == dto.InventoryItemId)
            ?? throw new InvalidOperationException("Inventory item was not found.");

        var nights = CalculateNights(inventoryItem.Kind, dto.ServiceDate, dto.EndDate);
        var item = new BookingItem
        {
            BookingId = booking.Id,
            InventoryItemId = inventoryItem.Id,
            SupplierId = inventoryItem.SupplierId,
            ServiceName = inventoryItem.Name,
            ServiceKind = inventoryItem.Kind,
            Description = Normalize(inventoryItem.Description),
            ServiceDate = dto.ServiceDate,
            EndDate = inventoryItem.Kind == InventoryItemKind.Hotel ? dto.EndDate : null,
            Nights = nights,
            CostPrice = inventoryItem.BaseCost,
            SellingPrice = dto.SellingPrice,
            CostCurrencyCode = booking.CostCurrencyCode,
            SellingCurrencyCode = booking.SellingCurrencyCode,
            Quantity = dto.Quantity,
            Pax = dto.Pax,
            SupplierReference = Normalize(dto.SupplierReference),
            SupplierNotes = Normalize(dto.SupplierNotes)
        };

        booking.Items.Add(item);
        _db.Entry(item).State = EntityState.Added;

        RecalculateTotals(booking);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateItemStatusAsync(Guid bookingId, Guid itemId, SupplierStatus newStatus)
    {
        var booking = await _db.Bookings
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == bookingId)
            ?? throw new InvalidOperationException("Booking was not found.");

        var item = booking.Items.FirstOrDefault(x => x.Id == itemId)
            ?? throw new InvalidOperationException("Booking item was not found.");

        item.SupplierStatus = newStatus;
        if (newStatus == SupplierStatus.Requested)
        {
            item.RequestedAt ??= DateTime.UtcNow;
        }
        else if (newStatus == SupplierStatus.Confirmed)
        {
            item.ConfirmedAt ??= DateTime.UtcNow;
        }
        else if (newStatus == SupplierStatus.Declined)
        {
            item.ConfirmedAt = null;
        }

        if (booking.Items.Count > 0 && booking.Items.All(x => x.SupplierStatus == SupplierStatus.Confirmed))
        {
            booking.Status = BookingStatus.Confirmed;
            booking.ConfirmedAt ??= DateTime.UtcNow;
        }
        else if (booking.Status == BookingStatus.Confirmed)
        {
            booking.Status = BookingStatus.Provisional;
            booking.ConfirmedAt = null;
        }

        RecalculateTotals(booking);
        await _db.SaveChangesAsync();
    }

    private async Task<string> GenerateNextReferenceAsync()
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"BK-{year}-";
        var currentRefs = await _db.Bookings
            .Where(x => x.BookingRef.StartsWith(prefix))
            .Select(x => x.BookingRef)
            .ToListAsync();

        var max = 0;
        foreach (var currentRef in currentRefs)
        {
            var suffix = currentRef[prefix.Length..];
            if (int.TryParse(suffix, out var parsed) && parsed > max)
            {
                max = parsed;
            }
        }

        return $"{prefix}{max + 1:0000}";
    }

    private async Task<List<BookingOptionDto>> GetClientOptionsAsync()
    {
        return await _db.Clients
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new BookingOptionDto { Id = x.Id, Label = x.Name })
            .ToListAsync();
    }

    private async Task<List<BookingOptionDto>> GetInventoryOptionsAsync()
    {
        return await _db.InventoryItems
            .AsNoTracking()
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.Name)
            .Select(x => new BookingOptionDto
            {
                Id = x.Id,
                Label = $"{x.Name} ({x.Kind})"
            })
            .ToListAsync();
    }

    private async Task<List<string>> GetCurrencyOptionsAsync()
    {
        var currencies = await _db.Currencies
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.IsBaseCurrency)
            .ThenBy(x => x.Code)
            .Select(x => x.Code)
            .ToListAsync();

        if (currencies.Count == 0)
        {
            currencies.Add("USD");
        }

        return currencies;
    }

    private async Task<string> ResolveCurrencyAsync(string? requestedCurrency)
    {
        if (!string.IsNullOrWhiteSpace(requestedCurrency))
        {
            var match = await _db.Currencies
                .AsNoTracking()
                .Where(x => x.IsActive && x.Code == requestedCurrency.Trim().ToUpper())
                .Select(x => x.Code)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return await _db.Currencies
            .AsNoTracking()
            .Where(x => x.IsBaseCurrency)
            .Select(x => x.Code)
            .FirstOrDefaultAsync() ?? "USD";
    }

    private static int? CalculateNights(InventoryItemKind kind, DateOnly? serviceDate, DateOnly? endDate)
    {
        if (kind != InventoryItemKind.Hotel || !serviceDate.HasValue || !endDate.HasValue)
        {
            return null;
        }

        var diff = endDate.Value.DayNumber - serviceDate.Value.DayNumber;
        return Math.Max(0, diff);
    }

    private static void RecalculateTotals(Booking booking)
    {
        booking.TotalCost = booking.Items.Sum(x => x.CostPrice * x.Quantity);
        booking.TotalSelling = booking.Items.Sum(x => x.SellingPrice * x.Quantity);
        booking.TotalProfit = booking.TotalSelling - booking.TotalCost;
    }

    private static bool TryParseStatus(string? status, out BookingStatus parsedStatus)
        => Enum.TryParse(status, true, out parsedStatus);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
