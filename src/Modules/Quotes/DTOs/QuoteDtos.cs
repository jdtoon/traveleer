using System.ComponentModel.DataAnnotations;
using saas.Modules.Quotes.Entities;
using saas.Modules.RateCards.Entities;

namespace saas.Modules.Quotes.DTOs;

public class QuoteListItemDto
{
    public Guid Id { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public QuoteStatus Status { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string? ClientEmail { get; set; }
    public string OutputCurrencyCode { get; set; } = "USD";
    public int RateCardCount { get; set; }
    public string? PrimaryHotelName { get; set; }
    public DateOnly? TravelStartDate { get; set; }
    public DateOnly? TravelEndDate { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class QuoteClientOptionDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class QuoteRateCardOptionDto
{
    public Guid Id { get; set; }
    public string RateCardName { get; set; } = string.Empty;
    public string HotelName { get; set; } = string.Empty;
    public string? DestinationName { get; set; }
    public string ContractCurrencyCode { get; set; } = "USD";
    public RateCardStatus Status { get; set; }
    public int SeasonCount { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public bool IsSelected { get; set; }
}

public class QuoteBuilderDto
{
    public Guid? ClientId { get; set; }

    [Required(ErrorMessage = "Client name is required.")]
    [StringLength(200)]
    public string ClientName { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Enter a valid client email.")]
    [StringLength(320)]
    public string? ClientEmail { get; set; }

    [StringLength(50)]
    public string? ClientPhone { get; set; }

    [Required(ErrorMessage = "Output currency is required.")]
    [StringLength(10)]
    public string OutputCurrencyCode { get; set; } = "USD";

    [Range(typeof(decimal), "0", "999", ErrorMessage = "Markup must be zero or more.")]
    public decimal MarkupPercentage { get; set; } = 10m;

    [StringLength(32)]
    public string GroupBy { get; set; } = "ratecard";

    public DateOnly? ValidUntil { get; set; }
    public DateOnly? TravelStartDate { get; set; }
    public DateOnly? TravelEndDate { get; set; }
    public bool FilterByTravelDates { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    [StringLength(4000)]
    public string? InternalNotes { get; set; }

    public List<Guid> SelectedRateCardIds { get; set; } = [];
    public List<QuoteClientOptionDto> ClientOptions { get; set; } = [];
    public List<string> CurrencyOptions { get; set; } = [];
    public List<QuoteRateCardOptionDto> AvailableRateCards { get; set; } = [];
}

public class QuoteDetailsDto
{
    public Guid Id { get; set; }
    public Guid? BookingId { get; set; }
    public string? BookingReferenceNumber { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public QuoteStatus Status { get; set; }
    public Guid? ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string? ClientEmail { get; set; }
    public string? ClientPhone { get; set; }
    public string OutputCurrencyCode { get; set; } = "USD";
    public decimal MarkupPercentage { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public DateOnly? TravelStartDate { get; set; }
    public DateOnly? TravelEndDate { get; set; }
    public bool FilterByTravelDates { get; set; }
    public string? Notes { get; set; }
    public string? InternalNotes { get; set; }
    public int RateCardCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class QuotePreviewDto
{
    public string ClientName { get; set; } = string.Empty;
    public string OutputCurrencyCode { get; set; } = "USD";
    public string CurrencySymbol { get; set; } = "$";
    public decimal MarkupPercentage { get; set; }
    public string? FooterText { get; set; }
    public bool FilterByTravelDates { get; set; }
    public DateOnly? TravelStartDate { get; set; }
    public DateOnly? TravelEndDate { get; set; }
    public List<QuotePreviewItemDto> Items { get; set; } = [];
}

public class QuotePreviewItemDto
{
    public Guid RateCardId { get; set; }
    public string RateCardName { get; set; } = string.Empty;
    public string HotelName { get; set; } = string.Empty;
    public string? DestinationName { get; set; }
    public string ContractCurrencyCode { get; set; } = "USD";
    public RateCardStatus Status { get; set; }
    public List<QuotePreviewRoomTypeDto> RoomTypes { get; set; } = [];
    public List<QuotePreviewSeasonDto> Seasons { get; set; } = [];
}

public class QuotePreviewRoomTypeDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class QuotePreviewSeasonDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsBlackout { get; set; }
    public string? Notes { get; set; }
    public List<QuotePreviewRateDto> Rates { get; set; } = [];
}

public class QuotePreviewRateDto
{
    public Guid RoomTypeId { get; set; }
    public string RoomTypeCode { get; set; } = string.Empty;
    public decimal WeekdayRate { get; set; }
    public decimal? WeekendRate { get; set; }
    public bool IsIncluded { get; set; }
}
