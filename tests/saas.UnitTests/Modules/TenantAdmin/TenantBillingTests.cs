using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Shared;
using Xunit;

namespace saas.Tests.Modules.TenantAdmin;

public class TenantBillingTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private CoreDbContext _coreDb = null!;
    private FakeBillingService _billingService = null!;

    private Guid _tenantId;
    private Guid _freePlanId;
    private Guid _proPlanId;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(_connection)
            .Options;

        _coreDb = new CoreDbContext(options);
        await _coreDb.Database.EnsureCreatedAsync();

        _freePlanId = Guid.NewGuid();
        _proPlanId = Guid.NewGuid();
        _tenantId = Guid.NewGuid();

        _coreDb.Plans.AddRange(
            new Plan { Id = _freePlanId, Name = "Free", Slug = "free", MonthlyPrice = 0, SortOrder = 0, IsActive = true },
            new Plan { Id = _proPlanId, Name = "Professional", Slug = "professional", MonthlyPrice = 499, SortOrder = 1, IsActive = true }
        );

        _coreDb.Tenants.Add(new Tenant
        {
            Id = _tenantId, Name = "Test Corp", Slug = "testcorp",
            ContactEmail = "admin@test.com", Status = TenantStatus.Active, PlanId = _freePlanId
        });

        _coreDb.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, PlanId = _freePlanId,
            Status = SubscriptionStatus.Active, BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow.AddMonths(-1),
            NextBillingDate = DateTime.UtcNow.AddDays(15)
        });

        await _coreDb.SaveChangesAsync();
        _billingService = new FakeBillingService();
    }

    public async Task DisposeAsync()
    {
        await _coreDb.DisposeAsync();
        await _connection.DisposeAsync();
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task ChangePlan_CallsBillingService()
    {
        var result = await _billingService.ChangePlanAsync(_tenantId, _proPlanId);

        Assert.True(result.Success);
        Assert.True(_billingService.ChangePlanCalled);
        Assert.Equal(_tenantId, _billingService.LastChangeTenantId);
        Assert.Equal(_proPlanId, _billingService.LastChangePlanId);
    }

    [Fact]
    public async Task CancelSubscription_CallsBillingService()
    {
        var result = await _billingService.CancelSubscriptionAsync(_tenantId);

        Assert.True(result);
        Assert.True(_billingService.CancelCalled);
        Assert.Equal(_tenantId, _billingService.LastCancelTenantId);
    }

    [Fact]
    public async Task GetSubscriptionStatus_ReturnsStatus()
    {
        var status = await _billingService.GetSubscriptionStatusAsync(_tenantId);

        Assert.Equal(SubscriptionStatus.Active, status);
    }

    // ── Fake ──────────────────────────────────────────────────────────────────

    private class FakeBillingService : IBillingService
    {
        public bool ChangePlanCalled { get; private set; }
        public Guid LastChangeTenantId { get; private set; }
        public Guid LastChangePlanId { get; private set; }
        public bool CancelCalled { get; private set; }
        public Guid LastCancelTenantId { get; private set; }

        public Task<SubscriptionInitResult> InitializeSubscriptionAsync(SubscriptionInitRequest request)
            => Task.FromResult(new SubscriptionInitResult(true));

        public Task<SubscriptionStatus?> GetSubscriptionStatusAsync(Guid tenantId)
            => Task.FromResult<SubscriptionStatus?>(SubscriptionStatus.Active);

        public Task<bool> CancelSubscriptionAsync(Guid tenantId)
        {
            CancelCalled = true;
            LastCancelTenantId = tenantId;
            return Task.FromResult(true);
        }

        public Task<PlanChangeResult> ChangePlanAsync(Guid tenantId, Guid newPlanId, BillingCycle? newCycle = null)
        {
            ChangePlanCalled = true;
            LastChangeTenantId = tenantId;
            LastChangePlanId = newPlanId;
            return Task.FromResult(new PlanChangeResult(true));
        }

        public Task<PlanChangePreview> PreviewPlanChangeAsync(Guid tenantId, Guid newPlanId, BillingCycle? newCycle = null)
            => Task.FromResult(new PlanChangePreview(true));

        public Task SyncPlansAsync() => Task.CompletedTask;

        public Task<WebhookResult> ProcessWebhookAsync(string payload, string signature)
            => Task.FromResult(new WebhookResult(true));

        public Task<bool> VerifyAndLinkSubscriptionAsync(string reference) => Task.FromResult(true);

        public Task<bool> UpdatePlanInGatewayAsync(Guid planId) => Task.FromResult(true);

        public Task<string?> GetManageLinkAsync(Guid tenantId) => Task.FromResult<string?>(null);

        public Task ReconcileSubscriptionsAsync() => Task.CompletedTask;

        public Task<SeatChangeResult> UpdateSeatCountAsync(Guid tenantId, int newSeatCount)
            => Task.FromResult(new SeatChangeResult(true));

        public Task<SeatChangePreview> PreviewSeatChangeAsync(Guid tenantId, int newSeatCount)
            => Task.FromResult(new SeatChangePreview(true));

        public Task<ChargeResult> ChargeOneOffAsync(Guid tenantId, decimal amount, string description)
            => Task.FromResult(new ChargeResult(true));

        public Task<ChargeResult> ChargeVariableAsync(Guid tenantId)
            => Task.FromResult(new ChargeResult(true));

        public Task<RefundResult> IssueRefundAsync(Guid paymentId, decimal? amount = null)
            => Task.FromResult(new RefundResult(true));

        public Task<DiscountResult> ApplyDiscountAsync(Guid tenantId, string discountCode)
            => Task.FromResult(new DiscountResult(true));

        public Task<UsageBillingResult> ProcessUsageBillingAsync(Guid tenantId)
            => Task.FromResult(new UsageBillingResult(true));

        public Task<BillingDashboard> GetBillingDashboardAsync(Guid tenantId)
            => Task.FromResult(new BillingDashboard(
                PlanName: "Free", PlanSlug: "free", BillingCycle: BillingCycle.Monthly,
                CurrentPrice: 0, Status: SubscriptionStatus.Active,
                NextBillingDate: null, TrialEndsAt: null, IsTrialing: false,
                CurrentSeats: 1, IncludedSeats: null, MaxSeats: null, PerSeatPrice: null,
                CreditBalance: 0, EstimatedNextInvoice: 0,
                EstimatedVariableCharges: 0,
                UsageSummary: null, ActiveAddOns: null, ActiveDiscounts: null,
                RecentInvoices: new List<InvoiceSummaryLine>(),
                PaymentMethods: new List<PaymentMethodLine>()));
    }
}
