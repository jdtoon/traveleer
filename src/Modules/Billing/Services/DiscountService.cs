using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Modules.Billing.Entities;

namespace saas.Modules.Billing.Services;

public interface IDiscountService
{
    Task<DiscountValidation> ValidateCodeAsync(string code, Guid tenantId, Guid? planId = null);
    Task<TenantDiscount> ApplyAsync(Guid tenantId, string code);
    Task<decimal> CalculateDiscountAsync(Guid tenantId, decimal subtotal);
    Task DecrementCyclesAsync(Guid tenantId);
    Task RemoveAsync(Guid tenantId, Guid discountId);
}

public record DiscountValidation(
    bool IsValid,
    string? Error = null,
    Discount? Discount = null
);

public class DiscountService : IDiscountService
{
    private readonly CoreDbContext _db;
    private readonly ILogger<DiscountService> _logger;

    public DiscountService(CoreDbContext db, ILogger<DiscountService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<DiscountValidation> ValidateCodeAsync(string code, Guid tenantId, Guid? planId = null)
    {
        var discount = await _db.Discounts.FirstOrDefaultAsync(d => d.Code == code);

        if (discount is null)
            return new DiscountValidation(false, "Discount code not found");

        if (!discount.IsActive)
            return new DiscountValidation(false, "Discount code is no longer active");

        var now = DateTime.UtcNow;
        if (discount.ValidFrom.HasValue && discount.ValidFrom > now)
            return new DiscountValidation(false, "Discount code is not yet valid");

        if (discount.ValidUntil.HasValue && discount.ValidUntil < now)
            return new DiscountValidation(false, "Discount code has expired");

        if (discount.MaxRedemptions.HasValue && discount.CurrentRedemptions >= discount.MaxRedemptions)
            return new DiscountValidation(false, "Discount code has reached maximum redemptions");

        // Check plan applicability
        if (!string.IsNullOrEmpty(discount.ApplicablePlanSlugs) && planId.HasValue)
        {
            var plan = await _db.Plans.FindAsync(planId.Value);
            if (plan is not null)
            {
                var slugs = discount.ApplicablePlanSlugs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (!slugs.Contains(plan.Slug, StringComparer.OrdinalIgnoreCase))
                    return new DiscountValidation(false, "Discount code is not applicable to this plan");
            }
        }

        // Check if tenant already has this discount active
        var existing = await _db.TenantDiscounts
            .AnyAsync(td => td.TenantId == tenantId && td.DiscountId == discount.Id && td.IsActive);
        if (existing)
            return new DiscountValidation(false, "Discount code is already applied to your account");

        return new DiscountValidation(true, Discount: discount);
    }

    public async Task<TenantDiscount> ApplyAsync(Guid tenantId, string code)
    {
        var validation = await ValidateCodeAsync(code, tenantId);
        if (!validation.IsValid || validation.Discount is null)
            throw new InvalidOperationException(validation.Error ?? "Invalid discount code");

        var discount = validation.Discount;

        var tenantDiscount = new TenantDiscount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DiscountId = discount.Id,
            AppliedAt = DateTime.UtcNow,
            ExpiresAt = discount.DurationInCycles.HasValue
                ? DateTime.UtcNow.AddMonths(discount.DurationInCycles.Value)
                : null,
            RemainingCycles = discount.DurationInCycles,
            IsActive = true
        };

        _db.TenantDiscounts.Add(tenantDiscount);
        discount.CurrentRedemptions++;
        discount.ConcurrencyStamp = Guid.NewGuid();

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("Discount was modified concurrently. Please try again.");
        }

        _logger.LogInformation("Applied discount {Code} to tenant {TenantId}", code, tenantId);
        return tenantDiscount;
    }

    public async Task<decimal> CalculateDiscountAsync(Guid tenantId, decimal subtotal)
    {
        if (subtotal <= 0) return 0;

        var activeDiscounts = await _db.TenantDiscounts
            .Where(td => td.TenantId == tenantId && td.IsActive
                && (!td.ExpiresAt.HasValue || td.ExpiresAt > DateTime.UtcNow))
            .Include(td => td.Discount)
            .ToListAsync();

        if (activeDiscounts.Count == 0) return 0;

        decimal totalDiscount = 0;
        foreach (var td in activeDiscounts)
        {
            var discount = td.Discount;
            decimal amount = discount.Type switch
            {
                DiscountType.Percentage => subtotal * (discount.Value / 100m),
                DiscountType.FixedAmount => Math.Min(discount.Value, subtotal),
                _ => 0
            };
            totalDiscount += amount;
        }

        // Cap at subtotal
        return Math.Min(totalDiscount, subtotal);
    }

    public async Task DecrementCyclesAsync(Guid tenantId)
    {
        var activeDiscounts = await _db.TenantDiscounts
            .Where(td => td.TenantId == tenantId && td.IsActive && td.RemainingCycles.HasValue)
            .ToListAsync();

        foreach (var td in activeDiscounts)
        {
            td.RemainingCycles--;
            if (td.RemainingCycles <= 0)
            {
                td.IsActive = false;
                _logger.LogInformation("Discount {DiscountId} expired for tenant {TenantId}", td.DiscountId, tenantId);
            }
        }

        if (activeDiscounts.Count > 0)
            await _db.SaveChangesAsync();
    }

    public async Task RemoveAsync(Guid tenantId, Guid discountId)
    {
        var td = await _db.TenantDiscounts
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.DiscountId == discountId && d.IsActive);

        if (td is not null)
        {
            td.IsActive = false;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Removed discount {DiscountId} from tenant {TenantId}", discountId, tenantId);
        }
    }
}
