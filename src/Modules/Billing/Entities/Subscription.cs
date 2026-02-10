using saas.Data;
using saas.Modules.Tenancy.Entities;

namespace saas.Modules.Billing.Entities;

public class Subscription : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid PlanId { get; set; }
    public Plan Plan { get; set; } = null!;
    public SubscriptionStatus Status { get; set; }
    public BillingCycle BillingCycle { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? PaystackSubscriptionCode { get; set; }
    public string? PaystackCustomerCode { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public enum SubscriptionStatus
{
    Active,
    PastDue,
    Cancelled,
    Expired,
    Trialing,
    NonRenewing
}

public enum BillingCycle
{
    Monthly,
    Annual
}
