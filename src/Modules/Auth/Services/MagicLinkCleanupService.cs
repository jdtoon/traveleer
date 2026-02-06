using Microsoft.Extensions.Hosting;

namespace saas.Modules.Auth.Services;

public class MagicLinkCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MagicLinkCleanupService> _logger;

    public MagicLinkCleanupService(IServiceProvider serviceProvider, ILogger<MagicLinkCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceProvider.CreateScope();
            var magicLinks = scope.ServiceProvider.GetRequiredService<MagicLinkService>();
            var deleted = await magicLinks.CleanupExpiredAsync();

            if (deleted > 0)
                _logger.LogInformation("Cleaned up {Count} expired magic link tokens", deleted);

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
