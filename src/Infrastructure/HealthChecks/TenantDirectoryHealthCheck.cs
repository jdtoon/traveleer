using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace saas.Infrastructure.HealthChecks;

public class TenantDirectoryHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "db", "tenants");
        var exists = Directory.Exists(basePath);

        return Task.FromResult(exists
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Tenant directory missing"));
    }
}
