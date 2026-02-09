using saas.Data.Core;

namespace saas.Modules.Marketing.Models;

public class MarketingPricingViewModel
{
    public IReadOnlyList<Plan> Plans { get; init; } = Array.Empty<Plan>();
    public string BillingCycle { get; init; } = "monthly";
}
