using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using saas.Data.Core;
using saas.Modules.Billing.Entities;
using saas.Modules.Billing.Services;
using saas.Modules.Tenancy.Entities;
using Xunit;

namespace saas.Tests.Modules.Billing;

public class AddOnServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CoreDbContext _db;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _planId = Guid.NewGuid();
    private readonly Guid _monthlyAddOnId = Guid.NewGuid();
    private readonly Guid _oneOffAddOnId = Guid.NewGuid();
    private readonly Guid _inactiveAddOnId = Guid.NewGuid();
    private readonly StubCreditService _creditService = new();
    private readonly StubInvoiceEngine _invoiceEngine = new();

    public AddOnServiceTests()
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
            Id = _planId,
            Name = "Pro",
            Slug = "pro",
            MonthlyPrice = 299,
            BillingModel = BillingModel.FlatRate
        };
        _db.Plans.Add(plan);

        _db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Test Tenant",
            Slug = "test",
            PlanId = _planId,
            Status = TenantStatus.Active,
            ContactEmail = "test@example.com"
        });

        _db.AddOns.AddRange(
            new AddOn { Id = _monthlyAddOnId, Name = "Extra Storage", Slug = "extra-storage", Price = 49m, Currency = "ZAR", BillingInterval = AddOnInterval.Monthly, IsActive = true, SortOrder = 1 },
            new AddOn { Id = _oneOffAddOnId, Name = "Setup", Slug = "setup", Price = 500m, Currency = "ZAR", BillingInterval = AddOnInterval.OneOff, IsActive = true, SortOrder = 2 },
            new AddOn { Id = _inactiveAddOnId, Name = "Old Feature", Slug = "old-feature", Price = 99m, Currency = "ZAR", BillingInterval = AddOnInterval.Monthly, IsActive = false, SortOrder = 3 }
        );

        _db.SaveChanges();
    }

    private AddOnService CreateService() =>
        new(_db, _creditService, _invoiceEngine, NullLogger<AddOnService>.Instance);

    // ── SubscribeAsync ────────────────────────────────────

    [Fact]
    public async Task Subscribe_ValidAddOn_CreatesTenantAddOn()
    {
        var svc = CreateService();
        var result = await svc.SubscribeAsync(_tenantId, _monthlyAddOnId);

        Assert.Equal(_tenantId, result.TenantId);
        Assert.Equal(_monthlyAddOnId, result.AddOnId);
        Assert.Equal(1, result.Quantity);
        Assert.NotEqual(default, result.ActivatedAt);
    }

    [Fact]
    public async Task Subscribe_OneOffAddOn_GeneratesInvoice()
    {
        var svc = CreateService();
        await svc.SubscribeAsync(_tenantId, _oneOffAddOnId);

        Assert.Single(_invoiceEngine.GeneratedOneOff);
        Assert.Equal(_tenantId, _invoiceEngine.GeneratedOneOff[0].TenantId);
        Assert.Equal(500m, _invoiceEngine.GeneratedOneOff[0].Amount);
    }

    [Fact]
    public async Task Subscribe_MonthlyAddOn_DoesNotGenerateInvoice()
    {
        var svc = CreateService();
        await svc.SubscribeAsync(_tenantId, _monthlyAddOnId);

        Assert.Empty(_invoiceEngine.GeneratedOneOff);
    }

    [Fact]
    public async Task Subscribe_InactiveAddOn_Throws()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SubscribeAsync(_tenantId, _inactiveAddOnId));
    }

    [Fact]
    public async Task Subscribe_AlreadySubscribed_Throws()
    {
        var svc = CreateService();
        await svc.SubscribeAsync(_tenantId, _monthlyAddOnId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SubscribeAsync(_tenantId, _monthlyAddOnId));
    }

    [Fact]
    public async Task Subscribe_NonExistentAddOn_Throws()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SubscribeAsync(_tenantId, Guid.NewGuid()));
    }

    // ── UnsubscribeAsync ──────────────────────────────────

    [Fact]
    public async Task Unsubscribe_ActiveSubscription_SetsDeactivatedAt()
    {
        var svc = CreateService();
        await svc.SubscribeAsync(_tenantId, _monthlyAddOnId);

        await svc.UnsubscribeAsync(_tenantId, _monthlyAddOnId);

        var ta = await _db.TenantAddOns.FirstAsync(a => a.AddOnId == _monthlyAddOnId);
        Assert.NotNull(ta.DeactivatedAt);
    }

    [Fact]
    public async Task Unsubscribe_RecurringAddOn_IssuesCredit()
    {
        var svc = CreateService();
        await svc.SubscribeAsync(_tenantId, _monthlyAddOnId);

        await svc.UnsubscribeAsync(_tenantId, _monthlyAddOnId);

        // Should have issued some credit (prorated)
        Assert.NotEmpty(_creditService.AddedCredits);
    }

    [Fact]
    public async Task Unsubscribe_NotSubscribed_Throws()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UnsubscribeAsync(_tenantId, _monthlyAddOnId));
    }

    // ── ListAvailableAsync ────────────────────────────────

    [Fact]
    public async Task ListAvailable_ExcludesSubscribedAndInactive()
    {
        var svc = CreateService();
        await svc.SubscribeAsync(_tenantId, _monthlyAddOnId);

        var available = await svc.ListAvailableAsync(_tenantId);

        // Only the one-off should be available (monthly subscribed, inactive excluded)
        Assert.Single(available);
        Assert.Equal(_oneOffAddOnId, available[0].Id);
    }

    [Fact]
    public async Task ListAvailable_NoSubscriptions_ReturnsAllActive()
    {
        var svc = CreateService();
        var available = await svc.ListAvailableAsync(_tenantId);

        Assert.Equal(2, available.Count); // monthly + one-off (inactive excluded)
    }

    // ── ListActiveAsync ───────────────────────────────────

    [Fact]
    public async Task ListActive_ReturnsOnlyActive()
    {
        var svc = CreateService();
        await svc.SubscribeAsync(_tenantId, _monthlyAddOnId);

        var active = await svc.ListActiveAsync(_tenantId);
        Assert.Single(active);
        Assert.Equal(_monthlyAddOnId, active[0].AddOnId);
    }

    [Fact]
    public async Task ListActive_AfterUnsubscribe_ReturnsEmpty()
    {
        var svc = CreateService();
        await svc.SubscribeAsync(_tenantId, _monthlyAddOnId);
        await svc.UnsubscribeAsync(_tenantId, _monthlyAddOnId);

        var active = await svc.ListActiveAsync(_tenantId);
        Assert.Empty(active);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ── Stubs ─────────────────────────────────────────────

    private class StubCreditService : ICreditService
    {
        public List<(Guid TenantId, decimal Amount, CreditReason Reason)> AddedCredits { get; } = [];

        public Task<TenantCredit> AddCreditAsync(Guid tenantId, decimal amount, CreditReason reason, string? description = null)
        {
            AddedCredits.Add((tenantId, amount, reason));
            return Task.FromResult(new TenantCredit
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Amount = amount,
                RemainingAmount = amount,
                Reason = reason
            });
        }

        public Task<decimal> ApplyCreditsToInvoiceAsync(Guid tenantId, Invoice invoice) => Task.FromResult(0m);
        public Task<decimal> GetBalanceAsync(Guid tenantId) => Task.FromResult(0m);
        public Task<List<TenantCredit>> GetLedgerAsync(Guid tenantId) => Task.FromResult(new List<TenantCredit>());
    }

    private class StubInvoiceEngine : IInvoiceEngine
    {
        public List<(Guid TenantId, string Description, decimal Amount)> GeneratedOneOff { get; } = [];

        public Task<Invoice> GenerateSubscriptionInvoiceAsync(Guid tenantId, DateTime? periodStart = null, DateTime? periodEnd = null)
            => Task.FromResult(new Invoice { Id = Guid.NewGuid(), TenantId = tenantId });

        public Task<Invoice> GenerateOneOffInvoiceAsync(Guid tenantId, string description, decimal amount)
        {
            GeneratedOneOff.Add((tenantId, description, amount));
            return Task.FromResult(new Invoice { Id = Guid.NewGuid(), TenantId = tenantId, Total = amount });
        }

        public Task<Invoice> GenerateProrationInvoiceAsync(Guid tenantId, string description, List<InvoiceLineItem> lineItems)
            => Task.FromResult(new Invoice { Id = Guid.NewGuid(), TenantId = tenantId });

        public Task FinalizeInvoiceAsync(Guid invoiceId) => Task.CompletedTask;
        public Task VoidInvoiceAsync(Guid invoiceId) => Task.CompletedTask;
        public Task<string> GenerateInvoiceNumberAsync() => Task.FromResult($"INV-{DateTime.UtcNow.Year}-00001");
    }
}
