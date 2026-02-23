using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using saas.Data.Audit;
using saas.Data.Core;
using saas.Modules.Billing.Entities;
using saas.Modules.Billing.Services;
using saas.Modules.SuperAdmin.Services;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.SuperAdmin.Controllers;

/// <summary>
/// Super admin controller for Discount CRUD, Add-On CRUD, tenant billing admin,
/// refund/credit operations, and webhook event viewer.
/// </summary>
[Authorize(Policy = "SuperAdmin")]
public class SuperAdminBillingController : SwapController
{
    private readonly CoreDbContext _db;
    private readonly IBillingService _billing;
    private readonly IAddOnService _addOnService;
    private readonly ICreditService _creditService;
    private readonly ISuperAdminAuditService _audit;
    private readonly ILogger<SuperAdminBillingController> _logger;

    public SuperAdminBillingController(
        CoreDbContext db,
        IBillingService billing,
        IAddOnService addOnService,
        ICreditService creditService,
        ISuperAdminAuditService audit,
        ILogger<SuperAdminBillingController> logger)
    {
        _db = db;
        _billing = billing;
        _addOnService = addOnService;
        _creditService = creditService;
        _audit = audit;
        _logger = logger;
    }

    // ── Discount Management ─────────────────────────────────────────────

    [HttpGet("/super-admin/discounts")]
    public async Task<IActionResult> Discounts()
    {
        var discounts = await _db.Discounts
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();
        return SwapView(discounts);
    }

    [HttpGet("/super-admin/discounts/edit")]
    public async Task<IActionResult> EditDiscount(Guid? id)
    {
        DiscountEditModel model;
        if (id.HasValue)
        {
            var discount = await _db.Discounts.FindAsync(id.Value);
            if (discount is null) return NotFound();
            model = new DiscountEditModel
            {
                Id = discount.Id,
                Name = discount.Name,
                Code = discount.Code,
                Type = discount.Type,
                Value = discount.Value,
                DurationInCycles = discount.DurationInCycles,
                MaxRedemptions = discount.MaxRedemptions,
                ValidFrom = discount.ValidFrom,
                ValidUntil = discount.ValidUntil,
                IsActive = discount.IsActive
            };
        }
        else
        {
            model = new DiscountEditModel();
        }
        return SwapView("_DiscountEditModal", model);
    }

    [HttpPost("/super-admin/discounts")]
    public async Task<IActionResult> SaveDiscount(DiscountEditModel model)
    {
        if (!ModelState.IsValid)
            return SwapView("_DiscountEditModal", model);

        Discount discount;
        if (model.Id.HasValue)
        {
            discount = await _db.Discounts.FindAsync(model.Id.Value) ?? new Discount();
        }
        else
        {
            discount = new Discount { Id = Guid.NewGuid() };
            _db.Discounts.Add(discount);
        }

        discount.Name = model.Name;
        discount.Code = model.Code.Trim().ToUpperInvariant();
        discount.Type = model.Type;
        discount.Value = model.Value;
        discount.DurationInCycles = model.DurationInCycles;
        discount.MaxRedemptions = model.MaxRedemptions;
        discount.ValidFrom = model.ValidFrom;
        discount.ValidUntil = model.ValidUntil;
        discount.IsActive = model.IsActive;

        await _db.SaveChangesAsync();
        await _audit.LogAsync(
            model.Id.HasValue ? "Updated" : "Created", "Discount", discount.Code,
            $"Discount '{discount.Name}' ({discount.Code}) — {discount.Type} {discount.Value}");

        var discounts = await _db.Discounts.OrderByDescending(d => d.CreatedAt).ToListAsync();

        return SwapResponse()
            .WithView(SwapViews.SuperAdminBilling._ModalClose)
            .AlsoUpdate(SwapElements.DiscountList, SwapViews.SuperAdminBilling._DiscountList, discounts)
            .WithSavedToast("Discount")
            .Build();
    }

    [HttpPost("/super-admin/discounts/delete")]
    public async Task<IActionResult> DeleteDiscount([FromForm] Guid id)
    {
        var discount = await _db.Discounts.FindAsync(id);
        if (discount is not null)
        {
            discount.IsActive = false;
            await _db.SaveChangesAsync();
            await _audit.LogAsync("Deactivated", "Discount", discount.Code, $"Deactivated discount '{discount.Name}'");
        }

        var discounts = await _db.Discounts.OrderByDescending(d => d.CreatedAt).ToListAsync();
        return SwapResponse()
            .AlsoUpdate(SwapElements.DiscountList, SwapViews.SuperAdminBilling._DiscountList, discounts)
            .WithWarningToast("Discount deactivated")
            .Build();
    }

    // ── Add-On Management ───────────────────────────────────────────────

    [HttpGet("/super-admin/addons")]
    public async Task<IActionResult> AddOns()
    {
        var addons = await _db.AddOns.OrderBy(a => a.SortOrder).ToListAsync();
        return SwapView(addons);
    }

    [HttpGet("/super-admin/addons/edit")]
    public async Task<IActionResult> EditAddOn(Guid? id)
    {
        AddOnEditModel model;
        if (id.HasValue)
        {
            var addon = await _db.AddOns.FindAsync(id.Value);
            if (addon is null) return NotFound();
            model = new AddOnEditModel
            {
                Id = addon.Id,
                Name = addon.Name,
                Slug = addon.Slug,
                Description = addon.Description,
                Price = addon.Price,
                Currency = addon.Currency,
                BillingInterval = addon.BillingInterval,
                SortOrder = addon.SortOrder,
                IsActive = addon.IsActive
            };
        }
        else
        {
            model = new AddOnEditModel();
        }
        return SwapView("_AddOnEditModal", model);
    }

    [HttpPost("/super-admin/addons")]
    public async Task<IActionResult> SaveAddOn(AddOnEditModel model)
    {
        if (!ModelState.IsValid)
            return SwapView("_AddOnEditModal", model);

        AddOn addon;
        if (model.Id.HasValue)
        {
            addon = await _db.AddOns.FindAsync(model.Id.Value) ?? new AddOn();
        }
        else
        {
            addon = new AddOn { Id = Guid.NewGuid() };
            _db.AddOns.Add(addon);
        }

        addon.Name = model.Name;
        addon.Slug = model.Slug;
        addon.Description = model.Description;
        addon.Price = model.Price;
        addon.Currency = model.Currency;
        addon.BillingInterval = model.BillingInterval;
        addon.SortOrder = model.SortOrder;
        addon.IsActive = model.IsActive;

        await _db.SaveChangesAsync();
        await _audit.LogAsync(
            model.Id.HasValue ? "Updated" : "Created", "AddOn", addon.Slug,
            $"Add-on '{addon.Name}' ({addon.Slug}) — {addon.Currency} {addon.Price}");

        var addons = await _db.AddOns.OrderBy(a => a.SortOrder).ToListAsync();

        return SwapResponse()
            .WithView(SwapViews.SuperAdminBilling._ModalClose)
            .AlsoUpdate(SwapElements.AddonList, SwapViews.SuperAdminBilling._AddOnList, addons)
            .WithSavedToast("Add-on")
            .Build();
    }

    // ── Tenant Billing Admin ────────────────────────────────────────────

    [HttpGet("/super-admin/tenants/{tenantId:guid}/billing")]
    public async Task<IActionResult> TenantBillingDetail(Guid tenantId)
    {
        var tenant = await _db.Tenants
            .Include(t => t.Plan)
            .Include(t => t.ActiveSubscription)
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant is null) return NotFound();

        var invoices = await _db.Invoices
            .Where(i => i.TenantId == tenantId)
            .OrderByDescending(i => i.IssuedDate)
            .Take(20)
            .ToListAsync();

        var payments = await _db.Payments
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.TransactionDate)
            .Take(20)
            .ToListAsync();

        var credits = await _creditService.GetLedgerAsync(tenantId);
        var creditBalance = await _creditService.GetBalanceAsync(tenantId);

        var discounts = await _db.TenantDiscounts
            .Include(td => td.Discount)
            .Where(td => td.TenantId == tenantId)
            .ToListAsync();

        var addons = await _db.TenantAddOns
            .Include(ta => ta.AddOn)
            .Where(ta => ta.TenantId == tenantId && ta.DeactivatedAt == null)
            .ToListAsync();

        return SwapView("_TenantBillingDetail", new TenantBillingAdminModel
        {
            TenantId = tenantId,
            TenantName = tenant.Name,
            PlanName = tenant.Plan.Name,
            SubscriptionStatus = tenant.ActiveSubscription?.Status,
            Invoices = invoices,
            Payments = payments,
            Credits = credits,
            CreditBalance = creditBalance,
            Discounts = discounts,
            AddOns = addons
        });
    }

    [HttpPost("/super-admin/tenants/{tenantId:guid}/add-credit")]
    public async Task<IActionResult> AddCredit(Guid tenantId, [FromForm] decimal amount, [FromForm] string? reason)
    {
        if (amount <= 0)
            return SwapResponse().WithErrorToast("Amount must be positive").Build();

        await _creditService.AddCreditAsync(tenantId, amount, CreditReason.Manual, reason);
        await _audit.LogAsync("AddCredit", "Tenant", tenantId.ToString(),
            $"Added R{amount:N2} credit to tenant. Reason: {reason}");

        return SwapResponse()
            .WithSuccessToast($"R{amount:N2} credit added")
            .Build();
    }

    [HttpPost("/super-admin/tenants/{tenantId:guid}/refund")]
    public async Task<IActionResult> RefundPayment(Guid tenantId, [FromForm] Guid paymentId, [FromForm] decimal? amount)
    {
        var result = await _billing.IssueRefundAsync(paymentId, amount);
        if (!result.Success)
            return SwapResponse().WithErrorToast(result.Error ?? "Refund failed").Build();

        await _audit.LogAsync("Refund", "Payment", paymentId.ToString(),
            $"Refunded R{result.AmountRefunded:N2} for payment on tenant {tenantId}");

        return SwapResponse()
            .WithSuccessToast($"Refund of R{result.AmountRefunded:N2} processed")
            .Build();
    }

    // ── Webhook Event Viewer ────────────────────────────────────────────

    [HttpGet("/super-admin/webhooks")]
    public async Task<IActionResult> WebhookEvents()
    {
        var events = await _db.WebhookEvents
            .OrderByDescending(w => w.ReceivedAt)
            .Take(50)
            .ToListAsync();
        return SwapView(events);
    }

    [HttpGet("/super-admin/webhooks/{id:guid}")]
    public async Task<IActionResult> WebhookEventDetail(Guid id)
    {
        var evt = await _db.WebhookEvents.FindAsync(id);
        if (evt is null) return NotFound();
        return SwapView("_WebhookEventDetail", evt);
    }
}

// ── View Models ────────────────────────────────────────────────────────

public class DiscountEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DiscountType Type { get; set; }
    public decimal Value { get; set; }
    public int? DurationInCycles { get; set; }
    public int? MaxRedemptions { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public bool IsActive { get; set; } = true;
}

public class AddOnEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "ZAR";
    public AddOnInterval BillingInterval { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class TenantBillingAdminModel
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public SubscriptionStatus? SubscriptionStatus { get; set; }
    public List<Invoice> Invoices { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
    public List<TenantCredit> Credits { get; set; } = [];
    public decimal CreditBalance { get; set; }
    public List<TenantDiscount> Discounts { get; set; } = [];
    public List<TenantAddOn> AddOns { get; set; } = [];
}
