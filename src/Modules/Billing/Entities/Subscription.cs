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

    // Status
    public SubscriptionStatus Status { get; set; }
    public BillingCycle BillingCycle { get; set; }

    // Dates
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public DateTime? GracePeriodEndsAt { get; set; }

    // Per-seat tracking
    public int Quantity { get; set; } = 1;

    // Paystack identifiers
    public string? PaystackSubscriptionCode { get; set; }
    public string? PaystackCustomerCode { get; set; }
    public string? PaystackAuthorizationCode { get; set; }
    public string? PaystackEmailToken { get; set; }
    public string? PaystackAuthorizationEmail { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    // Optimistic concurrency
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public enum SubscriptionStatus
{
    Active,
    PastDue,
    Cancelled,
    Expired,
    Trialing,
    NonRenewing,
    PendingPayment
}

public enum BillingCycle
{
    Monthly,
    Annual
}
