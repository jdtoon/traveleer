using saas.Data;
using saas.Modules.Tenancy.Entities;

namespace saas.Modules.Billing.Entities;

public class Invoice : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid? SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }

    // Invoice identity
    public string InvoiceNumber { get; set; } = string.Empty;
    public InvoiceStatus Status { get; set; }

    // Amounts
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal CreditApplied { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "ZAR";

    // Legacy single-amount field (computed alias for backwards compat in views)
    public decimal Amount
    {
        get => Total;
        set => Total = value;
    }

    // Dates
    public DateTime IssuedDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? PaidDate { get; set; }
    public DateTime? BillingPeriodStart { get; set; }
    public DateTime? BillingPeriodEnd { get; set; }

    // Company details (snapshot at invoice time)
    public string? CompanyName { get; set; }
    public string? CompanyAddress { get; set; }
    public string? CompanyVatNumber { get; set; }

    // Customer details (snapshot at invoice time)
    public string? TenantCompanyName { get; set; }
    public string? TenantBillingAddress { get; set; }
    public string? TenantVatNumber { get; set; }

    // Description
    public string? Description { get; set; }

    // Paystack
    public string? PaystackReference { get; set; }

    // Navigation
    public ICollection<InvoiceLineItem> LineItems { get; set; } = [];
    public Payment? Payment { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public enum InvoiceStatus
{
    Draft,
    Issued,
    Paid,
    Overdue,
    Cancelled,
    Refunded,
    PartiallyRefunded
}
