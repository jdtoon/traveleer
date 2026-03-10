using Microsoft.EntityFrameworkCore;
using saas.Data;
using saas.Data.Tenant;
using saas.Infrastructure.Services;
using saas.Modules.Branding.Entities;
using saas.Modules.Bookings.DTOs;
using saas.Modules.Bookings.Entities;
using saas.Modules.Inventory.Entities;
using saas.Modules.Quotes.Entities;
using saas.Shared;

namespace saas.Modules.Bookings.Services;

public interface IBookingService
{
    Task<PaginatedList<BookingListItemDto>> GetListAsync(string? status = null, string? search = null, int page = 1, int pageSize = 12);
    Task<BookingFormDto> CreateEmptyAsync();
    Task<Guid> CreateAsync(BookingFormDto dto);
    Task<BookingConversionResult> ConvertFromQuoteAsync(Guid quoteId);
    Task<BookingDetailsDto?> GetDetailsAsync(Guid id);
    Task<BookingItemFormDto> CreateEmptyItemAsync(Guid bookingId);
    Task AddItemAsync(Guid bookingId, BookingItemFormDto dto);
    Task<BookingItemActionResult> SendSupplierRequestAsync(Guid bookingId, Guid itemId, bool isReminder = false);
    Task<BookingItemActionResult> GenerateVoucherAsync(Guid bookingId, Guid itemId);
    Task<BookingItemActionResult> SendVoucherAsync(Guid bookingId, Guid itemId);
    Task<(byte[] PdfBytes, string FileName)?> GetVoucherPdfAsync(Guid bookingId, Guid itemId);
    Task UpdateItemStatusAsync(Guid bookingId, Guid itemId, SupplierStatus newStatus);
}

public class BookingService : IBookingService
{
    private readonly TenantDbContext _db;
    private readonly IEmailService _emailService;
    private readonly IEmailTemplateService _templateService;
    private readonly ITenantContext _tenantContext;
    private readonly IBookingVoucherDocumentService _voucherDocumentService;

    public BookingService(
        TenantDbContext db,
        IEmailService emailService,
        IEmailTemplateService templateService,
        ITenantContext tenantContext,
        IBookingVoucherDocumentService voucherDocumentService)
    {
        _db = db;
        _emailService = emailService;
        _templateService = templateService;
        _tenantContext = tenantContext;
        _voucherDocumentService = voucherDocumentService;
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
                QuoteId = x.QuoteId,
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

    public async Task<BookingConversionResult> ConvertFromQuoteAsync(Guid quoteId)
    {
        var quote = await _db.Quotes
            .Include(x => x.QuoteRateCards)
                .ThenInclude(x => x.RateCard)
                    .ThenInclude(x => x!.InventoryItem)
            .FirstOrDefaultAsync(x => x.Id == quoteId);

        if (quote is null)
        {
            return BookingConversionResult.Fail("Quote was not found.");
        }

        if (quote.Status != QuoteStatus.Accepted)
        {
            return BookingConversionResult.Fail("Only accepted quotes can be converted to bookings.");
        }

        if (!quote.ClientId.HasValue)
        {
            return BookingConversionResult.Fail("Link this quote to a saved client before converting it to a booking.");
        }

        var existing = await _db.Bookings
            .AsNoTracking()
            .Where(x => x.QuoteId == quoteId)
            .Select(x => new { x.Id, x.BookingRef })
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            return BookingConversionResult.Ok(existing.Id, existing.BookingRef, alreadyExists: true);
        }

        if (quote.QuoteRateCards.Count == 0)
        {
            return BookingConversionResult.Fail("Add at least one rate card to the quote before converting it.");
        }

        var currency = await ResolveCurrencyAsync(quote.OutputCurrencyCode);
        var booking = new Booking
        {
            QuoteId = quote.Id,
            BookingRef = await GenerateNextReferenceAsync(),
            ClientId = quote.ClientId.Value,
            ClientReference = quote.ReferenceNumber,
            TravelStartDate = quote.TravelStartDate,
            TravelEndDate = quote.TravelEndDate,
            SellingCurrencyCode = currency,
            CostCurrencyCode = currency,
            InternalNotes = Normalize(quote.InternalNotes),
            SpecialRequests = Normalize(quote.Notes),
            Status = BookingStatus.Provisional
        };

        foreach (var selectedRateCard in quote.QuoteRateCards.OrderBy(x => x.SortOrder))
        {
            var rateCard = selectedRateCard.RateCard;
            var inventoryItem = rateCard?.InventoryItem;
            var serviceKind = inventoryItem?.Kind ?? InventoryItemKind.Other;
            var costPrice = inventoryItem?.BaseCost ?? 0m;

            booking.Items.Add(new BookingItem
            {
                InventoryItemId = inventoryItem?.Id,
                SupplierId = inventoryItem?.SupplierId,
                ServiceName = inventoryItem?.Name ?? rateCard?.Name ?? "Service",
                ServiceKind = serviceKind,
                Description = Normalize(inventoryItem?.Description),
                ServiceDate = quote.TravelStartDate,
                EndDate = serviceKind == InventoryItemKind.Hotel ? quote.TravelEndDate : null,
                Nights = CalculateNights(serviceKind, quote.TravelStartDate, quote.TravelEndDate),
                CostPrice = costPrice,
                SellingPrice = decimal.Round(costPrice * (1m + (quote.MarkupPercentage / 100m)), 2),
                CostCurrencyCode = currency,
                SellingCurrencyCode = currency,
                Quantity = 1,
                Pax = 1,
                SupplierStatus = SupplierStatus.NotRequested
            });
        }

        if (booking.Items.Count == 0)
        {
            return BookingConversionResult.Fail("The selected quote services could not be converted into booking items.");
        }

        RecalculateTotals(booking);
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();
        return BookingConversionResult.Ok(booking.Id, booking.BookingRef);
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
                QuoteId = x.QuoteId,
                QuoteReferenceNumber = x.Quote != null ? x.Quote.ReferenceNumber : null,
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
                        SupplierEmail = item.Supplier != null ? item.Supplier.ContactEmail : null,
                        RequestedAt = item.RequestedAt,
                        ConfirmedAt = item.ConfirmedAt,
                        VoucherSent = item.VoucherSent,
                        VoucherSentAt = item.VoucherSentAt,
                        VoucherGenerated = item.VoucherGenerated,
                        VoucherGeneratedAt = item.VoucherGeneratedAt,
                        VoucherNumber = item.VoucherNumber,
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

    public async Task<BookingItemActionResult> SendSupplierRequestAsync(Guid bookingId, Guid itemId, bool isReminder = false)
    {
        var booking = await _db.Bookings
            .Include(x => x.Client)
            .Include(x => x.Items)
                .ThenInclude(x => x.Supplier)
            .FirstOrDefaultAsync(x => x.Id == bookingId);

        if (booking is null)
        {
            return BookingItemActionResult.Fail("Booking was not found.");
        }

        var item = booking.Items.FirstOrDefault(x => x.Id == itemId);
        if (item is null)
        {
            return BookingItemActionResult.Fail("Booking item was not found.");
        }

        if (!item.SupplierId.HasValue || item.Supplier is null)
        {
            return BookingItemActionResult.Fail("Assign a supplier before sending a supplier request.");
        }

        if (string.IsNullOrWhiteSpace(item.Supplier.ContactEmail))
        {
            return BookingItemActionResult.Fail("Add a supplier contact email before sending a supplier request.");
        }

        if (isReminder && item.SupplierStatus == SupplierStatus.NotRequested)
        {
            return BookingItemActionResult.Fail("Send the first supplier request before sending a reminder.");
        }

        if (!isReminder && item.SupplierStatus != SupplierStatus.NotRequested)
        {
            return BookingItemActionResult.Fail("This supplier request has already been sent.");
        }

        var branding = await _db.BrandingSettings.AsNoTracking()
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync() ?? new BrandingSettings();

        var subject = isReminder
            ? $"Reminder: booking request {booking.BookingRef} - {item.ServiceName}"
            : $"Booking request {booking.BookingRef} - {item.ServiceName}";

        var htmlBody = _templateService.Render("SupplierBookingRequest", BuildSupplierRequestVariables(booking, item, branding, isReminder));
        var plainTextBody = BuildSupplierRequestPlainText(booking, item, branding, isReminder);

        var result = await _emailService.SendAsync(new EmailMessage(
            To: item.Supplier.ContactEmail.Trim(),
            Subject: subject,
            HtmlBody: htmlBody,
            PlainTextBody: plainTextBody));

        if (!result.Success)
        {
            return BookingItemActionResult.Fail(result.ErrorMessage ?? "Supplier request could not be sent.");
        }

        if (!isReminder)
        {
            item.SupplierStatus = SupplierStatus.Requested;
            item.RequestedAt ??= DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return BookingItemActionResult.Ok();
    }

    public async Task<BookingItemActionResult> GenerateVoucherAsync(Guid bookingId, Guid itemId)
    {
        var booking = await _db.Bookings
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == bookingId);

        if (booking is null)
        {
            return BookingItemActionResult.Fail("Booking was not found.");
        }

        var item = booking.Items.FirstOrDefault(x => x.Id == itemId);
        if (item is null)
        {
            return BookingItemActionResult.Fail("Booking item was not found.");
        }

        if (item.SupplierStatus != SupplierStatus.Confirmed)
        {
            return BookingItemActionResult.Fail("Voucher can only be generated after supplier confirmation.");
        }

        if (item.VoucherGenerated)
        {
            return BookingItemActionResult.Ok();
        }

        item.VoucherNumber = GenerateVoucherNumber(booking, itemId);
        item.VoucherGenerated = true;
        item.VoucherGeneratedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return BookingItemActionResult.Ok();
    }

    public async Task<BookingItemActionResult> SendVoucherAsync(Guid bookingId, Guid itemId)
    {
        var booking = await _db.Bookings
            .Include(x => x.Client)
            .Include(x => x.Items)
                .ThenInclude(x => x.Supplier)
            .FirstOrDefaultAsync(x => x.Id == bookingId);

        if (booking is null)
        {
            return BookingItemActionResult.Fail("Booking was not found.");
        }

        var item = booking.Items.FirstOrDefault(x => x.Id == itemId);
        if (item is null)
        {
            return BookingItemActionResult.Fail("Booking item was not found.");
        }

        if (item.Supplier is null || string.IsNullOrWhiteSpace(item.Supplier.ContactEmail))
        {
            return BookingItemActionResult.Fail("Add a supplier contact email before sending a voucher.");
        }

        if (item.SupplierStatus != SupplierStatus.Confirmed)
        {
            return BookingItemActionResult.Fail("Voucher can only be sent after supplier confirmation.");
        }

        if (!item.VoucherGenerated || string.IsNullOrWhiteSpace(item.VoucherNumber))
        {
            return BookingItemActionResult.Fail("Generate the voucher before sending it.");
        }

        if (item.VoucherSent)
        {
            return BookingItemActionResult.Ok();
        }

        var branding = await _db.BrandingSettings.AsNoTracking()
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync() ?? new BrandingSettings();

        var agencyName = GetAgencyName(branding);
        var subject = $"Service voucher {booking.BookingRef} - {item.ServiceName}";
        var htmlBody = _templateService.Render("SupplierVoucher", BuildSupplierVoucherVariables(booking, item, branding));
        var plainTextBody = BuildSupplierVoucherPlainText(booking, item, branding);
        var pdfBytes = _voucherDocumentService.Generate(booking, item, branding, agencyName);

        var result = await _emailService.SendAsync(new EmailMessage(
            To: item.Supplier.ContactEmail.Trim(),
            Subject: subject,
            HtmlBody: htmlBody,
            PlainTextBody: plainTextBody,
            Attachments:
            [
                new EmailAttachment(
                    FileName: $"{item.VoucherNumber}.pdf",
                    Content: pdfBytes,
                    ContentType: "application/pdf")
            ]));

        if (!result.Success)
        {
            return BookingItemActionResult.Fail(result.ErrorMessage ?? "Voucher could not be sent.");
        }

        item.VoucherSent = true;
        item.VoucherSentAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return BookingItemActionResult.Ok();
    }

    public async Task<(byte[] PdfBytes, string FileName)?> GetVoucherPdfAsync(Guid bookingId, Guid itemId)
    {
        var booking = await _db.Bookings
            .AsNoTracking()
            .Include(x => x.Client)
            .Include(x => x.Items)
                .ThenInclude(x => x.Supplier)
            .FirstOrDefaultAsync(x => x.Id == bookingId);

        if (booking is null)
        {
            return null;
        }

        var item = booking.Items.FirstOrDefault(x => x.Id == itemId);
        if (item is null || !item.VoucherGenerated || string.IsNullOrWhiteSpace(item.VoucherNumber))
        {
            return null;
        }

        var branding = await _db.BrandingSettings.AsNoTracking()
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync() ?? new BrandingSettings();

        var agencyName = GetAgencyName(branding);
        var pdfBytes = _voucherDocumentService.Generate(booking, item, branding, agencyName);
        return (pdfBytes, $"{item.VoucherNumber}.pdf");
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
            item.VoucherSent = false;
            item.VoucherSentAt = null;
            item.VoucherGenerated = false;
            item.VoucherGeneratedAt = null;
            item.VoucherNumber = null;
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

    private Dictionary<string, string> BuildSupplierRequestVariables(Booking booking, BookingItem item, BrandingSettings branding, bool isReminder)
    {
        var agencyName = GetAgencyName(branding);
        var supplierName = item.Supplier?.Name ?? "Supplier";
        var serviceWindow = FormatServiceWindow(item.ServiceDate, item.EndDate);

        return new Dictionary<string, string>
        {
            ["AgencyName"] = agencyName,
            ["SupplierName"] = supplierName,
            ["SupplierContactName"] = Normalize(item.Supplier?.ContactName) ?? supplierName,
            ["RequestType"] = isReminder ? "Reminder" : "New request",
            ["BookingReference"] = booking.BookingRef,
            ["ClientName"] = booking.Client?.Name ?? "Client",
            ["TravelWindow"] = FormatTravelWindow(booking.TravelStartDate, booking.TravelEndDate),
            ["ServiceName"] = item.ServiceName,
            ["ServiceKind"] = item.ServiceKind.ToString(),
            ["ServiceWindow"] = serviceWindow,
            ["Quantity"] = item.Quantity.ToString(),
            ["Pax"] = item.Pax.ToString(),
            ["SupplierReference"] = Normalize(item.SupplierReference) ?? "Not provided",
            ["SpecialRequests"] = Normalize(booking.SpecialRequests) ?? "No special requests recorded.",
            ["InternalNotes"] = Normalize(item.SupplierNotes) ?? "No supplier-specific notes recorded.",
            ["ContactEmail"] = Normalize(branding.PublicContactEmail) ?? "Reply to this email",
            ["ContactPhone"] = Normalize(branding.ContactPhone) ?? "Phone not provided"
        };
    }

    private string BuildSupplierRequestPlainText(Booking booking, BookingItem item, BrandingSettings branding, bool isReminder)
    {
        var lines = new List<string>
        {
            $"{GetAgencyName(branding)} {(isReminder ? "is following up on" : "would like to request")} booking {booking.BookingRef}.",
            string.Empty,
            $"Client: {booking.Client?.Name ?? "Client"}",
            $"Travel window: {FormatTravelWindow(booking.TravelStartDate, booking.TravelEndDate)}",
            $"Service: {item.ServiceName} ({item.ServiceKind})",
            $"Service dates: {FormatServiceWindow(item.ServiceDate, item.EndDate)}",
            $"Quantity: {item.Quantity}",
            $"Pax: {item.Pax}",
            $"Supplier ref: {Normalize(item.SupplierReference) ?? "Not provided"}",
            string.Empty,
            $"Special requests: {Normalize(booking.SpecialRequests) ?? "No special requests recorded."}",
            $"Supplier notes: {Normalize(item.SupplierNotes) ?? "No supplier-specific notes recorded."}",
            string.Empty,
            $"Contact email: {Normalize(branding.PublicContactEmail) ?? "Reply to this email"}",
            $"Contact phone: {Normalize(branding.ContactPhone) ?? "Phone not provided"}"
        };

        return string.Join(Environment.NewLine, lines);
    }

    private Dictionary<string, string> BuildSupplierVoucherVariables(Booking booking, BookingItem item, BrandingSettings branding)
    {
        var agencyName = GetAgencyName(branding);
        var supplierName = item.Supplier?.Name ?? "Supplier";

        return new Dictionary<string, string>
        {
            ["AgencyName"] = agencyName,
            ["SupplierName"] = supplierName,
            ["SupplierContactName"] = Normalize(item.Supplier?.ContactName) ?? supplierName,
            ["BookingReference"] = booking.BookingRef,
            ["VoucherNumber"] = item.VoucherNumber ?? "Pending voucher number",
            ["ClientName"] = booking.Client?.Name ?? "Client",
            ["LeadGuestName"] = Normalize(booking.LeadGuestName) ?? "Lead guest not recorded",
            ["TravelWindow"] = FormatTravelWindow(booking.TravelStartDate, booking.TravelEndDate),
            ["ServiceName"] = item.ServiceName,
            ["ServiceKind"] = item.ServiceKind.ToString(),
            ["ServiceWindow"] = FormatServiceWindow(item.ServiceDate, item.EndDate),
            ["Quantity"] = item.Quantity.ToString(),
            ["Pax"] = item.Pax.ToString(),
            ["SupplierReference"] = Normalize(item.SupplierReference) ?? "Not provided",
            ["SpecialRequests"] = Normalize(booking.SpecialRequests) ?? "No special requests recorded.",
            ["ContactEmail"] = Normalize(branding.PublicContactEmail) ?? "Reply to this email",
            ["ContactPhone"] = Normalize(branding.ContactPhone) ?? "Phone not provided"
        };
    }

    private string BuildSupplierVoucherPlainText(Booking booking, BookingItem item, BrandingSettings branding)
    {
        var lines = new List<string>
        {
            $"{GetAgencyName(branding)} has attached the supplier voucher for booking {booking.BookingRef}.",
            string.Empty,
            $"Voucher number: {item.VoucherNumber}",
            $"Client: {booking.Client?.Name ?? "Client"}",
            $"Lead guest: {Normalize(booking.LeadGuestName) ?? "Lead guest not recorded"}",
            $"Travel window: {FormatTravelWindow(booking.TravelStartDate, booking.TravelEndDate)}",
            $"Service: {item.ServiceName} ({item.ServiceKind})",
            $"Service dates: {FormatServiceWindow(item.ServiceDate, item.EndDate)}",
            $"Quantity: {item.Quantity}",
            $"Pax: {item.Pax}",
            $"Supplier ref: {Normalize(item.SupplierReference) ?? "Not provided"}",
            string.Empty,
            $"Special requests: {Normalize(booking.SpecialRequests) ?? "No special requests recorded."}",
            string.Empty,
            $"Contact email: {Normalize(branding.PublicContactEmail) ?? "Reply to this email"}",
            $"Contact phone: {Normalize(branding.ContactPhone) ?? "Phone not provided"}"
        };

        return string.Join(Environment.NewLine, lines);
    }

    private string GetAgencyName(BrandingSettings branding)
        => string.IsNullOrWhiteSpace(branding.AgencyName)
            ? _tenantContext.TenantName ?? "Your travel workspace"
            : branding.AgencyName.Trim();

    private string GenerateVoucherNumber(Booking booking, Guid itemId)
    {
        var year = DateTime.UtcNow.Year;
        var bookingSequence = ExtractBookingSequence(booking.BookingRef) ?? booking.Id.ToString("N")[..8].ToUpperInvariant();
        var itemSequence = booking.Items
            .OrderBy(x => x.ServiceDate)
            .ThenBy(x => x.ServiceName)
            .ThenBy(x => x.Id)
            .Select((x, index) => new { x.Id, Sequence = index + 1 })
            .First(x => x.Id == itemId)
            .Sequence;

        return $"V-{year}-{bookingSequence}-{itemSequence:00}";
    }

    private static string? ExtractBookingSequence(string bookingRef)
    {
        var parts = bookingRef.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 3 ? parts[^1] : null;
    }

    private static string FormatTravelWindow(DateOnly? startDate, DateOnly? endDate)
    {
        if (!startDate.HasValue && !endDate.HasValue)
        {
            return "Travel dates pending";
        }

        if (startDate.HasValue && endDate.HasValue)
        {
            return $"{startDate.Value:dd MMM yyyy} - {endDate.Value:dd MMM yyyy}";
        }

        return startDate?.ToString("dd MMM yyyy") ?? endDate?.ToString("dd MMM yyyy") ?? "Travel dates pending";
    }

    private static string FormatServiceWindow(DateOnly? serviceDate, DateOnly? endDate)
    {
        if (!serviceDate.HasValue && !endDate.HasValue)
        {
            return "To be confirmed";
        }

        if (serviceDate.HasValue && endDate.HasValue)
        {
            return $"{serviceDate.Value:dd MMM yyyy} - {endDate.Value:dd MMM yyyy}";
        }

        return serviceDate?.ToString("dd MMM yyyy") ?? endDate?.ToString("dd MMM yyyy") ?? "To be confirmed";
    }

    private static bool TryParseStatus(string? status, out BookingStatus parsedStatus)
        => Enum.TryParse(status, true, out parsedStatus);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
