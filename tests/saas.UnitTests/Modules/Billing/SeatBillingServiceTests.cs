using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using saas.Data.Core;
using saas.Modules.Billing.Entities;
using saas.Modules.Billing.Services;
using saas.Modules.Tenancy.Entities;
using saas.Shared;
using Xunit;

namespace saas.Tests.Modules.Billing;

public class SeatBillingServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CoreDbContext _db;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _planId = Guid.NewGuid();
    private readonly StubCreditService _creditService = new();
    private readonly StubInvoiceEngine _invoiceEngine = new();

    public SeatBillingServiceTests()
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
            Name = "Team Pro",
            Slug = "team-pro",
            MonthlyPrice = 0,
            BillingModel = BillingModel.PerSeat,
            PerSeatMonthlyPrice = 50m,
            PerSeatAnnualPrice = 500m,
            IncludedSeats = 1,
            MaxUsers = 20
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

        _db.SaveChanges();
    }

    private void AddSubscription(int quantity = 5, BillingCycle cycle = BillingCycle.Monthly,
        DateTime? nextBillingDate = null, DateTime? startDate = null)
    {
        var start = startDate ?? DateTime.UtcNow.AddDays(-15);
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _planId,
            Status = SubscriptionStatus.Active,
            BillingCycle = cycle,
            Quantity = quantity,
            StartDate = start,
            NextBillingDate = nextBillingDate ?? start.AddDays(30)
        });
        _db.SaveChanges();
    }

    private SeatBillingService CreateService() =>
        new(_db, _creditService, _invoiceEngine,
            Options.Create(new BillingOptions()),
            NullLogger<SeatBillingService>.Instance);

    // ── UpdateSeatsAsync ──────────────────────────────────

    [Fact]
    public async Task UpdateSeats_ZeroCount_ReturnsError()
    {
        AddSubscription();
        var svc = CreateService();
        var result = await svc.UpdateSeatsAsync(_tenantId, 0);
        Assert.False(result.Success);
        Assert.Contains("at least 1", result.Error!);
    }

    [Fact]
    public async Task UpdateSeats_NoSubscription_ReturnsError()
    {
        var svc = CreateService();
        var result = await svc.UpdateSeatsAsync(_tenantId, 5);
        Assert.False(result.Success);
        Assert.Contains("No active subscription", result.Error!);
    }

    [Fact]
    public async Task UpdateSeats_SameCount_NoOpSuccess()
    {
        AddSubscription(quantity: 5);
        var svc = CreateService();
        var result = await svc.UpdateSeatsAsync(_tenantId, 5);
        Assert.True(result.Success);
        Assert.Equal(5, result.PreviousSeats);
        Assert.Equal(5, result.NewSeats);
    }

    [Fact]
    public async Task UpdateSeats_ExceedsMaxSeats_ReturnsError()
    {
        AddSubscription(quantity: 5);
        var svc = CreateService();
        var result = await svc.UpdateSeatsAsync(_tenantId, 25); // max is 20
        Assert.False(result.Success);
        Assert.Contains("Maximum seats", result.Error!);
    }

    [Fact]
    public async Task UpdateSeats_Increase_GeneratesProrationInvoice()
    {
        AddSubscription(quantity: 5);
        var svc = CreateService();
        var result = await svc.UpdateSeatsAsync(_tenantId, 8);

        Assert.True(result.Success);
        Assert.Equal(5, result.PreviousSeats);
        Assert.Equal(8, result.NewSeats);
        Assert.True(result.AmountCharged > 0);

        var sub = await _db.Subscriptions.FirstAsync(s => s.TenantId == _tenantId);
        Assert.Equal(8, sub.Quantity);

        Assert.Single(_invoiceEngine.ProrationCalls);
    }

    [Fact]
    public async Task UpdateSeats_Decrease_IssuesCredit()
    {
        AddSubscription(quantity: 8);
        var svc = CreateService();
        var result = await svc.UpdateSeatsAsync(_tenantId, 3);

        Assert.True(result.Success);
        Assert.Equal(8, result.PreviousSeats);
        Assert.Equal(3, result.NewSeats);
        Assert.True(result.CreditIssued > 0);

        var sub = await _db.Subscriptions.FirstAsync(s => s.TenantId == _tenantId);
        Assert.Equal(3, sub.Quantity);

        Assert.NotEmpty(_creditService.AddedCredits);
    }

    [Fact]
    public async Task UpdateSeats_FlatRatePlan_ReturnsError()
    {
        // Switch to a flat-rate plan
        var flatPlanId = Guid.NewGuid();
        _db.Plans.Add(new Plan
        {
            Id = flatPlanId,
            Name = "Basic",
            Slug = "basic",
            MonthlyPrice = 99,
            BillingModel = BillingModel.FlatRate
        });
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = flatPlanId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            Quantity = 1,
            StartDate = DateTime.UtcNow.AddDays(-10),
            NextBillingDate = DateTime.UtcNow.AddDays(20)
        });
        await _db.SaveChangesAsync();

        var svc = CreateService();
        var result = await svc.UpdateSeatsAsync(_tenantId, 5);
        Assert.False(result.Success);
        Assert.Contains("does not support per-seat", result.Error!);
    }

    // ── PreviewSeatChangeAsync ────────────────────────────

    [Fact]
    public async Task Preview_Increase_ShowsCorrectDetails()
    {
        AddSubscription(quantity: 3);
        var svc = CreateService();

        var preview = await svc.PreviewSeatChangeAsync(_tenantId, 7);

        Assert.True(preview.IsValid);
        Assert.Equal(3, preview.CurrentSeats);
        Assert.Equal(7, preview.NewSeats);
        Assert.Equal(4, preview.SeatDifference);
        Assert.True(preview.IsIncrease);
        Assert.True(preview.ProratedAmount > 0);
        Assert.Equal(50m, preview.PricePerSeat); // monthly per-seat price
    }

    [Fact]
    public async Task Preview_AnnualPlan_UsesAnnualPrice()
    {
        AddSubscription(quantity: 3, cycle: BillingCycle.Annual,
            startDate: DateTime.UtcNow.AddDays(-30),
            nextBillingDate: DateTime.UtcNow.AddDays(335));
        var svc = CreateService();

        var preview = await svc.PreviewSeatChangeAsync(_tenantId, 5);
        Assert.Equal(500m, preview.PricePerSeat); // annual per-seat price
    }

    [Fact]
    public async Task Preview_ExceedsMax_ReturnsError()
    {
        AddSubscription(quantity: 5);
        var svc = CreateService();

        var preview = await svc.PreviewSeatChangeAsync(_tenantId, 25);
        Assert.False(preview.IsValid);
        Assert.Contains("Maximum seats", preview.Error!);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ── Stubs ─────────────────────────────────────────────

    private class StubCreditService : ICreditService
    {
        public List<(Guid TenantId, decimal Amount)> AddedCredits { get; } = [];

        public Task<TenantCredit> AddCreditAsync(Guid tenantId, decimal amount, CreditReason reason, string? description = null)
        {
            AddedCredits.Add((tenantId, amount));
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
        public List<(Guid TenantId, string Description, List<InvoiceLineItem> Items)> ProrationCalls { get; } = [];

        public Task<Invoice> GenerateSubscriptionInvoiceAsync(Guid tenantId, DateTime? periodStart = null, DateTime? periodEnd = null)
            => Task.FromResult(new Invoice { Id = Guid.NewGuid(), TenantId = tenantId });

        public Task<Invoice> GenerateOneOffInvoiceAsync(Guid tenantId, string description, decimal amount)
            => Task.FromResult(new Invoice { Id = Guid.NewGuid(), TenantId = tenantId, Total = amount });

        public Task<Invoice> GenerateProrationInvoiceAsync(Guid tenantId, string description, List<InvoiceLineItem> lineItems)
        {
            ProrationCalls.Add((tenantId, description, lineItems));
            return Task.FromResult(new Invoice { Id = Guid.NewGuid(), TenantId = tenantId });
        }

        public Task FinalizeInvoiceAsync(Guid invoiceId) => Task.CompletedTask;
        public Task VoidInvoiceAsync(Guid invoiceId) => Task.CompletedTask;
        public Task<string> GenerateInvoiceNumberAsync() => Task.FromResult($"INV-{DateTime.UtcNow.Year}-00001");
    }
}
