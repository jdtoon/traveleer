using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using saas.Infrastructure.Services;
using saas.Shared;

namespace saas.Infrastructure.HealthChecks;

public class LitestreamReadinessHealthCheck : IHealthCheck
{
    private readonly ILitestreamStatusService _statusService;
    private readonly LitestreamOptions _options;

    public LitestreamReadinessHealthCheck(
        ILitestreamStatusService statusService,
        IOptions<LitestreamOptions> options)
    {
        _statusService = statusService;
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = await _statusService.GetStatusAsync(cancellationToken);

        if (status.AutoRestoreEnabled && !status.LitestreamBinaryAvailable)
            return HealthCheckResult.Unhealthy("Auto-restore is enabled but Litestream binary is missing in app container");

        if (status.AutoRestoreEnabled && !status.LitestreamConfigured)
            return HealthCheckResult.Degraded("Auto-restore enabled but R2 backup configuration is incomplete");

        if (status.KeyBackupEnabled)
        {
            var backupInterval = DurationParser.ParseOrDefault(_options.KeyBackupInterval, TimeSpan.FromHours(1));
            var stalenessThreshold = backupInterval.Add(backupInterval);
            if (status.LastKeyBackupUtc is null)
                return HealthCheckResult.Degraded("Key backup enabled but no successful key backup has been recorded yet");

            if (DateTime.UtcNow - status.LastKeyBackupUtc.Value > stalenessThreshold)
                return HealthCheckResult.Degraded("Key backup marker is stale; verify backup worker and storage connectivity");
        }

        return HealthCheckResult.Healthy("Litestream backup and restore readiness checks passed");
    }
}
