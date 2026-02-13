using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Modules.Billing.Entities;

namespace saas.Modules.Billing.Services;

public interface IUsageMeteringService
{
    Task RecordUsageAsync(Guid tenantId, string metric, long quantity = 1);
    Task<long> GetCurrentPeriodUsageAsync(Guid tenantId, string metric);
    Task<List<UsageSummary>> GetUsageSummaryAsync(Guid tenantId, DateTime? from = null, DateTime? to = null);
}

public class UsageMeteringService : IUsageMeteringService
{
    private readonly CoreDbContext _coreDb;

    public UsageMeteringService(CoreDbContext coreDb)
    {
        _coreDb = coreDb;
    }

    public async Task RecordUsageAsync(Guid tenantId, string metric, long quantity = 1)
    {
        var now = DateTime.UtcNow;
        var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1).AddTicks(-1);

        var existing = await _coreDb.UsageRecords
            .FirstOrDefaultAsync(r =>
                r.TenantId == tenantId &&
                r.Metric == metric &&
                r.PeriodStart == periodStart);

        if (existing is not null)
        {
            existing.Quantity += quantity;
            existing.UpdatedAt = now;
        }
        else
        {
            _coreDb.UsageRecords.Add(new UsageRecord
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

        await _coreDb.SaveChangesAsync();
    }

    public async Task<long> GetCurrentPeriodUsageAsync(Guid tenantId, string metric)
    {
        var periodStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        return await _coreDb.UsageRecords
            .Where(r => r.TenantId == tenantId && r.Metric == metric && r.PeriodStart == periodStart)
            .Select(r => r.Quantity)
            .FirstOrDefaultAsync();
    }

    public async Task<List<UsageSummary>> GetUsageSummaryAsync(Guid tenantId, DateTime? from = null, DateTime? to = null)
    {
        var query = _coreDb.UsageRecords
            .Where(r => r.TenantId == tenantId);

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
}

public class UsageSummary
{
    public string Metric { get; set; } = string.Empty;
    public long TotalQuantity { get; set; }
    public List<UsagePeriod> Periods { get; set; } = [];
}

public class UsagePeriod
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public long Quantity { get; set; }
}
