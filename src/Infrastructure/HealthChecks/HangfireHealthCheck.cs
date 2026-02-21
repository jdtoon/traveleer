using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace saas.Infrastructure.HealthChecks;

public class HangfireHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var storage = JobStorage.Current;
            var monitoringApi = storage.GetMonitoringApi();
            var failedCount = monitoringApi.FailedCount();

            var data = new Dictionary<string, object>
            {
                ["servers"] = monitoringApi.Servers().Count,
                ["failed"] = failedCount,
                ["enqueued"] = monitoringApi.EnqueuedCount("default"),
                ["processing"] = monitoringApi.ProcessingCount()
            };

            if (monitoringApi.Servers().Count == 0)
                return Task.FromResult(HealthCheckResult.Degraded("No Hangfire servers running", data: data));

            if (failedCount > 10)
                return Task.FromResult(HealthCheckResult.Degraded($"{failedCount} failed jobs", data: data));

            return Task.FromResult(HealthCheckResult.Healthy("Hangfire operational", data));
        }
        catch
        {
            // Hangfire storage not initialized (e.g. test environment) — treat as non-critical
            return Task.FromResult(HealthCheckResult.Degraded("Hangfire storage not available"));
        }
    }
}
