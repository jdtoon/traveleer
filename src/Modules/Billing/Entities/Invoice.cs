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
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public InvoiceStatus Status { get; set; }
    public DateTime IssuedDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? PaidDate { get; set; }
    public string? PaystackReference { get; set; }
    public string? Description { get; set; }

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
    Refunded
}
