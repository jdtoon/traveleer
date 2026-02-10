using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Modules.Tenancy.Entities;

namespace saas.Modules.Tenancy.Services;

/// <summary>
/// Background service that cleans up abandoned PendingSetup tenants.
/// When a user starts paid registration but never completes payment,
/// the tenant record stays in PendingSetup state. This service removes
/// such records after a configurable timeout (default 24 hours).
/// </summary>
public class PendingTenantCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PendingTenantCleanupService> _logger;
    private readonly TimeSpan _maxAge = TimeSpan.FromHours(24);
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);

    public PendingTenantCleanupService(
        IServiceProvider serviceProvider,
        ILogger<PendingTenantCleanupService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        var hours = configuration.GetValue<int?>("Tenancy:PendingCleanupHours");
        if (hours.HasValue)
            _maxAge = TimeSpan.FromHours(hours.Value);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during pending tenant cleanup");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var coreDb = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

        var cutoff = DateTime.UtcNow - _maxAge;

        var abandoned = await coreDb.Tenants
            .Where(t => t.Status == TenantStatus.PendingSetup && t.CreatedAt < cutoff)
            .ToListAsync(ct);

        if (abandoned.Count == 0)
            return;

        // Also clean up any subscriptions created for these tenants
        var abandonedIds = abandoned.Select(t => t.Id).ToList();
        var orphanSubs = await coreDb.Subscriptions
            .Where(s => abandonedIds.Contains(s.TenantId))
            .ToListAsync(ct);

        coreDb.Subscriptions.RemoveRange(orphanSubs);
        coreDb.Tenants.RemoveRange(abandoned);
        await coreDb.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Cleaned up {Count} abandoned PendingSetup tenants (older than {Hours}h)",
            abandoned.Count, _maxAge.TotalHours);
    }
}
