using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace saas.Infrastructure.HealthChecks;

public class DiskSpaceHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "db");
            var driveInfo = new DriveInfo(Path.GetPathRoot(dbPath) ?? "/");

            var freePercent = (double)driveInfo.AvailableFreeSpace / driveInfo.TotalSize * 100;
            var data = new Dictionary<string, object>
            {
                ["total_gb"] = Math.Round(driveInfo.TotalSize / 1_073_741_824.0, 2),
                ["free_gb"] = Math.Round(driveInfo.AvailableFreeSpace / 1_073_741_824.0, 2),
                ["free_percent"] = Math.Round(freePercent, 1)
            };

            if (freePercent < 5)
                return Task.FromResult(HealthCheckResult.Unhealthy($"Disk critically low ({freePercent:F1}% free)", data: data));

            if (freePercent < 15)
                return Task.FromResult(HealthCheckResult.Degraded($"Disk space low ({freePercent:F1}% free)", data: data));

            return Task.FromResult(HealthCheckResult.Healthy($"Disk OK ({freePercent:F1}% free)", data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Unable to check disk space", ex));
        }
    }
}
