using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Audit;
using Xunit;

namespace saas.Tests;

public class AuditDbContextTests
{
    [Fact]
    public async Task AuditDbContext_CreatesAuditEntriesTable()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AuditDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.AuditEntries.Add(new AuditEntry
        {
            EntityType = "Test",
            EntityId = "1",
            Action = "Created",
            Timestamp = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var count = await db.AuditEntries.CountAsync();
        Assert.Equal(1, count);
    }
}
