using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using saas.Data.Core;
using saas.Modules.Billing.DTOs;
using saas.Modules.Billing.Entities;
using saas.Modules.Billing.Models;
using saas.Modules.Billing.Services;
using saas.Modules.Tenancy.Entities;
using saas.Shared;
using Xunit;

namespace saas.Tests.Modules.Billing;

public class VariableChargeServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CoreDbContext _db;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _planId = Guid.NewGuid();
    private readonly StubUsageBillingService _usageBilling = new();
    private readonly StubInvoiceEngine _invoiceEngine;
    private readonly StubPaystackClient _paystackClient = new();
    private readonly StubDunningService _dunningService = new();

    public VariableChargeServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var dbOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new CoreDbContext(dbOptions);
        _db.Database.EnsureCreated();

        _invoiceEngine = new StubInvoiceEngine(_db);

        SeedTestData();
    }

    private void SeedTestData()
    {
        var plan = new Plan
        {
            Id = _planId,
            Name = "Standard",
            Slug = "standard",
            MonthlyPrice = 500m,
            BillingModel = BillingModel.Hybrid,
            IncludedSeats = 1,
            PerSeatMonthlyPrice = 250m,
            Currency = "ZAR"
        };
        _db.Plans.Add(plan);

        _db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Test Practice",
            Slug = "test-practice",
            PlanId = _planId,
            Status = TenantStatus.Active,
            ContactEmail = "test@example.com"
        });

        _db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            PlanId = _planId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            Quantity = 1, // 1 seat = 1 practice (included)
            StartDate = DateTime.UtcNow.AddMonths(-1),
            NextBillingDate = DateTime.UtcNow.AddDays(1),
            PaystackAuthorizationCode = "AUTH_test123",
            PaystackAuthorizationEmail = "test@example.com"
        });

        _db.SaveChanges();
    }

    private VariableChargeService CreateService(BillingOptions? opts = null)
    {
        opts ??= new BillingOptions
        {
            Features = new BillingFeatureToggles
            {
                PerSeatBilling = true,
                UsageBilling = true
            },
            UsageMetrics = new Dictionary<string, UsageMetricConfig>
            {
                ["medical_claims"] = new UsageMetricConfig
                {
                    DisplayName = "Medical Claims",
                    OveragePrice = 4.50m,
                    IncludedByPlan = new Dictionary<string, long?> { ["standard"] = 0 }
                }
            }
        };

        return new VariableChargeService(
            _db,
            Options.Create(opts),
            _usageBilling,
            _invoiceEngine,
            _paystackClient,
            _dunningService,
            NullLogger<VariableChargeService>.Instance);
    }

    // ── CalculateVariableChargesAsync ─────────────────────────

    [Fact]
    public async Task Calculate_NoExtraSeats_NoUsage_ReturnsEmpty()
    {
        var svc = CreateService();
        var now = DateTime.UtcNow;
        var breakdown = await svc.CalculateVariableChargesAsync(
            _tenantId, now.AddMonths(-1), now);

        Assert.False(breakdown.HasCharges);
        Assert.Equal(0m, breakdown.Total);
        Assert.Equal(0m, breakdown.SeatChargeTotal);
        Assert.Equal(0m, breakdown.UsageChargeTotal);
        Assert.Empty(breakdown.LineItems);
    }

    [Fact]
    public async Task Calculate_ExtraSeats_CalculatesSeatCharge()
    {
        // Add 2 extra practices (total 3, included 1, extra 2)
        var sub = await _db.Subscriptions.FirstAsync(s => s.TenantId == _tenantId);
        sub.Quantity = 3;
        await _db.SaveChangesAsync();

        var svc = CreateService();
        var now = DateTime.UtcNow;
        var breakdown = await svc.CalculateVariableChargesAsync(
            _tenantId, now.AddMonths(-1), now);

        Assert.True(breakdown.HasCharges);
        Assert.Equal(500m, breakdown.SeatChargeTotal); // 2 × R250
        Assert.Equal(0m, breakdown.UsageChargeTotal);  // no usage
        Assert.Equal(500m, breakdown.Total);

        var seatItem = Assert.Single(breakdown.LineItems, li => li.Type == LineItemType.Seat);
        Assert.Equal(2, seatItem.Quantity);
        Assert.Equal(250m, seatItem.UnitPrice);
    }

    [Fact]
    public async Task Calculate_UsageOnly_CalculatesUsageCharge()
    {
        // Configure usage charges to return
        _usageBilling.OverrideCharges = new Dictionary<string, UsageChargeLine>
        {
            ["medical_claims"] = new UsageChargeLine(
                MetricDisplayName: "Medical Claims",
                IncludedQuantity: 0,
                ActualQuantity: 100,
                OverageQuantity: 100,
                PricePerUnit: 4.50m,
                TotalCharge: 450m)
        };

        var svc = CreateService();
        var now = DateTime.UtcNow;
        var breakdown = await svc.CalculateVariableChargesAsync(
            _tenantId, now.AddMonths(-1), now);

        Assert.True(breakdown.HasCharges);
        Assert.Equal(0m, breakdown.SeatChargeTotal);
        Assert.Equal(450m, breakdown.UsageChargeTotal);
        Assert.Equal(450m, breakdown.Total);

        var usageItem = Assert.Single(breakdown.LineItems, li => li.Type == LineItemType.UsageCharge);
        Assert.Equal(100, usageItem.Quantity);
        Assert.Equal(4.50m, usageItem.UnitPrice);
        Assert.Equal("medical_claims", usageItem.UsageMetric);
    }

    [Fact]
    public async Task Calculate_SeatsAndUsage_CombinesCharges()
    {
        // 2 extra practices + 50 medical claims
        var sub = await _db.Subscriptions.FirstAsync(s => s.TenantId == _tenantId);
        sub.Quantity = 3;
        await _db.SaveChangesAsync();

        _usageBilling.OverrideCharges = new Dictionary<string, UsageChargeLine>
        {
            ["medical_claims"] = new UsageChargeLine(
                MetricDisplayName: "Medical Claims",
                IncludedQuantity: 0,
                ActualQuantity: 50,
                OverageQuantity: 50,
                PricePerUnit: 4.50m,
                TotalCharge: 225m)
        };

        var svc = CreateService();
        var now = DateTime.UtcNow;
        var breakdown = await svc.CalculateVariableChargesAsync(
            _tenantId, now.AddMonths(-1), now);

        Assert.True(breakdown.HasCharges);
        Assert.Equal(500m, breakdown.SeatChargeTotal);  // 2 × R250
        Assert.Equal(225m, breakdown.UsageChargeTotal); // 50 × R4.50
        Assert.Equal(725m, breakdown.Total);

        Assert.Equal(2, breakdown.LineItems.Count);
    }

    [Fact]
    public async Task Calculate_PerSeatBillingDisabled_SkipsSeats()
    {
        var sub = await _db.Subscriptions.FirstAsync(s => s.TenantId == _tenantId);
        sub.Quantity = 5;
        await _db.SaveChangesAsync();

        var opts = new BillingOptions
        {
            Features = new BillingFeatureToggles { PerSeatBilling = false, UsageBilling = false }
        };
        var svc = CreateService(opts);
        var now = DateTime.UtcNow;
        var breakdown = await svc.CalculateVariableChargesAsync(
            _tenantId, now.AddMonths(-1), now);

        Assert.False(breakdown.HasCharges);
        Assert.Equal(0m, breakdown.Total);
    }

    [Fact]
    public async Task Calculate_FlatRatePlan_NoSeatCharges()
    {
        // Switch plan to FlatRate
        var plan = await _db.Plans.FindAsync(_planId);
        plan!.BillingModel = BillingModel.FlatRate;
        await _db.SaveChangesAsync();

        var sub = await _db.Subscriptions.FirstAsync(s => s.TenantId == _tenantId);
        sub.Quantity = 5;
        await _db.SaveChangesAsync();

        var svc = CreateService();
        var now = DateTime.UtcNow;
        var breakdown = await svc.CalculateVariableChargesAsync(
            _tenantId, now.AddMonths(-1), now);

        // FlatRate plan shouldn't have seat charges regardless of quantity
        Assert.Equal(0m, breakdown.SeatChargeTotal);
    }

    [Fact]
    public async Task Calculate_NoSubscription_ReturnsEmpty()
    {
        // Remove subscription
        var sub = await _db.Subscriptions.FirstAsync(s => s.TenantId == _tenantId);
        _db.Subscriptions.Remove(sub);
        await _db.SaveChangesAsync();

        var svc = CreateService();
        var now = DateTime.UtcNow;
        var breakdown = await svc.CalculateVariableChargesAsync(
            _tenantId, now.AddMonths(-1), now);

        Assert.False(breakdown.HasCharges);
    }

    // ── ChargeVariableAsync ───────────────────────────────────

    [Fact]
    public async Task ChargeVariable_ZeroAmount_ReturnsSuccessWithoutCharging()
    {
        // 1 seat (included), no usage → zero charge
        var svc = CreateService();
        var result = await svc.ChargeVariableAsync(_tenantId);

        Assert.True(result.Success);
        Assert.False(_paystackClient.ChargedCalled);
    }

    [Fact]
    public async Task ChargeVariable_WithCharges_CreatesInvoiceAndCharges()
    {
        var sub = await _db.Subscriptions.FirstAsync(s => s.TenantId == _tenantId);
        sub.Quantity = 3; // 2 extra seats × R250 = R500
        await _db.SaveChangesAsync();

        _paystackClient.NextChargeResult = new PaystackChargeResponse
        {
            Status = "success",
            GatewayResponse = "Approved"
        };

        var svc = CreateService();
        var result = await svc.ChargeVariableAsync(_tenantId);

        Assert.True(result.Success);
        Assert.NotNull(result.InvoiceId);
        Assert.NotNull(result.PaymentId);
        Assert.True(_paystackClient.ChargedCalled);

        // Verify invoice was created and paid
        var invoice = await _db.Invoices.FindAsync(result.InvoiceId);
        Assert.NotNull(invoice);
        Assert.Equal(InvoiceStatus.Paid, invoice!.Status);

        // Verify payment was created
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == result.PaymentId);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Success, payment!.Status);
    }

    [Fact]
    public async Task ChargeVariable_NoAuthorization_ReturnsError()
    {
        var sub = await _db.Subscriptions.FirstAsync(s => s.TenantId == _tenantId);
        sub.Quantity = 3;
        sub.PaystackAuthorizationCode = null;
        await _db.SaveChangesAsync();

        var svc = CreateService();
        var result = await svc.ChargeVariableAsync(_tenantId);

        Assert.False(result.Success);
        Assert.Contains("authorization", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChargeVariable_ChargeFails_EntersDunning()
    {
        var sub = await _db.Subscriptions.FirstAsync(s => s.TenantId == _tenantId);
        sub.Quantity = 3;
        await _db.SaveChangesAsync();

        _paystackClient.NextChargeResult = new PaystackChargeResponse
        {
            Status = "failed",
            GatewayResponse = "Insufficient funds"
        };

        var svc = CreateService();
        var result = await svc.ChargeVariableAsync(_tenantId);

        Assert.False(result.Success);
        Assert.True(_dunningService.OnPaymentFailedCalled);
    }

    [Fact]
    public async Task ChargeVariable_2FARequired_ReturnsPendingUrl()
    {
        var sub = await _db.Subscriptions.FirstAsync(s => s.TenantId == _tenantId);
        sub.Quantity = 3;
        await _db.SaveChangesAsync();

        _paystackClient.NextChargeResult = new PaystackChargeResponse
        {
            Status = "send_otp",
            Paused = true,
            AuthorizationUrl = "https://paystack.com/authorize/test"
        };

        var svc = CreateService();
        var result = await svc.ChargeVariableAsync(_tenantId);

        Assert.True(result.Success);
        Assert.Equal("https://paystack.com/authorize/test", result.PaymentUrl);
    }

    // ── ChargeInvoiceAsync ────────────────────────────────────

    [Fact]
    public async Task ChargeInvoice_Success_MarksInvoicePaid()
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            InvoiceNumber = "INV-TEST-001",
            Total = 500m,
            Status = InvoiceStatus.Issued,
            Currency = "ZAR",
            IssuedDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow
        };
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        _paystackClient.NextChargeResult = new PaystackChargeResponse
        {
            Status = "success",
            GatewayResponse = "Approved"
        };

        var svc = CreateService();
        var result = await svc.ChargeInvoiceAsync(_tenantId, invoice);

        Assert.True(result.Success);

        var updated = await _db.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceStatus.Paid, updated!.Status);
    }

    [Fact]
    public async Task ChargeInvoice_ZeroAmount_ReturnsSuccessWithoutCharging()
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            InvoiceNumber = "INV-TEST-002",
            Total = 0m,
            Status = InvoiceStatus.Draft,
            Currency = "ZAR",
            IssuedDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow
        };
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        var svc = CreateService();
        var result = await svc.ChargeInvoiceAsync(_tenantId, invoice);

        Assert.True(result.Success);
        Assert.False(_paystackClient.ChargedCalled);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ── Stubs ─────────────────────────────────────────────────

    private class StubUsageBillingService : IUsageBillingService
    {
        public Dictionary<string, UsageChargeLine>? OverrideCharges { get; set; }

        public Task RecordUsageAsync(Guid tenantId, string metric, long quantity = 1) => Task.CompletedTask;
        public Task<long> GetCurrentPeriodUsageAsync(Guid tenantId, string metric) => Task.FromResult(0L);
        public Task<List<UsageSummary>> GetUsageSummaryAsync(Guid tenantId, DateTime? from = null, DateTime? to = null)
            => Task.FromResult(new List<UsageSummary>());
        public Task<Dictionary<string, UsageChargeLine>> CalculateUsageChargesAsync(Guid tenantId, DateTime periodStart, DateTime periodEnd)
            => Task.FromResult(OverrideCharges ?? new Dictionary<string, UsageChargeLine>());
        public Task<UsageBillingResult> ProcessEndOfPeriodAsync(Guid tenantId)
            => Task.FromResult(new UsageBillingResult(true));
    }

    private class StubInvoiceEngine : IInvoiceEngine
    {
        private readonly CoreDbContext _db;
        public StubInvoiceEngine(CoreDbContext db) => _db = db;

        public async Task<Invoice> GenerateSubscriptionInvoiceAsync(Guid tenantId, DateTime? periodStart = null, DateTime? periodEnd = null)
        {
            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                InvoiceNumber = $"INV-STUB-{Guid.NewGuid().ToString("N")[..6]}",
                Status = InvoiceStatus.Draft,
                Currency = "ZAR",
                IssuedDate = DateTime.UtcNow,
                DueDate = DateTime.UtcNow
            };
            _db.Invoices.Add(invoice);
            await _db.SaveChangesAsync();
            return invoice;
        }

        public async Task<Invoice> GenerateOneOffInvoiceAsync(Guid tenantId, string description, decimal amount)
        {
            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                InvoiceNumber = $"INV-STUB-{Guid.NewGuid().ToString("N")[..6]}",
                Subtotal = amount,
                Total = amount,
                Status = InvoiceStatus.Draft,
                Currency = "ZAR",
                IssuedDate = DateTime.UtcNow,
                DueDate = DateTime.UtcNow
            };
            _db.Invoices.Add(invoice);
            await _db.SaveChangesAsync();
            return invoice;
        }

        public async Task<Invoice> GenerateProrationInvoiceAsync(Guid tenantId, string description, List<InvoiceLineItem> lineItems)
        {
            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                InvoiceNumber = $"INV-STUB-{Guid.NewGuid().ToString("N")[..6]}",
                Subtotal = lineItems.Sum(l => l.Amount),
                Total = lineItems.Sum(l => l.Amount),
                Status = InvoiceStatus.Draft,
                Description = description,
                Currency = "ZAR",
                IssuedDate = DateTime.UtcNow,
                DueDate = DateTime.UtcNow
            };
            _db.Invoices.Add(invoice);
            await _db.SaveChangesAsync();
            return invoice;
        }

        public Task FinalizeInvoiceAsync(Guid invoiceId) => Task.CompletedTask;
        public Task VoidInvoiceAsync(Guid invoiceId) => Task.CompletedTask;
        public Task<string> GenerateInvoiceNumberAsync() => Task.FromResult("INV-STUB-00001");
    }

    private class StubPaystackClient : PaystackClient
    {
        public bool ChargedCalled { get; private set; }
        public PaystackChargeResponse? NextChargeResult { get; set; }

        public StubPaystackClient()
            : base(new HttpClient(new NoOpHandler()), Options.Create(new PaystackOptions { SecretKey = "sk_test" }), NullLogger<PaystackClient>.Instance)
        {
        }

        public override Task<PaystackChargeResponse?> ChargeAuthorizationAsync(
            PaystackChargeAuthorizationRequest request)
        {
            ChargedCalled = true;
            return Task.FromResult(NextChargeResult);
        }

        private class NoOpHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"status":true,"data":{}}""",
                        System.Text.Encoding.UTF8, "application/json")
                });
        }
    }

    private class StubDunningService : IDunningService
    {
        public bool OnPaymentFailedCalled { get; private set; }

        public Task OnPaymentFailedAsync(Guid tenantId, Guid? invoiceId = null)
        {
            OnPaymentFailedCalled = true;
            return Task.CompletedTask;
        }
        public Task<bool> RetryChargeAsync(Guid tenantId) => Task.FromResult(false);
        public Task ProcessGracePeriodsAsync() => Task.CompletedTask;
        public Task ReactivateAsync(Guid tenantId) => Task.CompletedTask;
    }
}
