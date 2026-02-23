using saas.Modules.Tenancy.Entities;

namespace saas.Modules.Billing.Entities;

public class TenantCredit
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public CreditReason Reason { get; set; }
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public Guid? ConsumedByInvoiceId { get; set; }
    public Invoice? ConsumedByInvoice { get; set; }

    // Partial consumption tracking
    public decimal RemainingAmount { get; set; }

    // Optimistic concurrency
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public enum CreditReason
{
    PlanChangeCredit,
    Refund,
    Manual,
    Promotional
}
