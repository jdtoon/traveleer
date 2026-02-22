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

    // Pricing
    public decimal MonthlyPrice { get; set; }
    public decimal? AnnualPrice { get; set; }
    public string Currency { get; set; } = "ZAR";
    public BillingModel BillingModel { get; set; } = BillingModel.FlatRate;

    // Per-seat pricing (when BillingModel = PerSeat or Hybrid)
    public int? IncludedSeats { get; set; }
    public decimal? PerSeatMonthlyPrice { get; set; }
    public decimal? PerSeatAnnualPrice { get; set; }

    // One-time
    public decimal? SetupFee { get; set; }

    // Trial
    public int? TrialDays { get; set; }

    // Limits
    public int? MaxUsers { get; set; }
    public int? MaxRequestsPerMinute { get; set; }

    // Display
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    // Paystack — one plan code per interval
    public string? PaystackMonthlyPlanCode { get; set; }
    public string? PaystackAnnualPlanCode { get; set; }

    // Navigation
    public ICollection<PlanFeature> PlanFeatures { get; set; } = [];
    public ICollection<PlanPricingTier> PricingTiers { get; set; } = [];
    public ICollection<Tenant> Tenants { get; set; } = [];

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    // Computed
    public bool IsFreePlan => MonthlyPrice == 0;
}

public enum BillingModel
{
    FlatRate,
    PerSeat,
    UsageBased,
    Hybrid
}
