using saas.Shared;

namespace saas.Modules.Billing.Services;

/// <summary>
/// Background service that periodically reconciles local subscription statuses
/// with Paystack. Catches missed webhooks and status drift.
/// Only runs when provider is Paystack (not in Mock mode).
/// </summary>
public class PaystackSubscriptionSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaystackSubscriptionSyncService> _logger;

    public PaystackSubscriptionSyncService(IServiceScopeFactory scopeFactory,
        ILogger<PaystackSubscriptionSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for app to fully start before first reconciliation
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Starting Paystack subscription reconciliation...");
                using var scope = _scopeFactory.CreateScope();
                var billing = scope.ServiceProvider.GetRequiredService<IBillingService>();
                await billing.ReconcileSubscriptionsAsync();
                _logger.LogInformation("Paystack subscription reconciliation completed.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Paystack subscription reconciliation failed");
            }

            // Reconcile every 6 hours
            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }
}
