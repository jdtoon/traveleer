using saas.Shared;

namespace saas.Modules.Billing.Services;

/// <summary>
/// Background service that syncs plans with Paystack on startup.
/// Only runs when provider is Paystack (not in Mock mode).
/// </summary>
public class PaystackPlanSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaystackPlanSyncService> _logger;

    public PaystackPlanSyncService(IServiceScopeFactory scopeFactory,
        ILogger<PaystackPlanSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay to let the app fully start
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        try
        {
            _logger.LogInformation("Starting Paystack plan sync...");
            using var scope = _scopeFactory.CreateScope();
            var billing = scope.ServiceProvider.GetRequiredService<IBillingService>();
            await billing.SyncPlansAsync();
            _logger.LogInformation("Paystack plan sync completed.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Paystack plan sync failed");
        }
    }
}
