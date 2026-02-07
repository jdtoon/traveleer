using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using saas.Data.Core;
using saas.Data.Seeding;
using saas.Shared;
using Xunit;

namespace saas.Tests;

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

        // Module list provides module-owned features (Notes, Audit)
        var modules = new IModule[]
        {
            new saas.Modules.Notes.NotesModule(),
            new saas.Modules.Audit.AuditModule()
        };

        await MasterDataSeeder.SeedAsync(db, config, modules);
        await MasterDataSeeder.SeedAsync(db, config, modules);

        Assert.Equal(4, await db.Plans.CountAsync());
        Assert.Equal(9, await db.Features.CountAsync());
        Assert.True(await db.PlanFeatures.AnyAsync());
        Assert.Equal(1, await db.SuperAdmins.CountAsync());
    }
}
