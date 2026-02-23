using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Modules.Billing.Entities;
using saas.Modules.Billing.Services;
using saas.Modules.TenantAdmin.Models;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.TenantAdmin.Controllers;

[Authorize(Policy = "TenantAdmin")]
[Route("{slug}/admin/billing")]
public class TenantBillingController : SwapController
{
    private readonly CoreDbContext _coreDb;
    private readonly IBillingService _billingService;
    private readonly ITenantContext _tenantContext;
    private readonly IAddOnService _addOnService;
    private readonly ICreditService _creditService;

    public TenantBillingController(
        CoreDbContext coreDb,
        IBillingService billingService,
        ITenantContext tenantContext,
        IAddOnService addOnService,
        ICreditService creditService)
    {
        _coreDb = coreDb;
        _billingService = billingService;
        _tenantContext = tenantContext;
        _addOnService = addOnService;
        _creditService = creditService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var model = await GetBillingModelAsync();
        if (model is null) return NotFound();
        return SwapView(model);
    }

    [HttpGet("change-plan-modal")]
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

        return SwapView(SwapViews.TenantBilling._ChangePlanModal, new ChangePlanViewModel
        {
            Plans = plans,
            CurrentPlanId = currentPlanId
        });
    }

    [HttpPost("preview-plan-change")]
    public async Task<IActionResult> PreviewPlanChange([FromForm] Guid planId)
    {
        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue) return NotFound();

        var preview = await _billingService.PreviewPlanChangeAsync(tenantId.Value, planId);
        if (!preview.IsValid)
        {
            return SwapResponse()
                .WithErrorToast(preview.Error ?? "Cannot change to this plan")
                .Build();
        }

        return SwapView(SwapViews.TenantBilling._PlanChangeConfirmModal, new PlanChangeConfirmViewModel
        {
            Preview = preview,
            NewPlanId = planId
        });
    }

    [HttpPost("change-plan")]
    public async Task<IActionResult> ChangePlan([FromForm] Guid planId)
    {
        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue) return NotFound();

        var result = await _billingService.ChangePlanAsync(tenantId.Value, planId);
        if (!result.Success)
        {
            return SwapResponse()
                .WithErrorToast(result.Error ?? "Failed to change plan")
                .WithView(SwapViews.TenantBilling._ModalClose)
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
            .WithView(SwapViews.TenantBilling._ModalClose)
            .AlsoUpdate(SwapElements.BillingContent, SwapViews.TenantBilling._BillingContent, model)
            .WithSuccessToast("Plan changed successfully")
            .Build();
    }

    /// <summary>
    /// Callback endpoint after Paystack payment for plan change.
    /// Full-page GET request from Paystack redirect.
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? reference, [FromQuery] string? trxref)
    {
        var ref_ = reference ?? trxref;
        var slug = _tenantContext.Slug;

        if (string.IsNullOrEmpty(ref_))
        {
            return SwapView(SwapViews.TenantBilling.Callback, new { Success = false, Slug = slug,
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
            return SwapView(SwapViews.TenantBilling.Callback, new { Success = false, Slug = slug,
                ErrorMessage = "Could not verify your payment. Please contact support." });
        }

        return SwapView(SwapViews.TenantBilling.Callback, new { Success = true, Slug = slug,
            ErrorMessage = (string?)null });
    }

    [HttpGet("cancel-modal")]
    public IActionResult CancelModal()
    {
        var tenantName = _tenantContext.TenantName ?? "this workspace";
        return SwapView(SwapViews.TenantBilling._CancelConfirmModal, new CancelConfirmViewModel
        {
            TenantName = tenantName
        });
    }

    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel([FromForm] string confirmName)
    {
        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue) return NotFound();

        var tenantName = _tenantContext.TenantName ?? "this workspace";
        if (!string.Equals(confirmName?.Trim(), tenantName, StringComparison.OrdinalIgnoreCase))
        {
            return SwapResponse()
                .WithErrorToast("Confirmation name does not match")
                .Build();
        }

        var success = await _billingService.CancelSubscriptionAsync(tenantId.Value);
        if (!success)
        {
            return SwapResponse()
                .WithErrorToast("Failed to cancel subscription")
                .Build();
        }

        var model = await GetBillingModelAsync();
        return SwapResponse()
            .WithView(SwapViews.TenantBilling._ModalClose)
            .AlsoUpdate(SwapElements.BillingContent, SwapViews.TenantBilling._BillingContent, model)
            .WithWarningToast("Subscription cancelled")
            .Build();
    }

    [HttpPost("manage-subscription")]
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

    // ── Seat Management ──────────────────────────────────────────────────

    [HttpGet("seat-change-modal")]
    public async Task<IActionResult> SeatChangeModal()
    {
        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue) return NotFound();

        var sub = await _coreDb.Subscriptions
            .Include(s => s.Plan)
            .Where(s => s.TenantId == tenantId.Value && s.Status == SubscriptionStatus.Active)
            .FirstOrDefaultAsync();

        if (sub is null)
            return SwapResponse().WithErrorToast("No active subscription").Build();

        return SwapView("_SeatChangeModal", new
        {
            CurrentSeats = sub.Quantity,
            MaxSeats = sub.Plan.MaxUsers,
            PerSeatPrice = sub.Plan.PerSeatMonthlyPrice ?? 0m,
            Currency = sub.Plan.Currency ?? "ZAR"
        });
    }

    [HttpPost("preview-seat-change")]
    public async Task<IActionResult> PreviewSeatChange([FromForm] int seatCount)
    {
        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue) return NotFound();

        var preview = await _billingService.PreviewSeatChangeAsync(tenantId.Value, seatCount);
        if (!preview.IsValid)
        {
            return SwapResponse()
                .WithErrorToast(preview.Error ?? "Invalid seat count")
                .Build();
        }

        return SwapView("_SeatChangeConfirmModal", new SeatChangeConfirmViewModel
        {
            Preview = preview,
            NewSeatCount = seatCount
        });
    }

    [HttpPost("update-seats")]
    public async Task<IActionResult> UpdateSeats([FromForm] int seatCount)
    {
        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue) return NotFound();

        var result = await _billingService.UpdateSeatCountAsync(tenantId.Value, seatCount);
        if (!result.Success)
        {
            return SwapResponse()
                .WithErrorToast(result.Error ?? "Failed to update seats")
                .Build();
        }

        var model = await GetBillingModelAsync();
        return SwapResponse()
            .WithView(SwapViews.TenantBilling._ModalClose)
            .AlsoUpdate(SwapElements.BillingContent, SwapViews.TenantBilling._BillingContent, model)
            .WithSuccessToast($"Seats updated to {seatCount}")
            .Build();
    }

    // ── Discount Code ────────────────────────────────────────────────────

    [HttpPost("apply-discount")]
    public async Task<IActionResult> ApplyDiscount([FromForm] string code)
    {
        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue) return NotFound();

        if (string.IsNullOrWhiteSpace(code))
            return SwapResponse().WithErrorToast("Please enter a discount code").Build();

        var result = await _billingService.ApplyDiscountAsync(tenantId.Value, code.Trim());
        if (!result.Success)
        {
            return SwapResponse()
                .WithErrorToast(result.Error ?? "Invalid discount code")
                .Build();
        }

        var model = await GetBillingModelAsync();
        return SwapResponse()
            .AlsoUpdate(SwapElements.BillingContent, SwapViews.TenantBilling._BillingContent, model)
            .WithSuccessToast($"Discount applied: {result.DiscountName}")
            .Build();
    }

    // ── Invoice Detail ───────────────────────────────────────────────────

    [HttpGet("invoice/{invoiceId:guid}")]
    public async Task<IActionResult> InvoiceDetail(Guid invoiceId)
    {
        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue) return NotFound();

        var invoice = await _coreDb.Invoices
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.TenantId == tenantId.Value);

        if (invoice is null) return NotFound();

        var payments = await _coreDb.Payments
            .Where(p => p.InvoiceId == invoiceId)
            .OrderByDescending(p => p.TransactionDate)
            .ToListAsync();

        return SwapView("_InvoiceDetail", new InvoiceDetailViewModel
        {
            Invoice = invoice,
            LineItems = invoice.LineItems?.ToList() ?? [],
            Payments = payments
        });
    }

    // ── Add-ons ──────────────────────────────────────────────────────────

    [HttpGet("addons")]
    public async Task<IActionResult> AddOns()
    {
        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue) return NotFound();

        var available = await _addOnService.ListAvailableAsync(tenantId.Value);
        var active = await _addOnService.ListActiveAsync(tenantId.Value);

        return SwapView("_AddOns", new AddOnViewModel
        {
            AvailableAddOns = available,
            ActiveAddOns = active
        });
    }

    [HttpPost("subscribe-addon")]
    public async Task<IActionResult> SubscribeAddOn([FromForm] Guid addOnId)
    {
        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue) return NotFound();

        try
        {
            await _addOnService.SubscribeAsync(tenantId.Value, addOnId);
        }
        catch (InvalidOperationException ex)
        {
            return SwapResponse()
                .WithErrorToast(ex.Message)
                .Build();
        }

        var model = await GetBillingModelAsync();
        return SwapResponse()
            .AlsoUpdate(SwapElements.BillingContent, SwapViews.TenantBilling._BillingContent, model)
            .WithSuccessToast("Add-on activated")
            .Build();
    }

    [HttpPost("unsubscribe-addon")]
    public async Task<IActionResult> UnsubscribeAddOn([FromForm] Guid addOnId)
    {
        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue) return NotFound();

        await _addOnService.UnsubscribeAsync(tenantId.Value, addOnId);

        var model = await GetBillingModelAsync();
        return SwapResponse()
            .AlsoUpdate(SwapElements.BillingContent, SwapViews.TenantBilling._BillingContent, model)
            .WithWarningToast("Add-on removed")
            .Build();
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

        var activeAddOns = await _coreDb.TenantAddOns
            .Include(ta => ta.AddOn)
            .Where(ta => ta.TenantId == tenantId.Value && ta.DeactivatedAt == null)
            .ToListAsync();

        var activeDiscounts = await _coreDb.TenantDiscounts
            .Include(td => td.Discount)
            .Where(td => td.TenantId == tenantId.Value && td.IsActive)
            .ToListAsync();

        var creditBalance = await _creditService.GetBalanceAsync(tenantId.Value);

        var availableAddOns = await _coreDb.AddOns
            .Where(a => a.IsActive)
            .OrderBy(a => a.Name)
            .ToListAsync();

        return new BillingViewModel
        {
            PlanName = tenant.Plan.Name,
            PlanId = tenant.PlanId,
            MonthlyPrice = tenant.Plan.MonthlyPrice,
            AnnualPrice = tenant.Plan.AnnualPrice,
            Currency = tenant.Plan.Currency ?? "ZAR",
            SubscriptionStatus = tenant.ActiveSubscription?.Status,
            BillingCycle = tenant.ActiveSubscription?.BillingCycle,
            NextBillingDate = tenant.ActiveSubscription?.NextBillingDate,
            CancelledAt = tenant.ActiveSubscription?.CancelledAt,
            TrialEndsAt = tenant.ActiveSubscription?.TrialEndsAt,
            HasPaystackSubscription = !string.IsNullOrEmpty(tenant.ActiveSubscription?.PaystackSubscriptionCode),
            Invoices = invoices,
            BillingModel = tenant.Plan.BillingModel,
            CurrentSeats = tenant.ActiveSubscription?.Quantity ?? 1,
            MaxSeats = tenant.Plan.MaxUsers ?? 0,
            PerSeatPrice = tenant.Plan.PerSeatMonthlyPrice ?? 0m,
            ActiveAddOns = activeAddOns,
            AvailableAddOns = availableAddOns,
            ActiveDiscounts = activeDiscounts,
            CreditBalance = creditBalance
        };
    }
}
