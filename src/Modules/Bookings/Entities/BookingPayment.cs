using saas.Data;

namespace saas.Modules.Bookings.Entities;

public enum PaymentMethod
{
    Cash = 1,
    BankTransfer = 2,
    CreditCard = 3,
    Online = 4,
    Other = 5
}

public enum PaymentDirection
{
    Received = 1,
    Refunded = 2,
    Paid = 3
}

public class BookingPayment : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public Booking? Booking { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public DateOnly PaymentDate { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.BankTransfer;
    public string? Reference { get; set; }
    public PaymentDirection Direction { get; set; } = PaymentDirection.Received;
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
