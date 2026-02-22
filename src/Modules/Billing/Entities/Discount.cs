using saas.Data;
using saas.Modules.Tenancy.Entities;

namespace saas.Modules.Billing.Entities;

public class Discount : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public DiscountType Type { get; set; }
    public decimal Value { get; set; }
    public string Currency { get; set; } = "ZAR";

    // Limits
    public int? MaxRedemptions { get; set; }
    public int CurrentRedemptions { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }

    // Scope
    public string? ApplicablePlanSlugs { get; set; }
    public int? DurationInCycles { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<TenantDiscount> TenantDiscounts { get; set; } = [];

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public enum DiscountType
{
    Percentage,
    FixedAmount
}
