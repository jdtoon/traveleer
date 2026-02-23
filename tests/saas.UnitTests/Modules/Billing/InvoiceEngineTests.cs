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

public class InvoiceEngineTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CoreDbContext _db;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _planId = Guid.NewGuid();
    private readonly StubCreditService _creditService = new();
    private readonly StubDiscountService _discountService = new();

    public InvoiceEngineTests()
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
            MonthlyPrice = 299m,
            AnnualPrice = 2990m,
            BillingModel = BillingModel.FlatRate,
            Currency = "ZAR"
        };
        _db.Plans.Add(plan);

        var tenant = new Tenant
        {
            Id = _tenantId,
            Name = "Test Tenant",
            Slug = "test",
            PlanId = _planId,
            Status = TenantStatus.Active,
            ContactEmail = "test@example.com"
        };
        _db.Tenants.Add(tenant);

        var sub = new Subscription
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            PlanId = _planId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            Quantity = 1,
            StartDate = DateTime.UtcNow.AddMonths(-1),
            NextBillingDate = DateTime.UtcNow.AddDays(1)
        };
        _db.Subscriptions.Add(sub);

        _db.SaveChanges();
    }

    private InvoiceEngine CreateService(BillingOptions? opts = null) =>
        new(_db,
            Options.Create(opts ?? new BillingOptions
            {
                Tax = new TaxOptions { Rate = 0.15m, Label = "VAT", Included = true },
                Invoice = new InvoiceOptions { Prefix = "INV", PaymentTermDays = 7 },
                Company = new CompanyOptions { Name = "Test Corp", Address = "123 Test St" }
            }),
            _creditService,
            _discountService,
            NullLogger<InvoiceEngine>.Instance);

    // ── GenerateOneOffInvoiceAsync ────────────────────────

    [Fact]
    public async Task GenerateOneOff_CreatesInvoiceWithLineItem()
    {
        var svc = CreateService();
        var inv = await svc.GenerateOneOffInvoiceAsync(_tenantId, "Consulting", 500m);

        Assert.Equal(_tenantId, inv.TenantId);
        Assert.Equal(InvoiceStatus.Draft, inv.Status);
        Assert.Equal(500m, inv.Subtotal);
        Assert.Equal("Consulting", inv.Description);
        Assert.StartsWith("INV-", inv.InvoiceNumber);

        var lineItems = await _db.InvoiceLineItems.Where(li => li.InvoiceId == inv.Id).ToListAsync();
        Assert.Contains(lineItems, li => li.Type == LineItemType.OneOff && li.Amount == 500m);
    }

    [Fact]
    public async Task GenerateOneOff_TaxInclusive_CalculatesTaxCorrectly()
    {
        var svc = CreateService();
        var inv = await svc.GenerateOneOffInvoiceAsync(_tenantId, "Item", 115m);

        // VAT-inclusive: tax = 115 * 0.15 / 1.15 = 15
        Assert.Equal(15m, inv.TaxAmount);
        Assert.Equal(115m, inv.Total); // Total stays same for inclusive
    }

    [Fact]
    public async Task GenerateOneOff_TaxExclusive_AddsTaxOnTop()
    {
        var svc = CreateService(new BillingOptions
        {
            Tax = new TaxOptions { Rate = 0.15m, Included = false },
            Invoice = new InvoiceOptions { Prefix = "INV" }
        });

        var inv = await svc.GenerateOneOffInvoiceAsync(_tenantId, "Item", 100m);

        Assert.Equal(15m, inv.TaxAmount);
        Assert.Equal(115m, inv.Total); // 100 + 15
    }

    // ── GenerateSubscriptionInvoiceAsync ──────────────────

    [Fact]
    public async Task GenerateSubscription_BasePlanCharge()
    {
        var svc = CreateService();
        var inv = await svc.GenerateSubscriptionInvoiceAsync(_tenantId);

        Assert.Equal(_tenantId, inv.TenantId);
        Assert.Equal(InvoiceStatus.Draft, inv.Status);

        var lineItems = await _db.InvoiceLineItems
            .Where(li => li.InvoiceId == inv.Id && li.Type == LineItemType.Subscription)
            .ToListAsync();

        Assert.Single(lineItems);
        Assert.Equal(299m, lineItems[0].Amount);
    }

    [Fact]
    public async Task GenerateSubscription_SnapshotsCompanyDetails()
    {
        var svc = CreateService();
        var inv = await svc.GenerateSubscriptionInvoiceAsync(_tenantId);

        Assert.Equal("Test Corp", inv.CompanyName);
        Assert.Equal("123 Test St", inv.CompanyAddress);
    }

    [Fact]
    public async Task GenerateSubscription_SetsBillingPeriod()
    {
        var svc = CreateService();
        var now = DateTime.UtcNow;
        var inv = await svc.GenerateSubscriptionInvoiceAsync(_tenantId, now, now.AddMonths(1));

        Assert.NotNull(inv.BillingPeriodStart);
        Assert.NotNull(inv.BillingPeriodEnd);
    }

    [Fact]
    public async Task GenerateSubscription_WithDiscounts_AppliesDiscount()
    {
        _discountService.DiscountAmount = 50m;
        var svc = CreateService(new BillingOptions
        {
            Tax = new TaxOptions { Rate = 0.15m, Included = true },
            Invoice = new InvoiceOptions { Prefix = "INV" },
            Features = new BillingFeatureToggles { Discounts = true }
        });

        var inv = await svc.GenerateSubscriptionInvoiceAsync(_tenantId);

        Assert.Equal(50m, inv.DiscountAmount);
        Assert.True(_discountService.DecrementCalled);
    }

    // ── GenerateProrationInvoiceAsync ─────────────────────

    [Fact]
    public async Task GenerateProration_UsesProvidedLineItems()
    {
        var svc = CreateService();
        var items = new List<InvoiceLineItem>
        {
            new()
            {
                Type = LineItemType.Proration,
                Description = "Seat upgrade",
                Quantity = 2,
                UnitPrice = 25m,
                Amount = 50m,
                SortOrder = 0
            }
        };

        var inv = await svc.GenerateProrationInvoiceAsync(_tenantId, "Seat change", items);

        Assert.Equal(50m, inv.Subtotal);
        Assert.Equal(50m, inv.Total);
        Assert.Equal("Seat change", inv.Description);

        var savedItems = await _db.InvoiceLineItems.Where(li => li.InvoiceId == inv.Id).ToListAsync();
        Assert.Single(savedItems);
        Assert.Equal(LineItemType.Proration, savedItems[0].Type);
    }

    // ── FinalizeInvoiceAsync ──────────────────────────────

    [Fact]
    public async Task Finalize_SetsStatusToIssued()
    {
        var svc = CreateService();
        var inv = await svc.GenerateOneOffInvoiceAsync(_tenantId, "Test", 100m);

        await svc.FinalizeInvoiceAsync(inv.Id);

        var fresh = await _db.Invoices.FindAsync(inv.Id);
        Assert.Equal(InvoiceStatus.Issued, fresh!.Status);
    }

    // ── VoidInvoiceAsync ──────────────────────────────────

    [Fact]
    public async Task Void_SetsStatusToCancelled()
    {
        var svc = CreateService();
        var inv = await svc.GenerateOneOffInvoiceAsync(_tenantId, "Test", 100m);

        await svc.VoidInvoiceAsync(inv.Id);

        var fresh = await _db.Invoices.FindAsync(inv.Id);
        Assert.Equal(InvoiceStatus.Cancelled, fresh!.Status);
    }

    // ── GenerateInvoiceNumberAsync ────────────────────────

    [Fact]
    public async Task GenerateNumber_StartsFromOne()
    {
        var svc = CreateService();
        var number = await svc.GenerateInvoiceNumberAsync();
        Assert.EndsWith("-00001", number);
    }

    [Fact]
    public async Task GenerateNumber_IncrementsSequence()
    {
        // Add an existing invoice with number INV-{year}-00001
        _db.Invoices.Add(new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            InvoiceNumber = $"INV-{DateTime.UtcNow.Year}-00001",
            Status = InvoiceStatus.Draft,
            Total = 0,
            IssuedDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var svc = CreateService();
        var number = await svc.GenerateInvoiceNumberAsync();
        Assert.EndsWith("-00002", number);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ── Stubs ─────────────────────────────────────────────

    private class StubCreditService : ICreditService
    {
        public Task<TenantCredit> AddCreditAsync(Guid tenantId, decimal amount, CreditReason reason, string? description = null)
            => Task.FromResult(new TenantCredit { Id = Guid.NewGuid(), TenantId = tenantId, Amount = amount, RemainingAmount = amount, Reason = reason });

        public Task<decimal> ApplyCreditsToInvoiceAsync(Guid tenantId, Invoice invoice) => Task.FromResult(0m);
        public Task<decimal> GetBalanceAsync(Guid tenantId) => Task.FromResult(0m);
        public Task<List<TenantCredit>> GetLedgerAsync(Guid tenantId) => Task.FromResult(new List<TenantCredit>());
    }

    private class StubDiscountService : IDiscountService
    {
        public decimal DiscountAmount { get; set; }
        public bool DecrementCalled { get; set; }

        public Task<DiscountValidation> ValidateCodeAsync(string code, Guid tenantId, Guid? planId = null)
            => Task.FromResult(new DiscountValidation(true));

        public Task<TenantDiscount> ApplyAsync(Guid tenantId, string code)
            => Task.FromResult(new TenantDiscount { Id = Guid.NewGuid(), TenantId = tenantId, IsActive = true });

        public Task<decimal> CalculateDiscountAsync(Guid tenantId, decimal subtotal)
            => Task.FromResult(Math.Min(DiscountAmount, subtotal));

        public Task DecrementCyclesAsync(Guid tenantId)
        {
            DecrementCalled = true;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(Guid tenantId, Guid discountId) => Task.CompletedTask;
    }
}
