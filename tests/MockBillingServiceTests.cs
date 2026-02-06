using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using saas.Data.Core;
using saas.Infrastructure.Services;
using saas.Shared;
using Xunit;

namespace saas.Tests;

public class MockBillingServiceTests
{
    [Fact]
    public async Task InitializeSubscriptionAsync_CreatesSubscription()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new CoreDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var plan = new Plan { Id = Guid.NewGuid(), Name = "Free", Slug = "free", MonthlyPrice = 0, SortOrder = 0 };
        var tenantId = Guid.NewGuid();

        db.Plans.Add(plan);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Test Tenant",
            Slug = "test",
            ContactEmail = "test@test.com",
            Status = TenantStatus.Active,
            PlanId = plan.Id
        });
        await db.SaveChangesAsync();

        var service = new MockBillingService(db, NullLogger<MockBillingService>.Instance);
        var result = await service.InitializeSubscriptionAsync(new SubscriptionInitRequest(
            tenantId,
            "test@test.com",
            plan.Id,
            BillingCycle.Monthly
        ));

        Assert.True(result.Success);
        Assert.Equal(1, await db.Subscriptions.CountAsync());
    }
}
