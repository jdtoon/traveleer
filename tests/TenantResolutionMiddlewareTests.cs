using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Infrastructure.Middleware;
using saas.Shared;
using Xunit;

namespace saas.Tests;

public class TenantResolutionMiddlewareTests
{
    [Fact]
    public async Task TenantResolutionMiddleware_SetsTenantContext_ForValidTenant()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new CoreDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var plan = new Plan { Id = Guid.NewGuid(), Name = "Free", Slug = "free", MonthlyPrice = 0, SortOrder = 0 };
        db.Plans.Add(plan);
        db.Tenants.Add(new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Test Tenant",
            Slug = "testcorp",
            ContactEmail = "test@test.com",
            Status = TenantStatus.Active,
            PlanId = plan.Id
        });
        await db.SaveChangesAsync();

        var context = new DefaultHttpContext();
        context.Request.Path = "/testcorp/notes";

        var tenantContext = new TenantContext();
        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, tenantContext, db);

        Assert.True(tenantContext.IsTenantRequest);
        Assert.Equal("testcorp", tenantContext.Slug);
        Assert.Equal("Test Tenant", tenantContext.TenantName);
        Assert.Equal("free", tenantContext.PlanSlug);
    }

    [Fact]
    public async Task TenantResolutionMiddleware_PassesThrough_ForNonTenantRoutes()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new CoreDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var context = new DefaultHttpContext();
        context.Request.Path = "/health";

        var tenantContext = new TenantContext();
        var called = false;
        var middleware = new TenantResolutionMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, tenantContext, db);

        Assert.True(called);
        Assert.False(tenantContext.IsTenantRequest);
    }
}
