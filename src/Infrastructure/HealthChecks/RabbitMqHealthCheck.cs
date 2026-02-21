using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace saas.Infrastructure.HealthChecks;

public class RabbitMqHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;

    public RabbitMqHealthCheck(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var provider = _configuration.GetValue<string>("Messaging:Provider") ?? "InMemory";
        if (!provider.Equals("RabbitMQ", StringComparison.OrdinalIgnoreCase))
            return HealthCheckResult.Healthy("RabbitMQ not configured (using in-memory messaging)");

        var host = _configuration.GetValue<string>("Messaging:RabbitMQ:Host") ?? "localhost";
        var port = _configuration.GetValue<int>("Messaging:RabbitMQ:Port", 5672);

        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port, cancellationToken);
            return HealthCheckResult.Healthy($"RabbitMQ reachable at {host}:{port}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"RabbitMQ unreachable at {host}:{port}", ex);
        }
    }
}
