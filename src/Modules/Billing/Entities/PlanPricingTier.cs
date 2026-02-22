namespace saas.Modules.Billing.Entities;

public class PlanPricingTier
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public Plan Plan { get; set; } = null!;

    public int MinUnits { get; set; }
    public int? MaxUnits { get; set; }
    public decimal PricePerUnit { get; set; }
}
