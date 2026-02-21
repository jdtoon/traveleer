using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace saas.Infrastructure.HealthChecks;

public class SeqHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public SeqHealthCheck(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var seqUrl = _configuration.GetValue<string>("Seq:Url");
        if (string.IsNullOrWhiteSpace(seqUrl))
            return HealthCheckResult.Healthy("Seq not configured");

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var response = await client.GetAsync($"{seqUrl.TrimEnd('/')}/api", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Seq API is reachable")
                : HealthCheckResult.Degraded($"Seq returned {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Seq unreachable", ex);
        }
    }
}
