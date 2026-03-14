using saas.Data;
using saas.Modules.Settings.Entities;

namespace saas.Modules.Bookings.Entities;

public class SupplierPayment : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingItemId { get; set; }
    public BookingItem? BookingItem { get; set; }
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public DateOnly PaymentDate { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.BankTransfer;
    public string? Reference { get; set; }
    public PaymentDirection Direction { get; set; } = PaymentDirection.Paid;
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
