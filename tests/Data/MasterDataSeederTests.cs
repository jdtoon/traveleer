using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using saas.Data.Core;
using saas.Data.Seeding;
using saas.Shared;
using Xunit;

namespace saas.Tests.Data;

public class MasterDataSeederTests
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

        await MasterDataSeeder.SeedAsync(db, config, modules);
        await MasterDataSeeder.SeedAsync(db, config, modules);

        Assert.Equal(4, await db.Plans.CountAsync());
        // Tenancy:5 + Notes:1 + Audit:1 + TenantAdmin:1 + Auth:1 = 9 features
        Assert.Equal(9, await db.Features.CountAsync());
        Assert.True(await db.PlanFeatures.AnyAsync());
        Assert.Equal(1, await db.SuperAdmins.CountAsync());
    }
}
