using Serilog;
using Serilog.Events;

namespace saas.Infrastructure;

public static class LoggingExtensions
{
    public static WebApplicationBuilder AddSerilogConfig(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithEnvironmentName()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Application", "saas")
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}  {Message:lj}{NewLine}{Exception}");

            // In production, also write structured JSON logs to file
            if (context.HostingEnvironment.IsProduction())
            {
                configuration.WriteTo.File(
                    path: "logs/saas-.json",
                    formatter: new Serilog.Formatting.Json.JsonFormatter(),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    fileSizeLimitBytes: 50_000_000);
            }

            // Seq structured log server (optional — configure via Seq:Url)
            var seqUrl = context.Configuration["Seq:Url"];
            if (!string.IsNullOrEmpty(seqUrl))
            {
                var seqApiKey = context.Configuration["Seq:ApiKey"];
                configuration.WriteTo.Seq(seqUrl, apiKey: seqApiKey);
            }
        });

        return builder;
    }

    public static WebApplication UseSerilogRequestLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                // Add tenant context to log entries
                var tenantContext = httpContext.RequestServices.GetService<Shared.ITenantContext>();
                if (tenantContext?.IsTenantRequest == true)
                {
                    diagnosticContext.Set("TenantSlug", tenantContext.Slug ?? "unknown");
                    diagnosticContext.Set("TenantId", (object?)tenantContext.TenantId ?? "unknown");
                }

                diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? "unknown");
                diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
            };

            // Don't log health checks as info
            options.GetLevel = (httpContext, elapsed, ex) =>
            {
                if (httpContext.Request.Path.StartsWithSegments("/health"))
                    return LogEventLevel.Verbose;

                return ex != null
                    ? LogEventLevel.Error
                    : elapsed > 500
                        ? LogEventLevel.Warning
                        : LogEventLevel.Information;
            };
        });

        return app;
    }
}
