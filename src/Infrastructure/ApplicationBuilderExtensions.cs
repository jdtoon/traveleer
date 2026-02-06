using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Data.Audit;
using saas.Data.Seeding;
using Swap.Htmx;

namespace saas.Infrastructure;

public static class ApplicationBuilderExtensions
{
    private static readonly SemaphoreSlim InitLock = new(1, 1);
    private static bool _initialized;

    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        await InitLock.WaitAsync();

        try
        {
            if (_initialized)
                return;

            using var scope = app.Services.CreateScope();

            // Initialize CoreDbContext
            var coreDb = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            await coreDb.Database.EnsureCreatedAsync();

            // Initialize AuditDbContext
            var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            await auditDb.Database.EnsureCreatedAsync();

            // Seed master data
            await MasterDataSeeder.SeedAsync(coreDb, app.Configuration);

            _initialized = true;
        }
        finally
        {
            InitLock.Release();
        }
    }

    public static WebApplication ConfigurePipeline(this WebApplication app)
    {
        app.UseResponseCompression();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseWebOptimizer();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseSwapHtmx();
        app.UseAuthorization();

        return app;
    }

    public static WebApplication MapEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                var result = new
                {
                    status = report.Status.ToString(),
                    checks = report.Entries.Select(e => new
                    {
                        name = e.Key,
                        status = e.Value.Status.ToString(),
                        description = e.Value.Description
                    })
                };
                await context.Response.WriteAsJsonAsync(result);
            }
        });

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");

        return app;
    }
}
