using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Notes.Entities;
using Xunit;

namespace saas.Tests.Data;

public class TenantDbContextTests
{
    [Fact]
    public async Task TenantDbContext_CreatesIdentityAndNoteTables()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new TenantDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Notes.Add(new Note { Id = Guid.NewGuid(), Title = "Test" });
        await db.SaveChangesAsync();

        var noteCount = await db.Notes.CountAsync();
        Assert.Equal(1, noteCount);
    }
}
