using saas.Data;
using saas.Modules.FeatureFlags.Entities;
using saas.Modules.Tenancy.Entities;

namespace saas.Modules.Billing.Entities;

public class Plan : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal MonthlyPrice { get; set; }
    public decimal? AnnualPrice { get; set; }
    public string Currency { get; set; } = "ZAR";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public int? MaxUsers { get; set; }
    public int? MaxRequestsPerMinute { get; set; }
    public string? PaystackPlanCode { get; set; }

    public ICollection<PlanFeature> PlanFeatures { get; set; } = [];
    public ICollection<Tenant> Tenants { get; set; } = [];

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
