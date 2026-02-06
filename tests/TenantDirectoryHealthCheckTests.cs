using saas.Infrastructure.HealthChecks;
using Xunit;

namespace saas.Tests;

public class TenantDirectoryHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthy_WhenDirectoryExists()
    {
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "db", "tenants");
        Directory.CreateDirectory(basePath);

        var healthCheck = new TenantDirectoryHealthCheck();
        var result = await healthCheck.CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Status);
    }
}
