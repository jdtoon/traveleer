using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace saas.Infrastructure.HealthChecks;

public class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer? _redis;

    public RedisHealthCheck(IConnectionMultiplexer? redis = null)
    {
        _redis = redis;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_redis is null)
            return HealthCheckResult.Healthy("Redis not configured (using in-memory cache)");

        try
        {
            var db = _redis.GetDatabase();
            var latency = await db.PingAsync();

            var data = new Dictionary<string, object>
            {
                ["latency_ms"] = latency.TotalMilliseconds
            };

            return latency.TotalMilliseconds < 500
                ? HealthCheckResult.Healthy($"Redis responding ({latency.TotalMilliseconds:F0}ms)", data)
                : HealthCheckResult.Degraded($"Redis slow ({latency.TotalMilliseconds:F0}ms)", data: data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis connection failed", ex);
        }
    }
}
