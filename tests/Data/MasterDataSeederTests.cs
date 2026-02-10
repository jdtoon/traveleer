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
            new saas.Modules.Notes.NotesModule(),
            new saas.Modules.Audit.AuditModule(),
            new saas.Modules.TenantAdmin.TenantAdminModule(),
            new saas.Modules.Auth.AuthModule(),
        };

        await CoreDataSeeder.SeedAsync(db, config, modules);
        await CoreDataSeeder.SeedAsync(db, config, modules);

        Assert.Equal(4, await db.Plans.CountAsync());
        // Notes:1 + Audit:1 + TenantAdmin:1 + Auth:1 = 4 features (TenancyModule no longer contributes features)
        Assert.Equal(4, await db.Features.CountAsync());
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
            new saas.Modules.Notes.NotesModule(),         // notes → starter
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

        // Starter (SortOrder 1) — notes + custom_roles = 2
        var starterPlan = plans.First(p => p.Slug == "starter");
        var starterFeatureCount = planFeatures.Count(pf => pf.PlanId == starterPlan.Id);
        Assert.Equal(2, starterFeatureCount);

        // Professional (SortOrder 2) — starter features + audit_log = 3
        var proPlan = plans.First(p => p.Slug == "professional");
        var proFeatureCount = planFeatures.Count(pf => pf.PlanId == proPlan.Id);
        Assert.Equal(3, proFeatureCount);

        // Enterprise (SortOrder 3) — all features = 4
        var entPlan = plans.First(p => p.Slug == "enterprise");
        var entFeatureCount = planFeatures.Count(pf => pf.PlanId == entPlan.Id);
        Assert.Equal(4, entFeatureCount);
    }
}
