using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Data.Tenant;
using saas.Infrastructure.Provisioning;
using saas.Modules.Auth.Entities;
using saas.Shared;

namespace saas.Data.Core;

/// <summary>
/// Seeds demo data for local development. Only runs when DevSeed:Enabled is true.
/// Creates a demo tenant via the full provisioning flow, adds a member user,
/// and calls SeedDemoDataAsync on each module.
/// Idempotent — skips if the demo tenant already exists.
/// </summary>
public static class DevDataSeeder
{
    public static async Task SeedAsync(
        IServiceProvider rootServices,
        DevSeedOptions options,
        IReadOnlyList<IModule> modules)
    {
        using var scope = rootServices.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DevDataSeeder");
        var coreDb = services.GetRequiredService<CoreDbContext>();

        // Guard: skip if demo tenant already exists
        var existingTenant = await coreDb.Tenants
            .FirstOrDefaultAsync(t => t.Slug == options.TenantSlug.ToLowerInvariant());
        if (existingTenant is not null)
        {
            logger.LogInformation("Demo tenant '{Slug}' already exists, skipping dev seed", options.TenantSlug);
            return;
        }

        // Resolve plan by slug
        var plan = await coreDb.Plans.FirstOrDefaultAsync(p => p.Slug == options.PlanSlug);
        if (plan is null)
        {
            logger.LogWarning("DevSeed plan '{PlanSlug}' not found. Run CoreDataSeeder first.", options.PlanSlug);
            return;
        }

        // Provision the demo tenant using the full provisioning flow
        var provisioner = services.GetRequiredService<ITenantProvisioner>();
        var result = await provisioner.ProvisionTenantAsync(options.TenantSlug, options.AdminEmail, plan.Id);

        if (!result.Success)
        {
            logger.LogWarning("DevSeed tenant provisioning failed: {Error}", result.ErrorMessage);
            return;
        }

        logger.LogInformation("DevSeed: Provisioned demo tenant '{Slug}' on plan '{Plan}'",
            options.TenantSlug, options.PlanSlug);

        // Create Member user on the demo tenant
        var configuration = services.GetRequiredService<IConfiguration>();
        var environment = services.GetRequiredService<IHostEnvironment>();
        var tenantPath = configuration["Tenancy:DatabasePath"] ?? Path.Combine("db", "tenants");
        var tenantBasePath = Path.IsPathRooted(tenantPath)
            ? tenantPath
            : Path.Combine(environment.ContentRootPath, tenantPath);
        var tenantDbPath = Path.Combine(tenantBasePath, $"{options.TenantSlug.ToLowerInvariant()}.db");
        var connectionString = $"Data Source={tenantDbPath}";

        // Use a new scope so UserManager resolves against the correct tenant DB
        using var tenantScope = rootServices.CreateScope();
        var tenantDb = tenantScope.ServiceProvider.GetRequiredService<TenantDbContext>();
        tenantDb.Database.SetConnectionString(connectionString);

        var userManager = tenantScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var memberExists = await tenantDb.Users.AnyAsync(u => u.Email == options.MemberEmail);
        if (!memberExists)
        {
            var memberUser = new AppUser
            {
                UserName = options.MemberEmail,
                Email = options.MemberEmail,
                EmailConfirmed = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var createResult = await userManager.CreateAsync(memberUser);
            if (createResult.Succeeded)
            {
                await userManager.AddToRoleAsync(memberUser, "Member");
                logger.LogInformation("DevSeed: Created member user {Email}", options.MemberEmail);
            }
            else
            {
                logger.LogWarning("DevSeed: Failed to create member user: {Errors}",
                    string.Join(", ", createResult.Errors.Select(e => e.Description)));
            }
        }

        // Call SeedDemoDataAsync on each module
        foreach (var module in modules)
        {
            try
            {
                await module.SeedDemoDataAsync(tenantScope.ServiceProvider);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "DevSeed: Module {Module} SeedDemoDataAsync failed", module.Name);
            }
        }

        logger.LogInformation("DevSeed: Complete — demo tenant '{Slug}' ready", options.TenantSlug);
    }
}
