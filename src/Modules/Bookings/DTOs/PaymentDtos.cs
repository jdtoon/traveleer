using System.ComponentModel.DataAnnotations;
using saas.Modules.Bookings.Entities;

namespace saas.Modules.Bookings.DTOs;

public class BookingPaymentListDto
{
    public Guid BookingId { get; set; }
    public string BookingRef { get; set; } = string.Empty;
    public decimal TotalSelling { get; set; }
    public decimal TotalReceived { get; set; }
    public decimal ClientBalance { get; set; }
    public string SellingCurrencyCode { get; set; } = "USD";
    public DateOnly? TravelStartDate { get; set; }
    public List<BookingPaymentItemDto> Payments { get; set; } = [];
}

public class BookingPaymentItemDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public DateOnly PaymentDate { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string? Reference { get; set; }
    public PaymentDirection Direction { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BookingPaymentFormDto
{
    public Guid BookingId { get; set; }

    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.01, 999999999, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Payment date is required.")]
    public DateOnly? PaymentDate { get; set; }

    [Required(ErrorMessage = "Payment method is required.")]
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.BankTransfer;

    public PaymentDirection Direction { get; set; } = PaymentDirection.Received;

    [StringLength(100)]
    public string? Reference { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public string CurrencyCode { get; set; } = "USD";
}

public class SupplierPaymentListDto
{
    public Guid BookingItemId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public decimal CostPrice { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal SupplierBalance { get; set; }
    public string CostCurrencyCode { get; set; } = "USD";
    public List<SupplierPaymentItemDto> Payments { get; set; } = [];
}

public class SupplierPaymentItemDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public DateOnly PaymentDate { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string? Reference { get; set; }
    public PaymentDirection Direction { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SupplierPaymentFormDto
{
    public Guid BookingItemId { get; set; }
    public Guid SupplierId { get; set; }

    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.01, 999999999, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Payment date is required.")]
    public DateOnly? PaymentDate { get; set; }

    [Required(ErrorMessage = "Payment method is required.")]
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.BankTransfer;

    public PaymentDirection Direction { get; set; } = PaymentDirection.Paid;

    [StringLength(100)]
    public string? Reference { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public string CurrencyCode { get; set; } = "USD";
}
