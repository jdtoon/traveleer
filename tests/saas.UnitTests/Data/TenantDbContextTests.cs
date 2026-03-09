using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Clients.Entities;
using Xunit;

namespace saas.Tests.Data;

public class TenantDbContextTests
{
    [Fact]
    public async Task TenantDbContext_CreatesIdentityAndDomainTables()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new TenantDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Clients.Add(new Client { Id = Guid.NewGuid(), Name = "Test Client" });
        await db.SaveChangesAsync();

        var clientCount = await db.Clients.CountAsync();
        Assert.Equal(1, clientCount);
    }
}
