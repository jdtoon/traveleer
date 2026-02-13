using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using saas.Infrastructure.Services;
using saas.Shared;

namespace saas.Infrastructure.HealthChecks;

public class BackupReadinessHealthCheck : IHealthCheck
{
    private readonly IBackupStatusService _backupStatusService;
    private readonly BackupOptions _backupOptions;

    public BackupReadinessHealthCheck(
        IBackupStatusService backupStatusService,
        IOptions<BackupOptions> backupOptions)
    {
        _backupStatusService = backupStatusService;
        _backupOptions = backupOptions.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = await _backupStatusService.GetStatusAsync(cancellationToken);

        if (status.AutoRestoreEnabled && !status.LitestreamBinaryAvailable)
            return HealthCheckResult.Unhealthy("Auto-restore is enabled but Litestream binary is missing in app container");

        if (status.AutoRestoreEnabled && !status.LitestreamConfigured)
            return HealthCheckResult.Degraded("Auto-restore enabled but R2 backup configuration is incomplete");

        if (status.KeyBackupEnabled)
        {
            var backupInterval = DurationParser.ParseOrDefault(_backupOptions.KeyBackupInterval, TimeSpan.FromHours(1));
            var stalenessThreshold = backupInterval.Add(backupInterval);
            if (status.LastKeyBackupUtc is null)
                return HealthCheckResult.Degraded("Key backup enabled but no successful key backup has been recorded yet");

            if (DateTime.UtcNow - status.LastKeyBackupUtc.Value > stalenessThreshold)
                return HealthCheckResult.Degraded("Key backup marker is stale; verify backup worker and storage connectivity");
        }

        return HealthCheckResult.Healthy("Backup and restore readiness checks passed");
    }
}
