using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using saas.Data.Core;
using saas.Modules.Billing.Entities;
using saas.Shared;

namespace saas.Modules.Billing.Services;

public interface IUsageBillingService : IUsageMeteringService
{
    Task<Dictionary<string, UsageChargeLine>> CalculateUsageChargesAsync(Guid tenantId, DateTime periodStart, DateTime periodEnd);
    Task<UsageBillingResult> ProcessEndOfPeriodAsync(Guid tenantId);
}

public class UsageBillingService : IUsageBillingService
{
    private readonly CoreDbContext _db;
    private readonly BillingOptions _options;
    private readonly IInvoiceEngine _invoiceEngine;
    private readonly ILogger<UsageBillingService> _logger;

    public UsageBillingService(
        CoreDbContext db,
        IOptions<BillingOptions> options,
        IInvoiceEngine invoiceEngine,
        ILogger<UsageBillingService> logger)
    {
        _db = db;
        _options = options.Value;
        _invoiceEngine = invoiceEngine;
        _logger = logger;
    }

    // ── IUsageMeteringService implementation ──

    public async Task RecordUsageAsync(Guid tenantId, string metric, long quantity = 1)
    {
        var now = DateTime.UtcNow;
        var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1).AddTicks(-1);

        var existing = await _db.UsageRecords
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Metric == metric && r.PeriodStart == periodStart);

        if (existing is not null)
        {
            existing.Quantity += quantity;
            existing.UpdatedAt = now;
        }
        else
        {
            _db.UsageRecords.Add(new UsageRecord
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Metric = metric,
                Quantity = quantity,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                CreatedAt = now
            });
        }

        await _db.SaveChangesAsync();
    }

    public async Task<long> GetCurrentPeriodUsageAsync(Guid tenantId, string metric)
    {
        var periodStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return await _db.UsageRecords
            .Where(r => r.TenantId == tenantId && r.Metric == metric && r.PeriodStart == periodStart)
            .Select(r => r.Quantity)
            .FirstOrDefaultAsync();
    }

    public async Task<List<UsageSummary>> GetUsageSummaryAsync(Guid tenantId, DateTime? from = null, DateTime? to = null)
    {
        var query = _db.UsageRecords.Where(r => r.TenantId == tenantId);
        if (from.HasValue) query = query.Where(r => r.PeriodStart >= from.Value);
        if (to.HasValue) query = query.Where(r => r.PeriodEnd <= to.Value);

        return await query
            .GroupBy(r => r.Metric)
            .Select(g => new UsageSummary
            {
                Metric = g.Key,
                TotalQuantity = g.Sum(r => r.Quantity),
                Periods = g.OrderByDescending(r => r.PeriodStart)
                    .Select(r => new UsagePeriod
                    {
                        PeriodStart = r.PeriodStart,
                        PeriodEnd = r.PeriodEnd,
                        Quantity = r.Quantity
                    }).ToList()
            })
            .ToListAsync();
    }

    // ── Usage billing ──

    public async Task<Dictionary<string, UsageChargeLine>> CalculateUsageChargesAsync(
        Guid tenantId, DateTime periodStart, DateTime periodEnd)
    {
        var tenant = await _db.Tenants.Include(t => t.Plan).FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return new();

        var result = new Dictionary<string, UsageChargeLine>();

        foreach (var (metric, config) in _options.UsageMetrics)
        {
            var actual = await _db.UsageRecords
                .Where(r => r.TenantId == tenantId && r.Metric == metric && r.PeriodStart >= periodStart && r.PeriodEnd <= periodEnd)
                .SumAsync(r => r.Quantity);

            long? included = null;
            if (config.IncludedByPlan.TryGetValue(tenant.Plan.Slug, out var inc))
                included = inc;

            long overage = 0;
            if (included.HasValue)
                overage = Math.Max(0, actual - included.Value);
            // If included is null (unlimited), overage stays 0

            var charge = overage * config.OveragePrice;

            result[metric] = new UsageChargeLine(
                MetricDisplayName: config.DisplayName,
                IncludedQuantity: included ?? 0,
                ActualQuantity: actual,
                OverageQuantity: overage,
                PricePerUnit: config.OveragePrice,
                TotalCharge: charge
            );
        }

        return result;
    }

    public async Task<UsageBillingResult> ProcessEndOfPeriodAsync(Guid tenantId)
    {
        if (!_options.Features.UsageBilling)
            return new UsageBillingResult(true, TotalUsageCharge: 0);

        var sub = await _db.Subscriptions
            .Where(s => s.TenantId == tenantId && s.Status == SubscriptionStatus.Active)
            .FirstOrDefaultAsync();

        if (sub is null)
            return new UsageBillingResult(false, Error: "No active subscription");

        var now = DateTime.UtcNow;
        var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);
        var periodEnd = periodStart.AddMonths(1).AddTicks(-1);

        var charges = await CalculateUsageChargesAsync(tenantId, periodStart, periodEnd);
        var totalCharge = charges.Values.Sum(c => c.TotalCharge);

        if (totalCharge <= 0)
            return new UsageBillingResult(true, TotalUsageCharge: 0, UsageBreakdown: charges);

        // Create usage invoice
        var lineItems = charges
            .Where(c => c.Value.TotalCharge > 0)
            .Select((c, i) => new InvoiceLineItem
            {
                Type = LineItemType.UsageCharge,
                Description = $"{c.Value.MetricDisplayName} overage ({c.Value.OverageQuantity} × {c.Value.PricePerUnit:C})",
                Quantity = (int)c.Value.OverageQuantity,
                UnitPrice = c.Value.PricePerUnit,
                Amount = c.Value.TotalCharge,
                UsageMetric = c.Key,
                SortOrder = i
            })
            .ToList();

        var invoice = await _invoiceEngine.GenerateProrationInvoiceAsync(
            tenantId, $"Usage charges for {periodStart:MMM yyyy}", lineItems);

        _logger.LogInformation("Generated usage invoice {Number} for tenant {TenantId}: {Total:C}",
            invoice.InvoiceNumber, tenantId, totalCharge);

        return new UsageBillingResult(true, InvoiceId: invoice.Id, TotalUsageCharge: totalCharge, UsageBreakdown: charges);
    }
}
