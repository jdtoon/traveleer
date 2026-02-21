namespace saas.Infrastructure.Middleware;

public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public SecurityHeadersMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

        var isSuperAdmin = context.Request.Path.StartsWithSegments("/super-admin");

        if (isSuperAdmin)
        {
            // Super Admin pages may embed infrastructure iframes (Seq, RabbitMQ, Uptime Kuma)
            // Allow framing from same origin only (SAMEORIGIN) instead of blanket DENY
            headers["X-Frame-Options"] = "SAMEORIGIN";

            // Build frame-src list from configured infrastructure URLs
            var frameSources = BuildInfrastructureFrameSources();
            headers["Content-Security-Policy"] = "default-src 'self'; " +
                                 "img-src 'self' data:; " +
                                 "style-src 'self' 'unsafe-inline'; " +
                                 "script-src 'self' 'unsafe-inline' https://challenges.cloudflare.com; " +
                                 $"frame-src 'self' https://challenges.cloudflare.com {frameSources}".TrimEnd();
        }
        else
        {
            headers["X-Frame-Options"] = "DENY";
            headers["Content-Security-Policy"] = "default-src 'self'; " +
                                 "img-src 'self' data:; " +
                                 "style-src 'self' 'unsafe-inline'; " +
                                 "script-src 'self' 'unsafe-inline' https://challenges.cloudflare.com; " +
                                 "frame-src https://challenges.cloudflare.com";
        }

        await _next(context);
    }

    private string BuildInfrastructureFrameSources()
    {
        var sources = new List<string>();

        var seqUrl = _configuration["Infrastructure:SeqUrl"];
        if (!string.IsNullOrWhiteSpace(seqUrl))
            sources.Add(seqUrl.TrimEnd('/'));

        var rabbitUrl = _configuration["Infrastructure:RabbitMqManagementUrl"];
        if (!string.IsNullOrWhiteSpace(rabbitUrl))
            sources.Add(rabbitUrl.TrimEnd('/'));

        var kumaUrl = _configuration["Infrastructure:UptimeKumaUrl"];
        if (!string.IsNullOrWhiteSpace(kumaUrl))
            sources.Add(kumaUrl.TrimEnd('/'));

        return string.Join(' ', sources);
    }
}
