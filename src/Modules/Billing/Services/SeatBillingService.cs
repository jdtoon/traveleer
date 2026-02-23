using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using saas.Data.Core;
using saas.Modules.Billing.Entities;
using saas.Shared;

namespace saas.Modules.Billing.Services;

public interface ISeatBillingService
{
    Task<SeatChangeResult> UpdateSeatsAsync(Guid tenantId, int newCount);
    Task<SeatChangePreview> PreviewSeatChangeAsync(Guid tenantId, int newCount);
}

public class SeatBillingService : ISeatBillingService
{
    private readonly CoreDbContext _db;
    private readonly ICreditService _creditService;
    private readonly IInvoiceEngine _invoiceEngine;
    private readonly BillingOptions _options;
    private readonly ILogger<SeatBillingService> _logger;

    public SeatBillingService(
        CoreDbContext db,
        ICreditService creditService,
        IInvoiceEngine invoiceEngine,
        IOptions<BillingOptions> options,
        ILogger<SeatBillingService> logger)
    {
        _db = db;
        _creditService = creditService;
        _invoiceEngine = invoiceEngine;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SeatChangeResult> UpdateSeatsAsync(Guid tenantId, int newCount)
    {
        if (newCount < 1)
            return new SeatChangeResult(false, Error: "Seat count must be at least 1");

        var sub = await _db.Subscriptions
            .Include(s => s.Plan)
            .ThenInclude(p => p.PricingTiers)
            .Where(s => s.TenantId == tenantId && s.Status == SubscriptionStatus.Active)
            .FirstOrDefaultAsync();

        if (sub is null)
            return new SeatChangeResult(false, Error: "No active subscription");

        var plan = sub.Plan;
        if (plan.BillingModel != BillingModel.PerSeat && plan.BillingModel != BillingModel.Hybrid)
            return new SeatChangeResult(false, Error: "Plan does not support per-seat billing");

        if (plan.MaxUsers.HasValue && newCount > plan.MaxUsers.Value)
            return new SeatChangeResult(false, Error: $"Maximum seats for this plan is {plan.MaxUsers}");

        var previousSeats = sub.Quantity;
        var seatDiff = newCount - previousSeats;
        if (seatDiff == 0)
            return new SeatChangeResult(true, PreviousSeats: previousSeats, NewSeats: newCount);

        var pricePerSeat = sub.BillingCycle == BillingCycle.Annual
            ? (plan.PerSeatAnnualPrice ?? 0)
            : (plan.PerSeatMonthlyPrice ?? 0);

        // Calculate proration
        var now = DateTime.UtcNow;
        int totalCycleDays = sub.BillingCycle == BillingCycle.Annual ? 365 : 30;
        int remainingDays = totalCycleDays;

        if (sub.NextBillingDate.HasValue)
        {
            remainingDays = Math.Max(0, (int)(sub.NextBillingDate.Value - now).TotalDays);
            if (sub.StartDate > DateTime.MinValue)
                totalCycleDays = Math.Max(1, (int)(sub.NextBillingDate.Value - sub.StartDate).TotalDays);
        }

        var proratedAmount = Math.Round(Math.Abs(seatDiff) * pricePerSeat * ((decimal)remainingDays / totalCycleDays), 2);

        decimal amountCharged = 0;
        decimal creditIssued = 0;

        if (seatDiff > 0)
        {
            // Increasing seats — create prorated invoice
            amountCharged = proratedAmount;
            if (proratedAmount > 0)
            {
                var lineItems = new List<InvoiceLineItem>
                {
                    new()
                    {
                        Type = LineItemType.Proration,
                        Description = $"Prorated charge for {seatDiff} additional seat(s) ({remainingDays} days remaining)",
                        Quantity = seatDiff,
                        UnitPrice = Math.Round(pricePerSeat * ((decimal)remainingDays / totalCycleDays), 2),
                        Amount = proratedAmount,
                        SortOrder = 0
                    }
                };
                await _invoiceEngine.GenerateProrationInvoiceAsync(tenantId,
                    $"Seat change: {previousSeats} → {newCount}", lineItems);
            }
        }
        else
        {
            // Decreasing seats — issue credit
            creditIssued = proratedAmount;
            if (proratedAmount > 0)
            {
                await _creditService.AddCreditAsync(tenantId, proratedAmount, CreditReason.PlanChangeCredit,
                    $"Credit for {Math.Abs(seatDiff)} removed seat(s)");
            }
        }

        sub.Quantity = newCount;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Seat count changed for tenant {TenantId}: {Old} → {New}", tenantId, previousSeats, newCount);
        return new SeatChangeResult(true, PreviousSeats: previousSeats, NewSeats: newCount,
            AmountCharged: amountCharged, CreditIssued: creditIssued);
    }

    public async Task<SeatChangePreview> PreviewSeatChangeAsync(Guid tenantId, int newCount)
    {
        if (newCount < 1)
            return new SeatChangePreview(false, Error: "Seat count must be at least 1");

        var sub = await _db.Subscriptions
            .Include(s => s.Plan)
            .Where(s => s.TenantId == tenantId && s.Status == SubscriptionStatus.Active)
            .FirstOrDefaultAsync();

        if (sub is null)
            return new SeatChangePreview(false, Error: "No active subscription");

        var plan = sub.Plan;
        if (plan.MaxUsers.HasValue && newCount > plan.MaxUsers.Value)
            return new SeatChangePreview(false, Error: $"Maximum seats for this plan is {plan.MaxUsers}");

        var seatDiff = newCount - sub.Quantity;
        var pricePerSeat = sub.BillingCycle == BillingCycle.Annual
            ? (plan.PerSeatAnnualPrice ?? 0)
            : (plan.PerSeatMonthlyPrice ?? 0);

        var now = DateTime.UtcNow;
        int totalCycleDays = sub.BillingCycle == BillingCycle.Annual ? 365 : 30;
        int remainingDays = totalCycleDays;

        if (sub.NextBillingDate.HasValue)
        {
            remainingDays = Math.Max(0, (int)(sub.NextBillingDate.Value - now).TotalDays);
            if (sub.StartDate > DateTime.MinValue)
                totalCycleDays = Math.Max(1, (int)(sub.NextBillingDate.Value - sub.StartDate).TotalDays);
        }

        var proratedAmount = Math.Round(Math.Abs(seatDiff) * pricePerSeat * ((decimal)remainingDays / totalCycleDays), 2);

        return new SeatChangePreview(
            IsValid: true,
            CurrentSeats: sub.Quantity,
            NewSeats: newCount,
            SeatDifference: seatDiff,
            PricePerSeat: pricePerSeat,
            RemainingDays: remainingDays,
            TotalCycleDays: totalCycleDays,
            ProratedAmount: proratedAmount,
            IsIncrease: seatDiff > 0
        );
    }
}
