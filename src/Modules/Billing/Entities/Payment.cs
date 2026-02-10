using saas.Data;
using saas.Modules.Tenancy.Entities;

namespace saas.Modules.Billing.Entities;

public class Payment : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public PaymentStatus Status { get; set; }
    public string? PaystackReference { get; set; }
    public string? PaystackTransactionId { get; set; }
    public string? GatewayResponse { get; set; }
    public DateTime TransactionDate { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public enum PaymentStatus
{
    Pending,
    Success,
    Failed,
    Refunded
}
