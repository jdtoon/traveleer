using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Modules.Billing.Entities;

namespace saas.Modules.Billing.Services;

public interface IAddOnService
{
    Task<TenantAddOn> SubscribeAsync(Guid tenantId, Guid addOnId, int quantity = 1);
    Task UnsubscribeAsync(Guid tenantId, Guid addOnId);
    Task<List<AddOn>> ListAvailableAsync(Guid tenantId);
    Task<List<TenantAddOn>> ListActiveAsync(Guid tenantId);
}

public class AddOnService : IAddOnService
{
    private readonly CoreDbContext _db;
    private readonly ICreditService _creditService;
    private readonly IInvoiceEngine _invoiceEngine;
    private readonly ILogger<AddOnService> _logger;

    public AddOnService(
        CoreDbContext db,
        ICreditService creditService,
        IInvoiceEngine invoiceEngine,
        ILogger<AddOnService> logger)
    {
        _db = db;
        _creditService = creditService;
        _invoiceEngine = invoiceEngine;
        _logger = logger;
    }

    public async Task<TenantAddOn> SubscribeAsync(Guid tenantId, Guid addOnId, int quantity = 1)
    {
        var addOn = await _db.AddOns.FindAsync(addOnId)
            ?? throw new InvalidOperationException("Add-on not found");

        if (!addOn.IsActive)
            throw new InvalidOperationException("Add-on is not available");

        // Check if already subscribed
        var existing = await _db.TenantAddOns
            .FirstOrDefaultAsync(ta => ta.TenantId == tenantId && ta.AddOnId == addOnId && ta.DeactivatedAt == null);

        if (existing is not null)
            throw new InvalidOperationException("Already subscribed to this add-on");

        var tenantAddOn = new TenantAddOn
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AddOnId = addOnId,
            Quantity = quantity,
            ActivatedAt = DateTime.UtcNow
        };
        _db.TenantAddOns.Add(tenantAddOn);

        // For one-off add-ons, generate invoice immediately
        if (addOn.BillingInterval == AddOnInterval.OneOff)
        {
            await _invoiceEngine.GenerateOneOffInvoiceAsync(tenantId,
                $"{addOn.Name} (one-time)", addOn.Price * quantity);
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Tenant {TenantId} subscribed to add-on {AddOnName}", tenantId, addOn.Name);
        return tenantAddOn;
    }

    public async Task UnsubscribeAsync(Guid tenantId, Guid addOnId)
    {
        var tenantAddOn = await _db.TenantAddOns
            .Include(ta => ta.AddOn)
            .FirstOrDefaultAsync(ta => ta.TenantId == tenantId && ta.AddOnId == addOnId && ta.DeactivatedAt == null);

        if (tenantAddOn is null)
            throw new InvalidOperationException("Add-on subscription not found");

        tenantAddOn.DeactivatedAt = DateTime.UtcNow;

        // Issue proportional credit for recurring add-ons
        if (tenantAddOn.AddOn.BillingInterval != AddOnInterval.OneOff)
        {
            // Simple: credit remaining fraction of current period
            var daysActive = (DateTime.UtcNow - tenantAddOn.ActivatedAt).TotalDays;
            var daysInPeriod = tenantAddOn.AddOn.BillingInterval == AddOnInterval.Annual ? 365 : 30;
            var daysSinceLastBilling = daysActive % daysInPeriod;
            var remainingDays = daysInPeriod - daysSinceLastBilling;
            var credit = Math.Round(tenantAddOn.AddOn.Price * tenantAddOn.Quantity * (decimal)(remainingDays / daysInPeriod), 2);

            if (credit > 0)
            {
                await _creditService.AddCreditAsync(tenantId, credit, CreditReason.PlanChangeCredit,
                    $"Credit for unused {tenantAddOn.AddOn.Name} add-on");
            }
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Tenant {TenantId} unsubscribed from add-on {AddOnName}", tenantId, tenantAddOn.AddOn.Name);
    }

    public async Task<List<AddOn>> ListAvailableAsync(Guid tenantId)
    {
        var subscribedIds = await _db.TenantAddOns
            .Where(ta => ta.TenantId == tenantId && ta.DeactivatedAt == null)
            .Select(ta => ta.AddOnId)
            .ToListAsync();

        return await _db.AddOns
            .Where(a => a.IsActive && !subscribedIds.Contains(a.Id))
            .OrderBy(a => a.SortOrder)
            .ToListAsync();
    }

    public async Task<List<TenantAddOn>> ListActiveAsync(Guid tenantId)
    {
        return await _db.TenantAddOns
            .Where(ta => ta.TenantId == tenantId && ta.DeactivatedAt == null)
            .Include(ta => ta.AddOn)
            .ToListAsync();
    }
}
