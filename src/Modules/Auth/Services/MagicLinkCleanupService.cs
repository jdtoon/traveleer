using Microsoft.Extensions.Hosting;

namespace saas.Modules.Auth.Services;

public class MagicLinkCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MagicLinkCleanupService> _logger;
    private readonly TimeSpan _interval;

    public MagicLinkCleanupService(
        IServiceProvider serviceProvider,
        ILogger<MagicLinkCleanupService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        var minutes = configuration.GetValue("Auth:MagicLinkCleanupIntervalMinutes", 60);
        _interval = TimeSpan.FromMinutes(minutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MagicLinkCleanupService started with interval {Interval}", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var magicLinks = scope.ServiceProvider.GetRequiredService<MagicLinkService>();
                var deleted = await magicLinks.CleanupExpiredAsync();

                if (deleted > 0)
                    _logger.LogInformation("Cleaned up {Count} expired magic link tokens", deleted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during magic link token cleanup — will retry next interval");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
