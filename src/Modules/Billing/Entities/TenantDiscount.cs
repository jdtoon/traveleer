using saas.Modules.Tenancy.Entities;

namespace saas.Modules.Billing.Entities;

public class TenantDiscount
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid DiscountId { get; set; }
    public Discount Discount { get; set; } = null!;

    public DateTime AppliedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? RemainingCycles { get; set; }
    public bool IsActive { get; set; } = true;
}
