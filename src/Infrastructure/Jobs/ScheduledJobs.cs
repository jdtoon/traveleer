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
/// Hourly cleanup of stale/expired user sessions.
/// </summary>
public class StaleSessionCleanupJob
{
    private readonly ILogger<StaleSessionCleanupJob> _logger;

    public StaleSessionCleanupJob(ILogger<StaleSessionCleanupJob> logger)
    {
        _logger = logger;
    }

    public Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Stale session cleanup — placeholder (sessions module pending)");
        // TODO: Implement when Session Management (Item 10) is built
        return Task.CompletedTask;
    }
}

/// <summary>
/// Daily check for expired trial tenants — suspend or notify.
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
        var coreDb = scope.ServiceProvider.GetRequiredService<Data.Core.CoreDbContext>();

        // Find tenants on trial that have expired
        var now = DateTime.UtcNow;
        var expiredTrials = coreDb.Tenants
            .Where(t => t.Status == TenantStatus.Active
                && t.TrialEndsAt != null
                && t.TrialEndsAt < now)
            .ToList();

        foreach (var tenant in expiredTrials)
        {
            _logger.LogWarning("Tenant {Slug} trial expired at {TrialEnd}", tenant.Slug, tenant.TrialEndsAt);
            // TODO: Send notification, suspend, or prompt for payment
        }

        _logger.LogInformation("Expired trial check done — found {Count} expired", expiredTrials.Count);
    }
}
