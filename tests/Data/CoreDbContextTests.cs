using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using Xunit;

namespace saas.Tests.Data;

public class CoreDbContextTests
{
    [Fact]
    public async Task CoreDbContext_CreatesAllTablesAndKeys()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new CoreDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var plan = new Plan
        {
            Id = Guid.NewGuid(),
            Name = "Free",
            Slug = "free",
            MonthlyPrice = 0,
            SortOrder = 0
        };

        db.Plans.Add(plan);
        await db.SaveChangesAsync();

        db.Tenants.Add(new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Slug = "test",
            ContactEmail = "test@test.com",
            Status = TenantStatus.Active,
            PlanId = plan.Id
        });

        await db.SaveChangesAsync();

        var tenantCount = await db.Tenants.CountAsync();
        Assert.Equal(1, tenantCount);
    }
}
