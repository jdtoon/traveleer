using Hangfire;
using Hangfire.InMemory;

namespace saas.Infrastructure;

public static class SchedulingExtensions
{
    public static IServiceCollection AddSchedulingConfig(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHangfire(config =>
        {
            config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180);
            config.UseSimpleAssemblyNameTypeSerializer();
            config.UseRecommendedSerializerSettings();

            // Use in-memory storage (lightweight for SQLite-based apps)
            config.UseInMemoryStorage();
        });

        services.AddHangfireServer(options =>
        {
            options.WorkerCount = configuration.GetValue("Hangfire:WorkerCount", 2);
            options.Queues = ["default", "emails", "maintenance"];
        });

        return services;
    }

    public static WebApplication UseSchedulingDashboard(this WebApplication app)
    {
        // Hangfire dashboard — only accessible to SuperAdmins
        app.MapHangfireDashboard("/super-admin/hangfire", new DashboardOptions
        {
            Authorization = [new SuperAdminDashboardAuthFilter()],
            DashboardTitle = "SaaS Job Dashboard"
        });

        return app;
    }

    public static WebApplication RegisterRecurringJobs(this WebApplication app)
    {
        // Register recurring jobs after app starts
        RecurringJob.AddOrUpdate<Jobs.BillingReconciliationJob>(
            "billing-reconciliation",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Daily(2, 0), // 2 AM daily
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        RecurringJob.AddOrUpdate<Jobs.StaleSessionCleanupJob>(
            "stale-session-cleanup",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Hourly,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        RecurringJob.AddOrUpdate<Jobs.ExpiredTrialJob>(
            "expired-trial-check",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Daily(6, 0), // 6 AM daily
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        return app;
    }
}

/// <summary>
/// Limits Hangfire dashboard access to authenticated SuperAdmin users.
/// </summary>
public class SuperAdminDashboardAuthFilter : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated == true
            && httpContext.User.HasClaim("SuperAdmin", "true");
    }
}
