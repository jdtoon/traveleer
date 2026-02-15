using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Modules.Auth.Entities;
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
            .Where(t => t.Status == TenantStatus.Active
                && t.TrialEndsAt != null
                && t.TrialEndsAt < now)
            .ToListAsync(ct);

        foreach (var tenant in expiredTrials)
        {
            tenant.Status = TenantStatus.Suspended;
            _logger.LogWarning("Tenant {Slug} trial expired at {TrialEnd} — suspended", tenant.Slug, tenant.TrialEndsAt);
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
