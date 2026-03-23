using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Identity;
using saas.Data.Core;
using Xunit;
using SuperAdminEntity = saas.Modules.SuperAdmin.Entities.SuperAdmin;

namespace saas.Tests.Data;

public class CoreDataSeederPasswordTests
{
    private static CoreDbContext CreateDb(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new CoreDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task SeedAsync_SetsPasswordHash_WhenPasswordConfigured()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SuperAdmin:Email"] = "admin@test.com",
                ["SuperAdmin:Password"] = "TestPassword1!"
            })
            .Build();

        var hasher = new PasswordHasher<SuperAdminEntity>();
        await CoreDataSeeder.SeedAsync(db, config, [], hasher);

        var admin = await db.SuperAdmins.FirstOrDefaultAsync(a => a.Email == "admin@test.com");
        Assert.NotNull(admin);
        Assert.NotNull(admin!.PasswordHash);

        var verifyResult = hasher.VerifyHashedPassword(admin, admin.PasswordHash!, "TestPassword1!");
        Assert.Equal(PasswordVerificationResult.Success, verifyResult);
    }

    [Fact]
    public async Task SeedAsync_DoesNotSetPasswordHash_WhenPasswordNotConfigured()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SuperAdmin:Email"] = "admin@test.com"
                // No password configured
            })
            .Build();

        await CoreDataSeeder.SeedAsync(db, config, []);

        var admin = await db.SuperAdmins.FirstOrDefaultAsync(a => a.Email == "admin@test.com");
        Assert.NotNull(admin);
        Assert.Null(admin!.PasswordHash);
    }

    [Fact]
    public async Task SeedAsync_DoesNotSetPasswordHash_WhenHasherIsNull()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SuperAdmin:Email"] = "admin@test.com",
                ["SuperAdmin:Password"] = "SomePassword1!"
            })
            .Build();

        // null hasher — password should not be set
        await CoreDataSeeder.SeedAsync(db, config, [], passwordHasher: null);

        var admin = await db.SuperAdmins.FirstOrDefaultAsync(a => a.Email == "admin@test.com");
        Assert.NotNull(admin);
        Assert.Null(admin!.PasswordHash);
    }
}
