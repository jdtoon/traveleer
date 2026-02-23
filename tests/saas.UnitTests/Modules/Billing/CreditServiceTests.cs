using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using saas.Data.Core;
using saas.Modules.Billing.Entities;
using saas.Modules.Billing.Services;
using Xunit;

namespace saas.Tests.Modules.Billing;

public class CreditServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CoreDbContext _db;
    private readonly Guid _tenantId = Guid.NewGuid();

    public CreditServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var dbOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new CoreDbContext(dbOptions);
        _db.Database.EnsureCreated();

        SeedTestData();
    }

    private void SeedTestData()
    {
        var plan = new Plan
        {
            Id = Guid.NewGuid(),
            Name = "Pro",
            Slug = "pro",
            MonthlyPrice = 299,
            BillingModel = BillingModel.FlatRate
        };
        _db.Plans.Add(plan);

        _db.Tenants.Add(new saas.Modules.Tenancy.Entities.Tenant
        {
            Id = _tenantId,
            Name = "Test Tenant",
            Slug = "test",
            PlanId = plan.Id,
            Status = saas.Modules.Tenancy.Entities.TenantStatus.Active,
            ContactEmail = "test@example.com"
        });

        _db.SaveChanges();
    }

    private CreditService CreateService() =>
        new(_db, NullLogger<CreditService>.Instance);

    [Fact]
    public async Task AddCredit_ValidAmount_ReturnsCredit()
    {
        var svc = CreateService();
        var credit = await svc.AddCreditAsync(_tenantId, 100m, CreditReason.Manual, "Test credit");

        Assert.Equal(100m, credit.Amount);
        Assert.Equal(100m, credit.RemainingAmount);
        Assert.Equal(CreditReason.Manual, credit.Reason);
        Assert.Equal("Test credit", credit.Description);
        Assert.Equal(_tenantId, credit.TenantId);
    }

    [Fact]
    public async Task AddCredit_ZeroAmount_Throws()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.AddCreditAsync(_tenantId, 0, CreditReason.Manual));
    }

    [Fact]
    public async Task AddCredit_NegativeAmount_Throws()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.AddCreditAsync(_tenantId, -50m, CreditReason.Manual));
    }

    [Fact]
    public async Task GetBalance_MultipleCreditsSomeConsumed_ReturnsRemainingOnly()
    {
        var svc = CreateService();
        await svc.AddCreditAsync(_tenantId, 100m, CreditReason.Manual);
        await svc.AddCreditAsync(_tenantId, 50m, CreditReason.Promotional);

        // Consume first credit partially
        var credits = await _db.TenantCredits.OrderBy(c => c.CreatedAt).ToListAsync();
        credits[0].RemainingAmount = 30m;
        await _db.SaveChangesAsync();

        var balance = await svc.GetBalanceAsync(_tenantId);
        Assert.Equal(80m, balance); // 30 + 50
    }

    [Fact]
    public async Task GetBalance_NoCredits_ReturnsZero()
    {
        var svc = CreateService();
        var balance = await svc.GetBalanceAsync(_tenantId);
        Assert.Equal(0m, balance);
    }

    [Fact]
    public async Task ApplyCredits_FullyCoverInvoice_ReturnsFullAmount()
    {
        var svc = CreateService();
        await svc.AddCreditAsync(_tenantId, 200m, CreditReason.Manual);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            InvoiceNumber = "INV-001",
            Total = 100m,
            IssuedDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(30)
        };
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        var applied = await svc.ApplyCreditsToInvoiceAsync(_tenantId, invoice);
        await _db.SaveChangesAsync(); // Persist changes so we can query line items

        Assert.Equal(100m, applied);
        Assert.Equal(0m, invoice.Total);
        Assert.Equal(100m, invoice.CreditApplied);

        // Check credit line item was added
        var lineItems = await _db.InvoiceLineItems.Where(li => li.InvoiceId == invoice.Id).ToListAsync();
        Assert.Single(lineItems);
        Assert.Equal(LineItemType.Credit, lineItems[0].Type);
        Assert.Equal(-100m, lineItems[0].Amount);
    }

    [Fact]
    public async Task ApplyCredits_PartiallyCoverInvoice_UsesAllCredits()
    {
        var svc = CreateService();
        await svc.AddCreditAsync(_tenantId, 40m, CreditReason.Manual);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            InvoiceNumber = "INV-002",
            Total = 100m,
            IssuedDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(30)
        };
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        var applied = await svc.ApplyCreditsToInvoiceAsync(_tenantId, invoice);

        Assert.Equal(40m, applied);
        Assert.Equal(60m, invoice.Total);
        Assert.Equal(40m, invoice.CreditApplied);
    }

    [Fact]
    public async Task ApplyCredits_FIFOOrder_ConsumeOldestFirst()
    {
        var svc = CreateService();

        // Add first credit
        var c1 = await svc.AddCreditAsync(_tenantId, 30m, CreditReason.Manual, "First");
        // Add second credit
        var c2 = await svc.AddCreditAsync(_tenantId, 50m, CreditReason.Promotional, "Second");

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            InvoiceNumber = "INV-003",
            Total = 40m,
            IssuedDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(30)
        };
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        await svc.ApplyCreditsToInvoiceAsync(_tenantId, invoice);

        // First credit should be fully consumed, second partially
        var credits = await _db.TenantCredits.OrderBy(c => c.CreatedAt).ToListAsync();
        Assert.Equal(0m, credits[0].RemainingAmount);
        Assert.NotNull(credits[0].ConsumedAt);
        Assert.Equal(40m, credits[1].RemainingAmount); // 50 - 10
    }

    [Fact]
    public async Task ApplyCredits_ZeroTotalInvoice_NoCreditsApplied()
    {
        var svc = CreateService();
        await svc.AddCreditAsync(_tenantId, 100m, CreditReason.Manual);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            InvoiceNumber = "INV-004",
            Total = 0m,
            IssuedDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(30)
        };

        var applied = await svc.ApplyCreditsToInvoiceAsync(_tenantId, invoice);
        Assert.Equal(0m, applied);

        var balance = await svc.GetBalanceAsync(_tenantId);
        Assert.Equal(100m, balance);
    }

    [Fact]
    public async Task GetLedger_ReturnsDescendingOrder()
    {
        var svc = CreateService();
        await svc.AddCreditAsync(_tenantId, 10m, CreditReason.Manual, "First");
        await svc.AddCreditAsync(_tenantId, 20m, CreditReason.Refund, "Second");

        var ledger = await svc.GetLedgerAsync(_tenantId);

        Assert.Equal(2, ledger.Count);
        Assert.Equal("Second", ledger[0].Description);
        Assert.Equal("First", ledger[1].Description);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
