using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Modules.Billing.Services;
using Xunit;

namespace saas.Tests.Modules.Billing;

public class InvoiceGeneratorTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CoreDbContext _db;
    private readonly InvoiceGenerator _generator;

    public InvoiceGeneratorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new CoreDbContext(options);
        _db.Database.EnsureCreated();

        _generator = new InvoiceGenerator(_db);

        SeedTestData();
    }

    private void SeedTestData()
    {
        var plan = new Plan
        {
            Id = Guid.NewGuid(),
            Name = "Pro",
            Slug = "pro",
            MonthlyPrice = 499,
            Currency = "ZAR",
            SortOrder = 1
        };
        _db.Plans.Add(plan);

        _db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Test Tenant",
            Slug = "test-tenant",
            ContactEmail = "test@example.com",
            Status = TenantStatus.Active,
            PlanId = plan.Id
        });

        _db.Subscriptions.Add(new Subscription
        {
            Id = _subscriptionId,
            TenantId = _tenantId,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow
        });

        _db.SaveChanges();
    }

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _subscriptionId = Guid.NewGuid();

    [Fact]
    public async Task GenerateAsync_CreatesInvoiceWithCorrectNumber()
    {
        var invoice = await _generator.GenerateAsync(_tenantId, _subscriptionId, 499m, "ZAR");

        Assert.NotNull(invoice);
        Assert.StartsWith("INV-", invoice.InvoiceNumber);
        Assert.Equal(499m, invoice.Amount);
        Assert.Equal("ZAR", invoice.Currency);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Equal(_tenantId, invoice.TenantId);
        Assert.Equal(_subscriptionId, invoice.SubscriptionId);
    }

    [Fact]
    public async Task GenerateAsync_SequentialNumbering()
    {
        var invoice1 = await _generator.GenerateAsync(_tenantId, _subscriptionId, 499m, "ZAR");
        var invoice2 = await _generator.GenerateAsync(_tenantId, _subscriptionId, 499m, "ZAR");
        var invoice3 = await _generator.GenerateAsync(_tenantId, _subscriptionId, 499m, "ZAR");

        var year = DateTime.UtcNow.Year;
        Assert.Equal($"INV-{year}-0001", invoice1.InvoiceNumber);
        Assert.Equal($"INV-{year}-0002", invoice2.InvoiceNumber);
        Assert.Equal($"INV-{year}-0003", invoice3.InvoiceNumber);
    }

    [Fact]
    public async Task GenerateAsync_PersistsToDatabase()
    {
        await _generator.GenerateAsync(_tenantId, _subscriptionId, 250m, "ZAR");

        var count = await _db.Invoices.CountAsync();
        Assert.Equal(1, count);

        var saved = await _db.Invoices.FirstAsync();
        Assert.Equal(250m, saved.Amount);
    }

    [Fact]
    public async Task GenerateAsync_SetsDueDateToNow()
    {
        var before = DateTime.UtcNow;
        var invoice = await _generator.GenerateAsync(_tenantId, _subscriptionId, 100m, "ZAR");
        var after = DateTime.UtcNow;

        Assert.InRange(invoice.IssuedDate, before, after);
        Assert.InRange(invoice.DueDate, before, after);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
