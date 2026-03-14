using saas.Data;
using saas.Modules.Clients.Entities;

namespace saas.Modules.Bookings.Entities;

public enum PaymentLinkStatus
{
    Pending = 0,
    Paid = 1,
    Expired = 2,
    Cancelled = 3
}

public class PaymentLink : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public Booking? Booking { get; set; }
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public string Token { get; set; } = string.Empty;
    public PaymentLinkStatus Status { get; set; } = PaymentLinkStatus.Pending;
    public string? Description { get; set; }
    public string? StripeSessionId { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public string TenantSlug { get; set; } = string.Empty;
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
