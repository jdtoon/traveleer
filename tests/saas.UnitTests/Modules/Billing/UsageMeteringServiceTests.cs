using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Modules.Billing.Entities;
using saas.Modules.Billing.Services;
using Xunit;

namespace saas.Tests.Modules.Billing;

public class UsageMeteringServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private CoreDbContext _coreDb = null!;
    private UsageMeteringService _service = null!;
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(_connection)
            .Options;

        _coreDb = new CoreDbContext(options);
        await _coreDb.Database.EnsureCreatedAsync();

        _service = new UsageMeteringService(_coreDb);
    }

    public async Task DisposeAsync()
    {
        await _coreDb.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task RecordUsageAsync_CreatesNewRecord()
    {
        await _service.RecordUsageAsync(_tenantId, "api_calls", 5);

        var records = await _coreDb.UsageRecords.ToListAsync();
        Assert.Single(records);
        Assert.Equal("api_calls", records[0].Metric);
        Assert.Equal(5, records[0].Quantity);
        Assert.Equal(_tenantId, records[0].TenantId);
    }

    [Fact]
    public async Task RecordUsageAsync_UpsertsSamePeriod()
    {
        await _service.RecordUsageAsync(_tenantId, "api_calls", 3);
        await _service.RecordUsageAsync(_tenantId, "api_calls", 7);

        var records = await _coreDb.UsageRecords.ToListAsync();
        Assert.Single(records);
        Assert.Equal(10, records[0].Quantity);
    }

    [Fact]
    public async Task RecordUsageAsync_SeparatesMetrics()
    {
        await _service.RecordUsageAsync(_tenantId, "api_calls", 5);
        await _service.RecordUsageAsync(_tenantId, "storage_bytes", 1024);

        var records = await _coreDb.UsageRecords.ToListAsync();
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public async Task RecordUsageAsync_SeparatesTenants()
    {
        var tenant2 = Guid.NewGuid();
        await _service.RecordUsageAsync(_tenantId, "api_calls", 5);
        await _service.RecordUsageAsync(tenant2, "api_calls", 10);

        var records = await _coreDb.UsageRecords.ToListAsync();
        Assert.Equal(2, records.Count);
        Assert.Equal(5, records.First(r => r.TenantId == _tenantId).Quantity);
        Assert.Equal(10, records.First(r => r.TenantId == tenant2).Quantity);
    }

    [Fact]
    public async Task GetCurrentPeriodUsageAsync_ReturnsCurrentMonth()
    {
        await _service.RecordUsageAsync(_tenantId, "api_calls", 42);

        var usage = await _service.GetCurrentPeriodUsageAsync(_tenantId, "api_calls");
        Assert.Equal(42, usage);
    }

    [Fact]
    public async Task GetCurrentPeriodUsageAsync_ReturnsZeroForNoData()
    {
        var usage = await _service.GetCurrentPeriodUsageAsync(_tenantId, "nonexistent");
        Assert.Equal(0, usage);
    }

    [Fact]
    public async Task GetUsageSummaryAsync_GroupsByMetric()
    {
        await _service.RecordUsageAsync(_tenantId, "api_calls", 100);
        await _service.RecordUsageAsync(_tenantId, "storage_bytes", 2048);

        var summary = await _service.GetUsageSummaryAsync(_tenantId);

        Assert.Equal(2, summary.Count);
        Assert.Contains(summary, s => s.Metric == "api_calls" && s.TotalQuantity == 100);
        Assert.Contains(summary, s => s.Metric == "storage_bytes" && s.TotalQuantity == 2048);
    }

    [Fact]
    public async Task GetUsageSummaryAsync_DoesNotReturnOtherTenants()
    {
        var tenant2 = Guid.NewGuid();
        await _service.RecordUsageAsync(_tenantId, "api_calls", 10);
        await _service.RecordUsageAsync(tenant2, "api_calls", 999);

        var summary = await _service.GetUsageSummaryAsync(_tenantId);

        Assert.Single(summary);
        Assert.Equal(10, summary[0].TotalQuantity);
    }

    [Fact]
    public async Task RecordUsageAsync_DefaultQuantityIsOne()
    {
        await _service.RecordUsageAsync(_tenantId, "logins");

        var record = await _coreDb.UsageRecords.FirstAsync();
        Assert.Equal(1, record.Quantity);
    }

    [Fact]
    public async Task RecordUsageAsync_SetsPeriodBoundaries()
    {
        await _service.RecordUsageAsync(_tenantId, "api_calls", 1);

        var record = await _coreDb.UsageRecords.FirstAsync();
        var now = DateTime.UtcNow;
        Assert.Equal(new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc), record.PeriodStart);
        Assert.True(record.PeriodEnd > record.PeriodStart);
    }
}
