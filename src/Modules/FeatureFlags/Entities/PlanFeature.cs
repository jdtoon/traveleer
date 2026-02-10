using saas.Modules.Billing.Entities;

namespace saas.Modules.FeatureFlags.Entities;

public class PlanFeature
{
    public Guid PlanId { get; set; }
    public Plan Plan { get; set; } = null!;
    public Guid FeatureId { get; set; }
    public Feature Feature { get; set; } = null!;
    public string? ConfigJson { get; set; }
}
