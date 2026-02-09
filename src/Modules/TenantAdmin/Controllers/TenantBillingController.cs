using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.TenantAdmin.Controllers;

[Authorize(Policy = "TenantAdmin")]
public class TenantBillingController : SwapController
{
    private readonly CoreDbContext _coreDb;
    private readonly IBillingService _billingService;
    private readonly ITenantContext _tenantContext;

    public TenantBillingController(
        CoreDbContext coreDb,
        IBillingService billingService,
        ITenantContext tenantContext)
    {
        _coreDb = coreDb;
        _billingService = billingService;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var model = await GetBillingModelAsync();
        if (model is null) return NotFound();
        return SwapView(model);
    }

    [HttpGet]
    public async Task<IActionResult> ChangePlanModal()
    {
        var plans = await _coreDb.Plans
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();

        var tenantId = _tenantContext.TenantId;
        var currentPlanId = await _coreDb.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.PlanId)
            .FirstOrDefaultAsync();

        return SwapView("_ChangePlanModal", new ChangePlanViewModel
        {
            Plans = plans,
            CurrentPlanId = currentPlanId
        });
    }

    [HttpPost]
    public async Task<IActionResult> ChangePlan([FromForm] Guid planId)
    {
        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue) return NotFound();

        var result = await _billingService.ChangePlanAsync(tenantId.Value, planId);
        if (!result.Success)
        {
            return SwapResponse()
                .WithErrorToast(result.Error ?? "Failed to change plan")
                .WithView("_ModalClose")
                .Build();
        }

        // Paid plan change — redirect to Paystack checkout
        if (!string.IsNullOrEmpty(result.PaymentUrl))
        {
            Response.Headers["HX-Redirect"] = result.PaymentUrl;
            return Ok();
        }

        // Free plan change — refresh the billing page
        var model = await GetBillingModelAsync();
        return SwapResponse()
            .WithView("_ModalClose")
            .AlsoUpdate("billing-content", "_BillingContent", model)
            .WithSuccessToast("Plan changed successfully")
            .Build();
    }

    /// <summary>
    /// Callback endpoint after Paystack payment for plan change.
    /// Full-page GET request from Paystack redirect.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Callback([FromQuery] string? reference, [FromQuery] string? trxref)
    {
        var ref_ = reference ?? trxref;
        var slug = _tenantContext.Slug;

        if (string.IsNullOrEmpty(ref_))
        {
            return SwapView("Callback", new { Success = false, Slug = slug,
                ErrorMessage = "Invalid payment reference. Please contact support." });
        }

        // Verify and link the real subscription code
        await _billingService.VerifyAndLinkSubscriptionAsync(ref_);

        // Find the subscription by Paystack reference
        var subscription = await _coreDb.Subscriptions
            .Include(s => s.Tenant)
            .FirstOrDefaultAsync(s => s.PaystackSubscriptionCode == ref_);

        if (subscription is null)
        {
            return SwapView("Callback", new { Success = false, Slug = slug,
                ErrorMessage = "Could not verify your payment. Please contact support." });
        }

        return SwapView("Callback", new { Success = true, Slug = slug,
            ErrorMessage = (string?)null });
    }

    [HttpPost]
    public async Task<IActionResult> Cancel()
    {
        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue) return NotFound();

        var success = await _billingService.CancelSubscriptionAsync(tenantId.Value);
        if (!success)
        {
            return SwapResponse()
                .WithErrorToast("Failed to cancel subscription")
                .Build();
        }

        var model = await GetBillingModelAsync();
        return SwapResponse()
            .WithView("_BillingContent", model)
            .WithWarningToast("Subscription cancelled")
            .Build();
    }

    [HttpPost]
    public async Task<IActionResult> ManageSubscription()
    {
        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue) return NotFound();

        var manageLink = await _billingService.GetManageLinkAsync(tenantId.Value);
        if (string.IsNullOrEmpty(manageLink))
        {
            return SwapResponse()
                .WithErrorToast("Unable to retrieve subscription management link")
                .Build();
        }

        Response.Headers["HX-Redirect"] = manageLink;
        return Ok();
    }

    private async Task<BillingViewModel?> GetBillingModelAsync()
    {
        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue) return null;

        var tenant = await _coreDb.Tenants
            .Include(t => t.Plan)
            .Include(t => t.ActiveSubscription)
            .FirstOrDefaultAsync(t => t.Id == tenantId.Value);

        if (tenant is null) return null;

        var invoices = await _coreDb.Invoices
            .Where(i => i.TenantId == tenantId.Value)
            .OrderByDescending(i => i.IssuedDate)
            .Take(10)
            .ToListAsync();

        return new BillingViewModel
        {
            PlanName = tenant.Plan.Name,
            PlanId = tenant.PlanId,
            MonthlyPrice = tenant.Plan.MonthlyPrice,
            Currency = tenant.Plan.Currency,
            SubscriptionStatus = tenant.ActiveSubscription?.Status,
            BillingCycle = tenant.ActiveSubscription?.BillingCycle,
            NextBillingDate = tenant.ActiveSubscription?.NextBillingDate,
            CancelledAt = tenant.ActiveSubscription?.CancelledAt,
            HasPaystackSubscription = tenant.ActiveSubscription?.PaystackSubscriptionCode?.StartsWith("SUB_") == true,
            Invoices = invoices
        };
    }
}

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
