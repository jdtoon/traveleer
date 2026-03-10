using saas.Data;
using saas.Modules.Clients.Entities;
using saas.Modules.Quotes.Entities;

namespace saas.Modules.Bookings.Entities;

public enum BookingStatus
{
    Provisional = 1,
    Confirmed = 2,
    InProgress = 3,
    Completed = 4,
    Cancelled = 5
}

public class Booking : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? QuoteId { get; set; }
    public string BookingRef { get; set; } = string.Empty;
    public BookingStatus Status { get; set; } = BookingStatus.Provisional;
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }
    public Quote? Quote { get; set; }
    public string? ClientReference { get; set; }
    public DateOnly? TravelStartDate { get; set; }
    public DateOnly? TravelEndDate { get; set; }
    public int Pax { get; set; } = 1;
    public string? LeadGuestName { get; set; }
    public string? LeadGuestNationality { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalSelling { get; set; }
    public decimal TotalProfit { get; set; }
    public string CostCurrencyCode { get; set; } = "USD";
    public string SellingCurrencyCode { get; set; } = "USD";
    public string? InternalNotes { get; set; }
    public string? SpecialRequests { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public ICollection<BookingItem> Items { get; set; } = [];
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
