using saas.Data;

namespace saas.Modules.Billing.Entities;

public class AddOn : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "ZAR";
    public AddOnInterval BillingInterval { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    // Paystack (for recurring add-ons)
    public string? PaystackPlanCode { get; set; }

    // Navigation
    public ICollection<TenantAddOn> TenantAddOns { get; set; } = [];

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public enum AddOnInterval
{
    OneOff,
    Monthly,
    Annual
}
