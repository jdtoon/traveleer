using saas.Modules.Billing.Entities;
using saas.Shared;

namespace saas.Modules.TenantAdmin.Models;

public class BillingViewModel
{
    public string PlanName { get; set; } = string.Empty;
    public Guid PlanId { get; set; }
    public decimal MonthlyPrice { get; set; }
    public decimal? AnnualPrice { get; set; }
    public string Currency { get; set; } = "ZAR";
    public SubscriptionStatus? SubscriptionStatus { get; set; }
    public BillingCycle? BillingCycle { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public bool HasPaystackSubscription { get; set; }
    public List<Invoice> Invoices { get; set; } = [];

    // Seat management
    public BillingModel BillingModel { get; set; }
    public int CurrentSeats { get; set; }
    public int MaxSeats { get; set; }
    public decimal PerSeatPrice { get; set; }

    // Add-ons
    public List<TenantAddOn> ActiveAddOns { get; set; } = [];
    public List<AddOn> AvailableAddOns { get; set; } = [];

    // Discounts & Credits
    public List<TenantDiscount> ActiveDiscounts { get; set; } = [];
    public decimal CreditBalance { get; set; }

    // Usage
    public Dictionary<string, long> CurrentUsage { get; set; } = new();
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

public class SeatChangeConfirmViewModel
{
    public SeatChangePreview Preview { get; set; } = null!;
    public int NewSeatCount { get; set; }
}

public class InvoiceDetailViewModel
{
    public Invoice Invoice { get; set; } = null!;
    public List<InvoiceLineItem> LineItems { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
}

public class AddOnViewModel
{
    public List<AddOn> AvailableAddOns { get; set; } = [];
    public List<TenantAddOn> ActiveAddOns { get; set; } = [];
}
