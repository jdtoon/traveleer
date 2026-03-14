using System.ComponentModel.DataAnnotations;
using saas.Modules.Bookings.Entities;

namespace saas.Modules.Bookings.DTOs;

public class PaymentLinkListDto
{
    public Guid BookingId { get; set; }
    public string BookingRef { get; set; } = string.Empty;
    public decimal TotalSelling { get; set; }
    public string SellingCurrencyCode { get; set; } = "USD";
    public string? ClientName { get; set; }
    public List<PaymentLinkItemDto> Links { get; set; } = [];
}

public class PaymentLinkItemDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public PaymentLinkStatus Status { get; set; }
    public string? Description { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string Token { get; set; } = string.Empty;
}

public class PaymentLinkFormDto
{
    public Guid BookingId { get; set; }

    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.01, 999999999, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Currency is required.")]
    [StringLength(10)]
    public string CurrencyCode { get; set; } = "USD";

    [StringLength(500)]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Expiry days is required.")]
    [Range(1, 90, ErrorMessage = "Expiry must be between 1 and 90 days.")]
    public int ExpiryDays { get; set; } = 7;
}

public class PaymentLinkPublicDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public string? Description { get; set; }
    public string BookingRef { get; set; } = string.Empty;
    public string? ClientName { get; set; }
    public PaymentLinkStatus Status { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string AgencyName { get; set; } = "Travel Agency";
    public string? LogoUrl { get; set; }
    public string PrimaryColor { get; set; } = "#2563EB";
    public string TenantSlug { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}
