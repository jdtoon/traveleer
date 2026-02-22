using saas.Modules.Tenancy.Entities;

namespace saas.Modules.Billing.Entities;

public class TenantAddOn
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid AddOnId { get; set; }
    public AddOn AddOn { get; set; } = null!;

    public int Quantity { get; set; } = 1;
    public DateTime ActivatedAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }

    // Paystack (for recurring add-ons)
    public string? PaystackSubscriptionCode { get; set; }
}
