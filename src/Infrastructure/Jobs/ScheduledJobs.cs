using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Modules.Auth.Entities;
using saas.Modules.Billing.Entities;
using saas.Modules.Billing.Services;
using saas.Modules.TenantAdmin.Services;

namespace saas.Infrastructure.Jobs;

/// <summary>
/// Daily billing reconciliation — sync subscription states with Paystack.
/// </summary>
public class BillingReconciliationJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BillingReconciliationJob> _logger;

    public BillingReconciliationJob(IServiceScopeFactory scopeFactory, ILogger<BillingReconciliationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting billing reconciliation job");

        using var scope = _scopeFactory.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<Shared.IBillingService>();
        await billing.ReconcileSubscriptionsAsync();

        _logger.LogInformation("Billing reconciliation completed");
    }
}

/// <summary>
/// Hourly cleanup of stale/expired user sessions across all tenant databases.
/// </summary>
public class StaleSessionCleanupJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StaleSessionCleanupJob> _logger;
    private readonly IConfiguration _configuration;

    public StaleSessionCleanupJob(IServiceScopeFactory scopeFactory, ILogger<StaleSessionCleanupJob> logger, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting stale session cleanup");

        using var scope = _scopeFactory.CreateScope();
        var coreDb = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

        var tenantSlugs = await coreDb.Tenants
            .Where(t => t.Status == TenantStatus.Active && !t.IsDeleted)
            .Select(t => t.Slug)
            .ToListAsync(ct);

        var tenantPath = _configuration["Tenancy:DatabasePath"] ?? Path.Combine("db", "tenants");
        var basePath = Path.IsPathRooted(tenantPath)
            ? tenantPath
            : Path.Combine(Directory.GetCurrentDirectory(), tenantPath);

        var totalCleaned = 0;
        foreach (var slug in tenantSlugs)
        {
            try
            {
                var dbPath = Path.Combine(basePath, $"{slug}.db");
                if (!File.Exists(dbPath)) continue;

                var connectionString = $"Data Source={dbPath}";
                var optionsBuilder = new DbContextOptionsBuilder<Data.Tenant.TenantDbContext>();
                optionsBuilder.UseSqlite(connectionString);

                using var tenantDb = new Data.Tenant.TenantDbContext(optionsBuilder.Options);

                var cutoff = DateTime.UtcNow;
                var stale = await tenantDb.Set<UserSession>()
                    .Where(s => !s.IsRevoked && s.ExpiresAt.HasValue && s.ExpiresAt < cutoff)
                    .ToListAsync(ct);

                foreach (var session in stale)
                    session.IsRevoked = true;

                if (stale.Count > 0)
                {
                    await tenantDb.SaveChangesAsync(ct);
                    totalCleaned += stale.Count;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean sessions for tenant {Slug}", slug);
            }
        }

        _logger.LogInformation("Stale session cleanup completed — revoked {Count} sessions", totalCleaned);
    }
}

/// <summary>
/// Daily check for expired trial tenants — suspends them.
/// </summary>
public class ExpiredTrialJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpiredTrialJob> _logger;

    public ExpiredTrialJob(IServiceScopeFactory scopeFactory, ILogger<ExpiredTrialJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Checking for expired trials");

        using var scope = _scopeFactory.CreateScope();
        var coreDb = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

        var now = DateTime.UtcNow;
        var expiredTrials = await coreDb.Tenants
            .Include(t => t.ActiveSubscription)
            .Where(t => t.Status == TenantStatus.Active
                && t.ActiveSubscription != null
                && t.ActiveSubscription.TrialEndsAt != null
                && t.ActiveSubscription.TrialEndsAt < now)
            .ToListAsync(ct);

        foreach (var tenant in expiredTrials)
        {
            tenant.Status = TenantStatus.Suspended;
            _logger.LogWarning("Tenant {Slug} trial expired at {TrialEnd} — suspended", tenant.Slug, tenant.ActiveSubscription?.TrialEndsAt);
        }

        if (expiredTrials.Count > 0)
            await coreDb.SaveChangesAsync(ct);

        _logger.LogInformation("Expired trial check done — suspended {Count} tenants", expiredTrials.Count);
    }
}

/// <summary>
/// Daily purge of tenants past their scheduled deletion date.
/// </summary>
public class TenantDeletionJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TenantDeletionJob> _logger;

    public TenantDeletionJob(IServiceScopeFactory scopeFactory, ILogger<TenantDeletionJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Checking for tenants scheduled for deletion");

        using var scope = _scopeFactory.CreateScope();
        var coreDb = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var lifecycle = scope.ServiceProvider.GetRequiredService<ITenantLifecycleService>();

        var now = DateTime.UtcNow;
        var toDelete = await coreDb.Tenants
            .Where(t => t.IsDeleted && t.ScheduledDeletionAt.HasValue && t.ScheduledDeletionAt <= now)
            .ToListAsync(ct);

        foreach (var tenant in toDelete)
        {
            try
            {
                await lifecycle.PermanentlyDeleteTenantAsync(tenant.Id);
                _logger.LogWarning("Permanently deleted tenant {Slug} (scheduled at {Date})", tenant.Slug, tenant.ScheduledDeletionAt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to permanently delete tenant {Slug}", tenant.Slug);
            }
        }

        _logger.LogInformation("Tenant deletion check done — deleted {Count} tenants", toDelete.Count);
    }
}

/// <summary>
/// Hourly dunning job — retries failed charges and suspends tenants past grace period.
/// </summary>
public class DunningJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DunningJob> _logger;

    public DunningJob(IServiceScopeFactory scopeFactory, ILogger<DunningJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting dunning job");

        using var scope = _scopeFactory.CreateScope();
        var dunning = scope.ServiceProvider.GetRequiredService<IDunningService>();
        await dunning.ProcessGracePeriodsAsync();

        _logger.LogInformation("Dunning job completed");
    }
}

/// <summary>
/// Daily usage billing job — processes end-of-period usage charges for all active tenants with usage-based plans.
/// </summary>
public class UsageBillingJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UsageBillingJob> _logger;

    public UsageBillingJob(IServiceScopeFactory scopeFactory, ILogger<UsageBillingJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting usage billing job");

        using var scope = _scopeFactory.CreateScope();
        var coreDb = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var usageBilling = scope.ServiceProvider.GetRequiredService<IUsageBillingService>();

        // Find tenants with usage-based or hybrid plans whose billing period ended
        var now = DateTime.UtcNow;
        var eligibleTenants = await coreDb.Subscriptions
            .Include(s => s.Plan)
            .Where(s => s.Status == SubscriptionStatus.Active
                && (s.Plan.BillingModel == BillingModel.UsageBased || s.Plan.BillingModel == BillingModel.Hybrid)
                && s.NextBillingDate <= now)
            .Select(s => s.TenantId)
            .ToListAsync(ct);

        var processed = 0;
        foreach (var tenantId in eligibleTenants)
        {
            try
            {
                var result = await usageBilling.ProcessEndOfPeriodAsync(tenantId);
                if (result.Success) processed++;
                else _logger.LogWarning("Usage billing failed for tenant {TenantId}: {Error}", tenantId, result.Error);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Usage billing error for tenant {TenantId}", tenantId);
            }
        }

        _logger.LogInformation("Usage billing job completed — processed {Count}/{Total} tenants", processed, eligibleTenants.Count);
    }
}

/// <summary>
/// Daily discount and trial expiry job — deactivates expired discounts and decrements billing-cycle counters.
/// </summary>
public class DiscountExpiryJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DiscountExpiryJob> _logger;

    public DiscountExpiryJob(IServiceScopeFactory scopeFactory, ILogger<DiscountExpiryJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting discount expiry job");

        using var scope = _scopeFactory.CreateScope();
        var coreDb = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var discountService = scope.ServiceProvider.GetRequiredService<IDiscountService>();

        var now = DateTime.UtcNow;

        // Deactivate global discounts past their ValidUntil date
        var expiredDiscounts = await coreDb.Discounts
            .Where(d => d.IsActive && d.ValidUntil.HasValue && d.ValidUntil < now)
            .ToListAsync(ct);

        foreach (var discount in expiredDiscounts)
        {
            discount.IsActive = false;
            _logger.LogInformation("Deactivated expired discount: {Code}", discount.Code);
        }

        // Remove tenant discounts that have expired
        var expiredTenantDiscounts = await coreDb.TenantDiscounts
            .Where(td => td.IsActive && td.ExpiresAt.HasValue && td.ExpiresAt < now)
            .ToListAsync(ct);

        foreach (var td in expiredTenantDiscounts)
        {
            td.IsActive = false;
            _logger.LogInformation("Deactivated expired tenant discount {Id} for tenant {TenantId}", td.Id, td.TenantId);
        }

        // Decrement remaining cycles for tenant discounts that are per-billing-cycle
        var cycleDiscounts = await coreDb.TenantDiscounts
            .Where(td => td.IsActive && td.RemainingCycles.HasValue && td.RemainingCycles > 0)
            .ToListAsync(ct);

        foreach (var td in cycleDiscounts)
        {
            await discountService.DecrementCyclesAsync(td.Id);
        }

        if (expiredDiscounts.Count > 0 || expiredTenantDiscounts.Count > 0)
            await coreDb.SaveChangesAsync(ct);

        _logger.LogInformation("Discount expiry job completed — deactivated {Global} global, {Tenant} tenant discounts",
            expiredDiscounts.Count, expiredTenantDiscounts.Count);
    }
}
