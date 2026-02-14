using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Data.Tenant;
using saas.Modules.TenantAdmin.Services;
using saas.Modules.Tenancy.Entities;
using saas.Shared;
using Xunit;

namespace saas.Tests.Modules.TenantAdmin;

public class TenantLifecycleServiceTests : IAsyncLifetime
{
    private SqliteConnection _coreConnection = null!;
    private SqliteConnection _tenantConnection = null!;
    private CoreDbContext _coreDb = null!;
    private TenantDbContext _tenantDb = null!;
    private TenantLifecycleService _service = null!;
    private Guid _tenantId;

    public async Task InitializeAsync()
    {
        _coreConnection = new SqliteConnection("Data Source=:memory:");
        await _coreConnection.OpenAsync();

        _tenantConnection = new SqliteConnection("Data Source=:memory:");
        await _tenantConnection.OpenAsync();

        var coreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(_coreConnection)
            .Options;

        var tenantOptions = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_tenantConnection)
            .Options;

        _coreDb = new CoreDbContext(coreOptions);
        _tenantDb = new TenantDbContext(tenantOptions);

        await _coreDb.Database.EnsureCreatedAsync();
        await _tenantDb.Database.EnsureCreatedAsync();

        // Seed a plan (required FK for Tenant)
        var planId = Guid.NewGuid();
        _coreDb.Plans.Add(new Plan
        {
            Id = planId,
            Name = "Free",
            Slug = "free",
            MonthlyPrice = 0,
            AnnualPrice = 0,
            MaxUsers = 5,
            MaxRequestsPerMinute = 60,
            SortOrder = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        // Seed tenant
        _tenantId = Guid.NewGuid();
        _coreDb.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Test Corp",
            Slug = "testcorp",
            ContactEmail = "admin@testcorp.com",
            Status = TenantStatus.Active,
            PlanId = planId,
            CreatedAt = DateTime.UtcNow
        });
        await _coreDb.SaveChangesAsync();

        var tenantContext = new FakeTenantContext("testcorp", _tenantId);
        _service = new TenantLifecycleService(_coreDb, _tenantDb, tenantContext);
    }

    public async Task DisposeAsync()
    {
        await _coreDb.DisposeAsync();
        await _tenantDb.DisposeAsync();
        await _coreConnection.DisposeAsync();
        await _tenantConnection.DisposeAsync();
    }

    [Fact]
    public async Task ExportTenantDataAsync_ReturnsNonEmptyData()
    {
        var data = await _service.ExportTenantDataAsync();

        Assert.NotEmpty(data);
        var json = System.Text.Encoding.UTF8.GetString(data);
        Assert.Contains("testcorp", json);
        Assert.Contains("Test Corp", json);
    }

    [Fact]
    public async Task RequestDeletionAsync_SetsDeletionFlags()
    {
        var result = await _service.RequestDeletionAsync(30);

        Assert.True(result);
        var tenant = await _coreDb.Tenants.FindAsync(_tenantId);
        Assert.True(tenant!.IsDeleted);
        Assert.NotNull(tenant.DeletedAt);
        Assert.NotNull(tenant.ScheduledDeletionAt);
        Assert.True(tenant.ScheduledDeletionAt > DateTime.UtcNow.AddDays(29));
    }

    [Fact]
    public async Task CancelDeletionAsync_ClearsDeletionFlags()
    {
        await _service.RequestDeletionAsync(30);
        var result = await _service.CancelDeletionAsync();

        Assert.True(result);
        var tenant = await _coreDb.Tenants.FindAsync(_tenantId);
        Assert.False(tenant!.IsDeleted);
        Assert.Null(tenant.DeletedAt);
        Assert.Null(tenant.ScheduledDeletionAt);
    }

    [Fact]
    public async Task RequestDeletionAsync_ReturnsFalseForUnknownTenant()
    {
        var tenantContext = new FakeTenantContext("nonexistent", Guid.NewGuid());
        var service = new TenantLifecycleService(_coreDb, _tenantDb, tenantContext);

        var result = await service.RequestDeletionAsync();
        Assert.False(result);
    }

    [Fact]
    public async Task PermanentlyDeleteTenantAsync_RemovesTenant()
    {
        var result = await _service.PermanentlyDeleteTenantAsync(_tenantId);

        Assert.True(result);
        var tenant = await _coreDb.Tenants.FindAsync(_tenantId);
        Assert.Null(tenant);
    }

    [Fact]
    public async Task RequestDeletionAsync_CustomGracePeriod()
    {
        var result = await _service.RequestDeletionAsync(7);

        Assert.True(result);
        var tenant = await _coreDb.Tenants.FindAsync(_tenantId);
        Assert.True(tenant!.ScheduledDeletionAt < DateTime.UtcNow.AddDays(8));
        Assert.True(tenant.ScheduledDeletionAt > DateTime.UtcNow.AddDays(6));
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    private class FakeTenantContext : ITenantContext
    {
        public FakeTenantContext(string slug, Guid tenantId)
        {
            Slug = slug;
            TenantId = tenantId;
        }
        public string? Slug { get; }
        public Guid? TenantId { get; }
        public string? PlanSlug => "test";
        public string? TenantName => "Test Tenant";
        public bool IsTenantRequest => true;
    }
}
