using saas.Modules.Billing.Entities;
using saas.Shared;

namespace saas.Modules.TenantAdmin.Models;

public class BillingViewModel
{
    public string PlanName { get; set; } = string.Empty;
    public Guid PlanId { get; set; }
    public decimal MonthlyPrice { get; set; }
    public string Currency { get; set; } = "ZAR";
    public SubscriptionStatus? SubscriptionStatus { get; set; }
    public BillingCycle? BillingCycle { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? CancelledAt { get; set; }
    public bool HasPaystackSubscription { get; set; }
    public List<Invoice> Invoices { get; set; } = [];
}

public class ChangePlanViewModel
{
    public List<Plan> Plans { get; set; } = [];
    public Guid CurrentPlanId { get; set; }
}

public class PlanChangeConfirmViewModel
{
    public PlanChangePreview Preview { get; set; } = null!;
    public Guid NewPlanId { get; set; }
}

public class CancelConfirmViewModel
{
    public string TenantName { get; set; } = string.Empty;
}
