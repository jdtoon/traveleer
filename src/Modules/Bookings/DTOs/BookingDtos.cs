using System.ComponentModel.DataAnnotations;
using saas.Modules.Bookings.Entities;
using saas.Modules.Inventory.Entities;

namespace saas.Modules.Bookings.DTOs;

public class BookingOptionDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class BookingListItemDto
{
    public Guid Id { get; set; }
    public Guid? QuoteId { get; set; }
    public string BookingRef { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public BookingStatus Status { get; set; }
    public DateOnly? TravelStartDate { get; set; }
    public DateOnly? TravelEndDate { get; set; }
    public int Pax { get; set; }
    public int ItemCount { get; set; }
    public decimal TotalSelling { get; set; }
    public string SellingCurrencyCode { get; set; } = "USD";
    public DateTime CreatedAt { get; set; }
}

public class BookingFormDto
{
    [Required(ErrorMessage = "Client is required.")]
    public Guid? ClientId { get; set; }

    [StringLength(100)]
    public string? ClientReference { get; set; }

    public DateOnly? TravelStartDate { get; set; }
    public DateOnly? TravelEndDate { get; set; }

    [Range(1, 500, ErrorMessage = "Pax must be at least 1.")]
    public int Pax { get; set; } = 1;

    [StringLength(200)]
    public string? LeadGuestName { get; set; }

    [StringLength(100)]
    public string? LeadGuestNationality { get; set; }

    [Required(ErrorMessage = "Selling currency is required.")]
    [StringLength(10)]
    public string SellingCurrencyCode { get; set; } = "USD";

    [StringLength(4000)]
    public string? InternalNotes { get; set; }

    [StringLength(4000)]
    public string? SpecialRequests { get; set; }

    public List<BookingOptionDto> ClientOptions { get; set; } = [];
    public List<string> CurrencyOptions { get; set; } = [];
}

public class BookingItemFormDto
{
    public Guid BookingId { get; set; }

    [Required(ErrorMessage = "Inventory item is required.")]
    public Guid? InventoryItemId { get; set; }

    public DateOnly? ServiceDate { get; set; }
    public DateOnly? EndDate { get; set; }

    [Range(1, 500, ErrorMessage = "Quantity must be at least 1.")]
    public int Quantity { get; set; } = 1;

    [Range(1, 500, ErrorMessage = "Pax must be at least 1.")]
    public int Pax { get; set; } = 1;

    [Range(typeof(decimal), "0", "999999999", ErrorMessage = "Selling price must be zero or more.")]
    public decimal SellingPrice { get; set; }

    [StringLength(100)]
    public string? SupplierReference { get; set; }

    [StringLength(2000)]
    public string? SupplierNotes { get; set; }

    public List<BookingOptionDto> InventoryOptions { get; set; } = [];
}

public class BookingItemListItemDto
{
    public Guid Id { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public InventoryItemKind ServiceKind { get; set; }
    public string? Description { get; set; }
    public DateOnly? ServiceDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? Nights { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public string CostCurrencyCode { get; set; } = "USD";
    public string SellingCurrencyCode { get; set; } = "USD";
    public int Quantity { get; set; }
    public int Pax { get; set; }
    public Guid? SupplierId { get; set; }
    public SupplierStatus SupplierStatus { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierEmail { get; set; }
    public DateTime? RequestedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public bool VoucherSent { get; set; }
    public DateTime? VoucherSentAt { get; set; }
    public bool VoucherGenerated { get; set; }
    public DateTime? VoucherGeneratedAt { get; set; }
    public string? VoucherNumber { get; set; }
    public string? SupplierReference { get; set; }
    public string? SupplierNotes { get; set; }
    public decimal LineCostTotal => CostPrice * Quantity;
    public decimal LineSellingTotal => SellingPrice * Quantity;
}

public class BookingDetailsDto
{
    public Guid Id { get; set; }
    public Guid? QuoteId { get; set; }
    public string? QuoteReferenceNumber { get; set; }
    public string BookingRef { get; set; } = string.Empty;
    public BookingStatus Status { get; set; }
    public Guid ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string? ClientReference { get; set; }
    public DateOnly? TravelStartDate { get; set; }
    public DateOnly? TravelEndDate { get; set; }
    public int Pax { get; set; }
    public string? LeadGuestName { get; set; }
    public string? LeadGuestNationality { get; set; }
    public string CostCurrencyCode { get; set; } = "USD";
    public string SellingCurrencyCode { get; set; } = "USD";
    public decimal TotalCost { get; set; }
    public decimal TotalSelling { get; set; }
    public decimal TotalProfit { get; set; }
    public string? InternalNotes { get; set; }
    public string? SpecialRequests { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public List<BookingItemListItemDto> Items { get; set; } = [];

    public string TravelWindowSummary
    {
        get
        {
            if (TravelStartDate.HasValue && TravelEndDate.HasValue)
            {
                return $"{TravelStartDate.Value:dd MMM yyyy} - {TravelEndDate.Value:dd MMM yyyy}";
            }

            if (TravelStartDate.HasValue)
            {
                return $"Starting {TravelStartDate.Value:dd MMM yyyy}";
            }

            if (TravelEndDate.HasValue)
            {
                return $"Ending {TravelEndDate.Value:dd MMM yyyy}";
            }

            return "Travel dates not set yet";
        }
    }

    public string TravelWindowCompact
        => TravelStartDate.HasValue
            ? TravelWindowSummary
            : "To be confirmed";
}

public class BookingConversionResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public Guid? BookingId { get; init; }
    public string? BookingRef { get; init; }
    public bool AlreadyExists { get; init; }

    public static BookingConversionResult Ok(Guid bookingId, string bookingRef, bool alreadyExists = false)
        => new()
        {
            Success = true,
            BookingId = bookingId,
            BookingRef = bookingRef,
            AlreadyExists = alreadyExists
        };

    public static BookingConversionResult Fail(string errorMessage)
        => new()
        {
            Success = false,
            ErrorMessage = errorMessage
        };
}

public class BookingItemActionResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static BookingItemActionResult Ok()
        => new()
        {
            Success = true
        };

    public static BookingItemActionResult Fail(string errorMessage)
        => new()
        {
            Success = false,
            ErrorMessage = errorMessage
        };
}
