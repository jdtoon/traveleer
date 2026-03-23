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

public class DunningServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CoreDbContext _db;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _planId = Guid.NewGuid();
    private readonly StubEmailService _emailService = new();

    public DunningServiceTests()
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

        _db.SaveChanges();
    }

    private DunningService CreateService(int graceDays = 3) =>
        new(_db,
            Options.Create(new BillingOptions
            {
                GracePeriod = new GracePeriodOptions { Days = graceDays, DunningIntervalHours = 72 }
            }),
            _emailService,
            new Lazy<IVariableChargeService>(() => new StubVariableChargeService()),
            NullLogger<DunningService>.Instance);

    // ── OnPaymentFailedAsync ──────────────────────────────

    [Fact]
    public async Task OnPaymentFailed_ActiveSubscription_SetsPastDueWithGracePeriod()
    {
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _planId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow.AddMonths(-1)
        });
        await _db.SaveChangesAsync();

        var svc = CreateService(graceDays: 5);
        await svc.OnPaymentFailedAsync(_tenantId);

        var sub = await _db.Subscriptions.FirstAsync(s => s.TenantId == _tenantId);
        Assert.Equal(SubscriptionStatus.PastDue, sub.Status);
        Assert.NotNull(sub.GracePeriodEndsAt);
        Assert.True(sub.GracePeriodEndsAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task OnPaymentFailed_WithInvoice_SetsInvoiceOverdue()
    {
        var sub = new Subscription
        {
            TenantId = _tenantId,
            PlanId = _planId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow.AddMonths(-1)
        };
        _db.Subscriptions.Add(sub);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            InvoiceNumber = "INV-001",
            Status = InvoiceStatus.Issued,
            Total = 299m,
            IssuedDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(7)
        };
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        var svc = CreateService();
        await svc.OnPaymentFailedAsync(_tenantId, invoice.Id);

        var freshInv = await _db.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceStatus.Overdue, freshInv!.Status);
    }

    [Fact]
    public async Task OnPaymentFailed_SendsEmail()
    {
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _planId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow.AddMonths(-1)
        });
        await _db.SaveChangesAsync();

        var svc = CreateService();
        await svc.OnPaymentFailedAsync(_tenantId);

        Assert.Single(_emailService.SentMessages);
        Assert.Contains("Payment", _emailService.SentMessages[0].Subject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnPaymentFailed_NoActiveSubscription_DoesNothing()
    {
        var svc = CreateService();
        await svc.OnPaymentFailedAsync(_tenantId);
        // No exception, no emails
        Assert.Empty(_emailService.SentMessages);
    }

    // ── RetryChargeAsync ──────────────────────────────────

    [Fact]
    public async Task RetryCharge_NoAuthorizationCode_ReturnsFalse()
    {
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _planId,
            Status = SubscriptionStatus.PastDue,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow.AddMonths(-1)
        });
        await _db.SaveChangesAsync();

        var svc = CreateService();
        var result = await svc.RetryChargeAsync(_tenantId);
        Assert.False(result);
    }

    // ── ProcessGracePeriodsAsync ──────────────────────────

    [Fact]
    public async Task ProcessGracePeriods_ExpiredGrace_CancelsAndSuspends()
    {
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _planId,
            Status = SubscriptionStatus.PastDue,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow.AddMonths(-2),
            GracePeriodEndsAt = DateTime.UtcNow.AddDays(-1) // expired
        });
        await _db.SaveChangesAsync();

        var svc = CreateService();
        await svc.ProcessGracePeriodsAsync();

        var sub = await _db.Subscriptions.FirstAsync(s => s.TenantId == _tenantId);
        Assert.Equal(SubscriptionStatus.Cancelled, sub.Status);
        Assert.NotNull(sub.CancelledAt);

        var tenant = await _db.Tenants.FindAsync(_tenantId);
        Assert.Equal(TenantStatus.Suspended, tenant!.Status);
    }

    [Fact]
    public async Task ProcessGracePeriods_WithinGrace_DoesNotCancel()
    {
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _planId,
            Status = SubscriptionStatus.PastDue,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow.AddMonths(-1),
            GracePeriodEndsAt = DateTime.UtcNow.AddDays(2) // still within grace
        });
        await _db.SaveChangesAsync();

        var svc = CreateService();
        await svc.ProcessGracePeriodsAsync();

        var sub = await _db.Subscriptions.FirstAsync(s => s.TenantId == _tenantId);
        Assert.Equal(SubscriptionStatus.PastDue, sub.Status);
    }

    // ── ReactivateAsync ───────────────────────────────────

    [Fact]
    public async Task Reactivate_PastDueSubscription_SetsActive()
    {
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _planId,
            Status = SubscriptionStatus.PastDue,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow.AddMonths(-1),
            GracePeriodEndsAt = DateTime.UtcNow.AddDays(2)
        });

        // Suspend tenant
        var tenant = await _db.Tenants.FindAsync(_tenantId);
        tenant!.Status = TenantStatus.Suspended;
        await _db.SaveChangesAsync();

        var svc = CreateService();
        await svc.ReactivateAsync(_tenantId);

        var sub = await _db.Subscriptions.FirstAsync(s => s.TenantId == _tenantId);
        Assert.Equal(SubscriptionStatus.Active, sub.Status);
        Assert.Null(sub.GracePeriodEndsAt);

        var freshTenant = await _db.Tenants.FindAsync(_tenantId);
        Assert.Equal(TenantStatus.Active, freshTenant!.Status);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // Stub
    private class StubEmailService : IEmailService
    {
        public List<EmailMessage> SentMessages { get; } = [];
        public Task<EmailSendResult> SendAsync(EmailMessage message)
        {
            SentMessages.Add(message);
            return Task.FromResult(EmailSendResult.Succeeded());
        }

        public Task<EmailSendResult> SendMagicLinkAsync(string to, string magicLinkUrl) => Task.FromResult(EmailSendResult.Succeeded());
        public Task SendPasswordResetAsync(string to, string resetUrl) => Task.CompletedTask;
    }

    private class StubVariableChargeService : IVariableChargeService
    {
        public Task<VariableChargeBreakdown> CalculateVariableChargesAsync(Guid tenantId, DateTime periodStart, DateTime periodEnd)
            => Task.FromResult(VariableChargeBreakdown.Empty);
        public Task<ChargeResult> ChargeVariableAsync(Guid tenantId)
            => Task.FromResult(new ChargeResult(true));
        public Task<ChargeResult> ChargeInvoiceAsync(Guid tenantId, Invoice invoice)
            => Task.FromResult(new ChargeResult(true));
    }
}
