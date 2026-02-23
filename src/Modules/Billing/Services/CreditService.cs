using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Modules.Billing.Entities;

namespace saas.Modules.Billing.Services;

public interface ICreditService
{
    Task<TenantCredit> AddCreditAsync(Guid tenantId, decimal amount, CreditReason reason, string? description = null);
    Task<decimal> ApplyCreditsToInvoiceAsync(Guid tenantId, Invoice invoice);
    Task<decimal> GetBalanceAsync(Guid tenantId);
    Task<List<TenantCredit>> GetLedgerAsync(Guid tenantId);
}

public class CreditService : ICreditService
{
    private readonly CoreDbContext _db;
    private readonly ILogger<CreditService> _logger;

    public CreditService(CoreDbContext db, ILogger<CreditService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TenantCredit> AddCreditAsync(Guid tenantId, decimal amount, CreditReason reason, string? description = null)
    {
        if (amount <= 0)
            throw new ArgumentException("Credit amount must be positive", nameof(amount));

        var credit = new TenantCredit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Amount = amount,
            RemainingAmount = amount,
            Currency = "ZAR",
            Reason = reason,
            Description = description,
            CreatedAt = DateTime.UtcNow
        };

        _db.TenantCredits.Add(credit);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Added credit of {Amount} for tenant {TenantId} ({Reason})", amount, tenantId, reason);
        return credit;
    }

    public async Task<decimal> ApplyCreditsToInvoiceAsync(Guid tenantId, Invoice invoice)
    {
        if (invoice.Total <= 0)
            return 0;

        // Retry loop handles optimistic concurrency conflicts on TenantCredit.ConcurrencyStamp
        const int maxRetries = 3;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            var credits = await _db.TenantCredits
                .Where(c => c.TenantId == tenantId && c.RemainingAmount > 0)
                .OrderBy(c => c.CreatedAt)
                .ToListAsync();

            if (credits.Count == 0)
                return 0;

            var remaining = invoice.Total;
            decimal totalApplied = 0;

            foreach (var credit in credits)
            {
                if (remaining <= 0) break;

                var toApply = Math.Min(credit.RemainingAmount, remaining);
                credit.RemainingAmount -= toApply;
                remaining -= toApply;
                totalApplied += toApply;

                if (credit.RemainingAmount == 0)
                {
                    credit.ConsumedAt = DateTime.UtcNow;
                    credit.ConsumedByInvoiceId = invoice.Id;
                }

                // Update concurrency stamp for optimistic locking
                credit.ConcurrencyStamp = Guid.NewGuid();
            }

            if (totalApplied > 0)
            {
                invoice.CreditApplied = totalApplied;
                invoice.Total -= totalApplied;

                // Add credit line item
                _db.InvoiceLineItems.Add(new InvoiceLineItem
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoice.Id,
                    Type = LineItemType.Credit,
                    Description = "Credit applied",
                    Quantity = 1,
                    UnitPrice = -totalApplied,
                    Amount = -totalApplied,
                    SortOrder = 900
                });

                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException) when (attempt < maxRetries - 1)
                {
                    // Another request modified the credits — reload and retry
                    _logger.LogWarning("Credit concurrency conflict for tenant {TenantId}, retrying (attempt {Attempt})",
                        tenantId, attempt + 1);

                    // Detach all modified entries and retry
                    foreach (var entry in _db.ChangeTracker.Entries())
                        entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;

                    continue;
                }

                _logger.LogInformation("Applied {Amount} credits to invoice {InvoiceId} for tenant {TenantId}",
                    totalApplied, invoice.Id, tenantId);
            }

            return totalApplied;
        }

        return 0;
    }

    public async Task<decimal> GetBalanceAsync(Guid tenantId)
    {
        return await _db.TenantCredits
            .Where(c => c.TenantId == tenantId && c.RemainingAmount > 0)
            .SumAsync(c => c.RemainingAmount);
    }

    public async Task<List<TenantCredit>> GetLedgerAsync(Guid tenantId)
    {
        return await _db.TenantCredits
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }
}
