using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using saas.Data.Core;
using saas.Modules.Billing.Entities;
using saas.Shared;

namespace saas.Modules.Billing.Services;

/// <summary>
/// Calculates variable charges (seats + usage) and collects them via charge_authorization.
/// This is the generic orchestrator that bridges seat billing, usage billing, and Paystack charging.
/// Downstream apps configure behavior via BillingOptions (plan model, seat pricing, usage metrics).
/// </summary>
public interface IVariableChargeService
{
    /// <summary>
    /// Calculate seat + usage charges for a tenant's billing period without charging.
    /// </summary>
    Task<VariableChargeBreakdown> CalculateVariableChargesAsync(Guid tenantId, DateTime periodStart, DateTime periodEnd);

    /// <summary>
    /// Calculate, invoice, and charge the variable portion for a tenant.
    /// Used at renewal time and by the usage billing job.
    /// </summary>
    Task<ChargeResult> ChargeVariableAsync(Guid tenantId);

    /// <summary>
    /// Charge an existing unpaid/overdue invoice via charge_authorization.
    /// Used by dunning retries and 2FA completion flows.
    /// </summary>
    Task<ChargeResult> ChargeInvoiceAsync(Guid tenantId, Invoice invoice);
}

public class VariableChargeService : IVariableChargeService
{
    private readonly CoreDbContext _db;
    private readonly BillingOptions _options;
    private readonly IUsageBillingService _usageBilling;
    private readonly IInvoiceEngine _invoiceEngine;
    private readonly PaystackClient _paystack;
    private readonly IDunningService _dunning;
    private readonly ILogger<VariableChargeService> _logger;

    public VariableChargeService(
        CoreDbContext db,
        IOptions<BillingOptions> options,
        IUsageBillingService usageBilling,
        IInvoiceEngine invoiceEngine,
        PaystackClient paystack,
        IDunningService dunning,
        ILogger<VariableChargeService> logger)
    {
        _db = db;
        _options = options.Value;
        _usageBilling = usageBilling;
        _invoiceEngine = invoiceEngine;
        _paystack = paystack;
        _dunning = dunning;
        _logger = logger;
    }

    public async Task<VariableChargeBreakdown> CalculateVariableChargesAsync(
        Guid tenantId, DateTime periodStart, DateTime periodEnd)
    {
        var subscription = await _db.Subscriptions
            .Include(s => s.Plan)
            .ThenInclude(p => p.PricingTiers)
            .Where(s => s.TenantId == tenantId
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue))
            .FirstOrDefaultAsync();

        if (subscription is null)
            return VariableChargeBreakdown.Empty;

        var plan = subscription.Plan;
        var lineItems = new List<InvoiceLineItem>();
        decimal seatChargeTotal = 0;
        decimal usageChargeTotal = 0;
        var sortOrder = 0;

        // ── 1. Seat / entity charges ─────────────────────────────────
        if (_options.Features.PerSeatBilling
            && (plan.BillingModel == BillingModel.PerSeat || plan.BillingModel == BillingModel.Hybrid))
        {
            var includedSeats = plan.IncludedSeats ?? 0;
            var extraSeats = Math.Max(0, subscription.Quantity - includedSeats);

            if (extraSeats > 0)
            {
                var seatPrice = subscription.BillingCycle == BillingCycle.Annual
                    ? (plan.PerSeatAnnualPrice ?? 0)
                    : (plan.PerSeatMonthlyPrice ?? 0);

                // Use tiered pricing if configured
                if (plan.PricingTiers.Count > 0)
                    seatPrice = CalculateTieredSeatPrice(plan, subscription.Quantity) / subscription.Quantity;

                if (seatPrice > 0)
                {
                    var seatAmount = extraSeats * seatPrice;
                    seatChargeTotal = seatAmount;

                    lineItems.Add(new InvoiceLineItem
                    {
                        Id = Guid.NewGuid(),
                        Type = LineItemType.Seat,
                        Description = $"Additional seats ({extraSeats} × {seatPrice:C})",
                        Quantity = extraSeats,
                        UnitPrice = seatPrice,
                        Amount = seatAmount,
                        SortOrder = sortOrder++
                    });
                }
            }
        }

        // ── 2. Usage charges ─────────────────────────────────────────
        if (_options.Features.UsageBilling)
        {
            var usageCharges = await _usageBilling.CalculateUsageChargesAsync(tenantId, periodStart, periodEnd);

            foreach (var (metric, charge) in usageCharges)
            {
                if (charge.TotalCharge <= 0) continue;

                usageChargeTotal += charge.TotalCharge;

                lineItems.Add(new InvoiceLineItem
                {
                    Id = Guid.NewGuid(),
                    Type = LineItemType.UsageCharge,
                    Description = $"{charge.MetricDisplayName} ({charge.OverageQuantity} × {charge.PricePerUnit:C})",
                    Quantity = (int)charge.OverageQuantity,
                    UnitPrice = charge.PricePerUnit,
                    Amount = charge.TotalCharge,
                    UsageMetric = metric,
                    SortOrder = sortOrder++
                });
            }
        }

        return new VariableChargeBreakdown(
            LineItems: lineItems,
            SeatChargeTotal: seatChargeTotal,
            UsageChargeTotal: usageChargeTotal,
            Total: seatChargeTotal + usageChargeTotal);
    }

    public async Task<ChargeResult> ChargeVariableAsync(Guid tenantId)
    {
        var subscription = await _db.Subscriptions
            .Include(s => s.Plan)
            .Where(s => s.TenantId == tenantId
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue))
            .FirstOrDefaultAsync();

        if (subscription is null)
            return new ChargeResult(false, Error: "No active subscription");

        // Determine the billing period that just ended
        var now = DateTime.UtcNow;
        var periodEnd = now;
        var periodStart = subscription.BillingCycle == BillingCycle.Annual
            ? periodEnd.AddYears(-1)
            : new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);
        var periodEndBound = periodStart.AddMonths(1).AddTicks(-1);

        var breakdown = await CalculateVariableChargesAsync(tenantId, periodStart, periodEndBound);

        if (breakdown.Total <= 0)
        {
            _logger.LogInformation("No variable charges for tenant {TenantId}", tenantId);
            return new ChargeResult(true); // Nothing to charge — success with zero
        }

        // Generate variable charge invoice
        var invoice = await _invoiceEngine.GenerateProrationInvoiceAsync(
            tenantId,
            $"Variable charges for {periodStart:MMM yyyy}",
            breakdown.LineItems);

        _logger.LogInformation(
            "Generated variable charge invoice {Number} for tenant {TenantId}: seats={SeatTotal:C}, usage={UsageTotal:C}, total={Total:C}",
            invoice.InvoiceNumber, tenantId, breakdown.SeatChargeTotal, breakdown.UsageChargeTotal, breakdown.Total);

        return await ChargeInvoiceAsync(tenantId, invoice);
    }

    public async Task<ChargeResult> ChargeInvoiceAsync(Guid tenantId, Invoice invoice)
    {
        var subscription = await _db.Subscriptions
            .Where(s => s.TenantId == tenantId
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue))
            .FirstOrDefaultAsync();

        if (subscription is null || string.IsNullOrEmpty(subscription.PaystackAuthorizationCode))
        {
            _logger.LogWarning("Cannot charge variable for tenant {TenantId}: no authorization", tenantId);
            return new ChargeResult(false, Error: "No stored payment authorization");
        }

        var amount = invoice.Total;
        if (amount <= 0)
            return new ChargeResult(true, InvoiceId: invoice.Id);

        try
        {
            var chargeRef = $"VAR-{invoice.Id:N}";
            var result = await _paystack.ChargeAuthorizationAsync(new Billing.DTOs.PaystackChargeAuthorizationRequest
            {
                AuthorizationCode = subscription.PaystackAuthorizationCode,
                Email = subscription.PaystackAuthorizationEmail ?? "",
                Amount = (int)(amount * 100), // Convert to kobo/cents
                Reference = chargeRef,
                Currency = "ZAR",
                Metadata = new Dictionary<string, object>
                {
                    ["tenant_id"] = tenantId.ToString(),
                    ["charge_type"] = "variable",
                    ["invoice_id"] = invoice.Id.ToString()
                }
            });

            if (result is null)
            {
                _logger.LogError("Paystack charge_authorization returned null for tenant {TenantId}", tenantId);
                await _dunning.OnPaymentFailedAsync(tenantId, invoice.Id);
                return new ChargeResult(false, Error: "Payment gateway error");
            }

            // Handle 2FA challenge — user must complete authorization at redirect URL
            if (result.Paused && !string.IsNullOrEmpty(result.AuthorizationUrl))
            {
                invoice.PaystackReference = chargeRef;
                await _db.SaveChangesAsync();

                _logger.LogInformation(
                    "Variable charge for tenant {TenantId} requires 2FA: {Url}",
                    tenantId, result.AuthorizationUrl);

                return new ChargeResult(true, InvoiceId: invoice.Id, PaymentUrl: result.AuthorizationUrl);
            }

            if (result.Status == "success")
            {
                var payment = new Payment
                {
                    TenantId = tenantId,
                    InvoiceId = invoice.Id,
                    Amount = amount,
                    Currency = "ZAR",
                    Status = PaymentStatus.Success,
                    PaystackReference = chargeRef,
                    GatewayResponse = result.GatewayResponse,
                    TransactionDate = DateTime.UtcNow
                };
                _db.Payments.Add(payment);

                invoice.Status = InvoiceStatus.Paid;
                invoice.PaidDate = DateTime.UtcNow;
                invoice.PaystackReference = chargeRef;
                await _db.SaveChangesAsync();

                _logger.LogInformation(
                    "Variable charge of {Amount:C} collected for tenant {TenantId}, invoice {Number}",
                    amount, tenantId, invoice.InvoiceNumber);

                return new ChargeResult(true, InvoiceId: invoice.Id, PaymentId: payment.Id);
            }

            // Charge failed — enter dunning
            _logger.LogWarning(
                "Variable charge failed for tenant {TenantId}: {Response}",
                tenantId, result.GatewayResponse);

            await _dunning.OnPaymentFailedAsync(tenantId, invoice.Id);
            return new ChargeResult(false, InvoiceId: invoice.Id, Error: result.GatewayResponse ?? "Charge failed");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Paystack charge_authorization HTTP error for tenant {TenantId}", tenantId);
            await _dunning.OnPaymentFailedAsync(tenantId, invoice.Id);
            return new ChargeResult(false, InvoiceId: invoice.Id, Error: "Payment gateway error");
        }
    }

    // ── Tiered pricing helper (mirrors InvoiceEngine logic) ─────────

    private static decimal CalculateTieredSeatPrice(Plan plan, int seatCount)
    {
        var tiers = plan.PricingTiers.OrderBy(t => t.MinUnits).ToList();
        if (tiers.Count == 0) return seatCount * (plan.PerSeatMonthlyPrice ?? 0);

        decimal total = 0;
        int remaining = seatCount;

        foreach (var tier in tiers)
        {
            if (remaining <= 0) break;

            var tierMax = tier.MaxUnits ?? int.MaxValue;
            var tierUnits = Math.Min(remaining, tierMax - tier.MinUnits + 1);
            total += tierUnits * tier.PricePerUnit;
            remaining -= tierUnits;
        }

        return total;
    }
}

/// <summary>
/// No-op implementation used when Paystack is not configured (e.g. Mock billing mode).
/// </summary>
public class NullVariableChargeService : IVariableChargeService
{
    public Task<VariableChargeBreakdown> CalculateVariableChargesAsync(Guid tenantId, DateTime periodStart, DateTime periodEnd)
        => Task.FromResult(VariableChargeBreakdown.Empty);

    public Task<ChargeResult> ChargeVariableAsync(Guid tenantId)
        => Task.FromResult(new ChargeResult(false, Error: "Variable charging is not available in this billing mode."));

    public Task<ChargeResult> ChargeInvoiceAsync(Guid tenantId, Invoice invoice)
        => Task.FromResult(new ChargeResult(false, Error: "Variable charging is not available in this billing mode."));
}

/// <summary>
/// Breakdown of variable charges for a billing period.
/// Contains pre-built invoice line items ready for invoice generation.
/// </summary>
public record VariableChargeBreakdown(
    List<InvoiceLineItem> LineItems,
    decimal SeatChargeTotal,
    decimal UsageChargeTotal,
    decimal Total)
{
    public static VariableChargeBreakdown Empty => new([], 0, 0, 0);
    public bool HasCharges => Total > 0;
}
