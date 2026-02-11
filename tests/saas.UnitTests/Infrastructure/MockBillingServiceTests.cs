using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using saas.Data.Core;
using saas.Infrastructure.Services;
using saas.Shared;
using Xunit;

namespace saas.Tests.Infrastructure;

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

    [Fact]
    public async Task ChangePlanAsync_TrialingSubscription_UpdatesInPlace()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new CoreDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var starterPlan = new Plan { Id = Guid.NewGuid(), Name = "Starter", Slug = "starter", MonthlyPrice = 199, SortOrder = 1 };
        var proPlan = new Plan { Id = Guid.NewGuid(), Name = "Professional", Slug = "professional", MonthlyPrice = 499, SortOrder = 2 };
        var tenantId = Guid.NewGuid();

        db.Plans.AddRange(starterPlan, proPlan);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Test Tenant",
            Slug = "test",
            ContactEmail = "test@test.com",
            Status = TenantStatus.Active,
            PlanId = starterPlan.Id
        });
        // Simulate a Trialing subscription (as TenantProvisionerService creates)
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PlanId = starterPlan.Id,
            Status = SubscriptionStatus.Trialing,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow,
            NextBillingDate = DateTime.UtcNow.AddDays(14)
        });
        await db.SaveChangesAsync();

        var service = new MockBillingService(db, NullLogger<MockBillingService>.Instance);
        var result = await service.ChangePlanAsync(tenantId, proPlan.Id);

        Assert.True(result.Success);
        // Should still have exactly 1 subscription (updated in-place, not a new row)
        Assert.Equal(1, await db.Subscriptions.CountAsync());

        var sub = await db.Subscriptions.FirstAsync();
        Assert.Equal(proPlan.Id, sub.PlanId);
        Assert.Equal(SubscriptionStatus.Active, sub.Status);
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_TrialingSubscription_ReturnsValid()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new CoreDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var starterPlan = new Plan { Id = Guid.NewGuid(), Name = "Starter", Slug = "starter", MonthlyPrice = 199, SortOrder = 1 };
        var proPlan = new Plan { Id = Guid.NewGuid(), Name = "Professional", Slug = "professional", MonthlyPrice = 499, SortOrder = 2 };
        var tenantId = Guid.NewGuid();

        db.Plans.AddRange(starterPlan, proPlan);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Test Tenant",
            Slug = "test",
            ContactEmail = "test@test.com",
            Status = TenantStatus.Active,
            PlanId = starterPlan.Id
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PlanId = starterPlan.Id,
            Status = SubscriptionStatus.Trialing,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow,
            NextBillingDate = DateTime.UtcNow.AddDays(14)
        });
        await db.SaveChangesAsync();

        var service = new MockBillingService(db, NullLogger<MockBillingService>.Instance);
        var preview = await service.PreviewPlanChangeAsync(tenantId, proPlan.Id);

        Assert.True(preview.IsValid);
        Assert.True(preview.IsUpgrade);
        Assert.Equal("Starter", preview.CurrentPlanName);
        Assert.Equal("Professional", preview.NewPlanName);
    }
}
