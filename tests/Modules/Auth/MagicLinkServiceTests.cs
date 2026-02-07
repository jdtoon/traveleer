using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using saas.Data.Core;
using saas.Modules.Auth.Services;
using Xunit;

namespace saas.Tests.Modules.Auth;

public class MagicLinkServiceTests
{
    [Fact]
    public async Task GenerateAndVerifyToken_Works_AndHashIsStored()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new CoreDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new MagicLinkService(db, config);

        var token = await service.GenerateTokenAsync("test@test.com");

        Assert.NotNull(token.Token);
        Assert.NotEqual(string.Empty, token.Token);

        var stored = await db.MagicLinkTokens.FirstOrDefaultAsync();
        Assert.NotNull(stored);
        Assert.NotEqual(token.Token, stored!.Token);

        var verify = await service.VerifyTokenAsync(token.Token);
        Assert.True(verify.Success);
        Assert.Equal("test@test.com", verify.Email);
    }

    [Fact]
    public async Task VerifyToken_Fails_WhenExpiredOrUsed()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new CoreDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MagicLinks:ExpiryMinutes"] = "-1"
        }).Build();

        var service = new MagicLinkService(db, config);
        var token = await service.GenerateTokenAsync("expired@test.com");

        var verifyExpired = await service.VerifyTokenAsync(token.Token);
        Assert.False(verifyExpired.Success);

        // Create a valid token then mark it as used
        var validConfig = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service2 = new MagicLinkService(db, validConfig);
        var token2 = await service2.GenerateTokenAsync("used@test.com");
        var verify1 = await service2.VerifyTokenAsync(token2.Token);
        Assert.True(verify1.Success);
        var verify2 = await service2.VerifyTokenAsync(token2.Token);
        Assert.False(verify2.Success);
    }
}
