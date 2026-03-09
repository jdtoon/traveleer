using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using saas.Data.Core;
using saas.Shared;
using Xunit;

namespace saas.Tests.Data;

public class CoreDataSeederTests
{
    [Fact]
    public async Task SeedAsync_SeedsPlansFeaturesAndSuperAdmin_AndIsIdempotent()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new CoreDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SuperAdmin:Email"] = "admin@localhost"
            })
            .Build();

        // Module list provides all features — same as production module set
        var modules = new IModule[]
        {
            new saas.Modules.Tenancy.TenancyModule(),
            new saas.Modules.Audit.AuditModule(),
            new saas.Modules.TenantAdmin.TenantAdminModule(),
            new saas.Modules.Auth.AuthModule(),
        };

        await CoreDataSeeder.SeedAsync(db, config, modules);
        await CoreDataSeeder.SeedAsync(db, config, modules);

        Assert.Equal(4, await db.Plans.CountAsync());
        // Audit:1 + TenantAdmin:1 = 2 features (AuthModule SSO removed, TenancyModule has no features)
        Assert.Equal(2, await db.Features.CountAsync());
        Assert.True(await db.PlanFeatures.AnyAsync());
        Assert.Equal(1, await db.SuperAdmins.CountAsync());
    }

    [Fact]
    public async Task SeedAsync_PlanFeatureMatrix_AssignsFeaturesByMinPlanSlug()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new CoreDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SuperAdmin:Email"] = "admin@localhost"
            })
            .Build();

        var modules = new IModule[]
        {
            new saas.Modules.TenantAdmin.TenantAdminModule(), // custom_roles → starter
            new saas.Modules.Audit.AuditModule(),         // audit_log → professional
            new saas.Modules.Auth.AuthModule(),           // sso → enterprise
        };

        await CoreDataSeeder.SeedAsync(db, config, modules);

        var plans = await db.Plans.OrderBy(p => p.SortOrder).ToListAsync();
        var planFeatures = await db.PlanFeatures.ToListAsync();
        var features = await db.Features.ToListAsync();

        // Free (SortOrder 0) — no features (all have MinPlanSlug set)
        var freePlan = plans.First(p => p.Slug == "free");
        var freeFeatureCount = planFeatures.Count(pf => pf.PlanId == freePlan.Id);
        Assert.Equal(0, freeFeatureCount);

        // Starter (SortOrder 1) — custom_roles = 1
        var starterPlan = plans.First(p => p.Slug == "starter");
        var starterFeatureCount = planFeatures.Count(pf => pf.PlanId == starterPlan.Id);
        Assert.Equal(1, starterFeatureCount);

        // Professional (SortOrder 2) — starter features + audit_log = 2
        var proPlan = plans.First(p => p.Slug == "professional");
        var proFeatureCount = planFeatures.Count(pf => pf.PlanId == proPlan.Id);
        Assert.Equal(2, proFeatureCount);

        // Enterprise (SortOrder 3) — all features = 2
        var entPlan = plans.First(p => p.Slug == "enterprise");
        var entFeatureCount = planFeatures.Count(pf => pf.PlanId == entPlan.Id);
        Assert.Equal(2, entFeatureCount);
    }

    [Fact]
    public async Task SeedAsync_WhenNewFeatureIsAddedLater_BackfillsPlanMappings()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new CoreDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SuperAdmin:Email"] = "admin@localhost"
            })
            .Build();

        IModule[] initialModules =
        [
            new saas.Modules.Tenancy.TenancyModule(),
            new saas.Modules.Audit.AuditModule(),
            new saas.Modules.TenantAdmin.TenantAdminModule(),
            new saas.Modules.Auth.AuthModule(),
            new saas.Modules.Settings.SettingsModule(),
        ];

        await CoreDataSeeder.SeedAsync(db, config, initialModules);

        var settingsFeatureId = await db.Features
            .Where(f => f.Key == saas.Modules.Settings.SettingsFeatures.Settings)
            .Select(f => f.Id)
            .SingleAsync();

        var staleMappings = await db.PlanFeatures
            .Where(pf => pf.FeatureId == settingsFeatureId)
            .ToListAsync();
        db.PlanFeatures.RemoveRange(staleMappings);
        await db.SaveChangesAsync();

        var settingsFeature = await db.Features.SingleAsync(f => f.Key == saas.Modules.Settings.SettingsFeatures.Settings);
        var starterPlan = await db.Plans.SingleAsync(p => p.Slug == "starter");
        var freePlan = await db.Plans.SingleAsync(p => p.Slug == "free");

        await CoreDataSeeder.SeedAsync(db, config, initialModules);

        Assert.True(await db.PlanFeatures.AnyAsync(pf => pf.PlanId == starterPlan.Id && pf.FeatureId == settingsFeature.Id));
        Assert.False(await db.PlanFeatures.AnyAsync(pf => pf.PlanId == freePlan.Id && pf.FeatureId == settingsFeature.Id));
    }
}
