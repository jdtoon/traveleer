using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Modules.SuperAdmin.Services;
using saas.Shared;
using Xunit;

namespace saas.Tests.Modules.SuperAdmin;

public class SuperAdminServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private CoreDbContext _coreDb = null!;
    private SuperAdminService _service = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(_connection)
            .Options;

        _coreDb = new CoreDbContext(options);
        await _coreDb.Database.EnsureCreatedAsync();

        // Seed test data
        var freePlan = new Plan { Id = Guid.NewGuid(), Name = "Free", Slug = "free", MonthlyPrice = 0, SortOrder = 0, IsActive = true };
        var proPlan = new Plan { Id = Guid.NewGuid(), Name = "Professional", Slug = "professional", MonthlyPrice = 499, SortOrder = 1, IsActive = true };
        _coreDb.Plans.AddRange(freePlan, proPlan);

        var feature1 = new Feature { Id = Guid.NewGuid(), Key = "notes", Name = "Notes", Module = "Notes", IsGlobal = false, IsEnabled = true };
        var feature2 = new Feature { Id = Guid.NewGuid(), Key = "audit", Name = "Audit Trail", Module = "Audit", IsGlobal = true, IsEnabled = true };
        _coreDb.Features.AddRange(feature1, feature2);

        // PlanFeature: notes enabled for pro plan
        _coreDb.PlanFeatures.Add(new PlanFeature { PlanId = proPlan.Id, FeatureId = feature1.Id });

        var tenant1 = new Tenant
        {
            Id = Guid.NewGuid(), Name = "Test Corp", Slug = "testcorp",
            ContactEmail = "admin@testcorp.com", Status = TenantStatus.Active, PlanId = freePlan.Id
        };
        var tenant2 = new Tenant
        {
            Id = Guid.NewGuid(), Name = "Other Inc", Slug = "other",
            ContactEmail = "admin@other.com", Status = TenantStatus.Active, PlanId = proPlan.Id
        };
        _coreDb.Tenants.AddRange(tenant1, tenant2);

        _coreDb.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(), TenantId = tenant2.Id, PlanId = proPlan.Id,
            Status = SubscriptionStatus.Active, BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow.AddMonths(-1),
            NextBillingDate = DateTime.UtcNow.AddDays(15)
        });

        await _coreDb.SaveChangesAsync();

        // Use a mock service provider — we won't test GetTenantUserCountAsync (needs real tenant DB)
        _service = new SuperAdminService(_coreDb, null!, new MockBillingForAdmin(), new MockLitestreamStatusService());
    }

    private class MockBillingForAdmin : IBillingService
    {
        public Task<SubscriptionInitResult> InitializeSubscriptionAsync(SubscriptionInitRequest r) => Task.FromResult(new SubscriptionInitResult(true));
        public Task<SubscriptionStatus?> GetSubscriptionStatusAsync(Guid t) => Task.FromResult<SubscriptionStatus?>(SubscriptionStatus.Active);
        public Task<bool> CancelSubscriptionAsync(Guid t) => Task.FromResult(true);
        public Task<PlanChangeResult> ChangePlanAsync(Guid t, Guid p, BillingCycle? newCycle = null) => Task.FromResult(new PlanChangeResult(true));
        public Task<PlanChangePreview> PreviewPlanChangeAsync(Guid t, Guid p, BillingCycle? newCycle = null) => Task.FromResult(new PlanChangePreview(true));
        public Task SyncPlansAsync() => Task.CompletedTask;
        public Task<WebhookResult> ProcessWebhookAsync(string p, string s) => Task.FromResult(new WebhookResult(true));
        public Task<bool> VerifyAndLinkSubscriptionAsync(string reference) => Task.FromResult(true);
        public Task<bool> UpdatePlanInGatewayAsync(Guid planId) => Task.FromResult(true);
        public Task<string?> GetManageLinkAsync(Guid tenantId) => Task.FromResult<string?>(null);
        public Task ReconcileSubscriptionsAsync() => Task.CompletedTask;
        public Task<SeatChangeResult> UpdateSeatCountAsync(Guid tenantId, int newSeatCount) => Task.FromResult(new SeatChangeResult(true));
        public Task<SeatChangePreview> PreviewSeatChangeAsync(Guid tenantId, int newSeatCount) => Task.FromResult(new SeatChangePreview(true));
        public Task<ChargeResult> ChargeOneOffAsync(Guid tenantId, decimal amount, string description) => Task.FromResult(new ChargeResult(true));
        public Task<ChargeResult> ChargeVariableAsync(Guid tenantId) => Task.FromResult(new ChargeResult(true));
        public Task<RefundResult> IssueRefundAsync(Guid paymentId, decimal? amount = null) => Task.FromResult(new RefundResult(true));
        public Task<DiscountResult> ApplyDiscountAsync(Guid tenantId, string discountCode) => Task.FromResult(new DiscountResult(true));
        public Task<UsageBillingResult> ProcessUsageBillingAsync(Guid tenantId) => Task.FromResult(new UsageBillingResult(true));
        public Task<BillingDashboard> GetBillingDashboardAsync(Guid tenantId) => Task.FromResult(new BillingDashboard(
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

    private class MockLitestreamStatusService : ILitestreamStatusService
    {
        public Task<LitestreamStatusModel> GetStatusAsync(CancellationToken ct = default)
            => Task.FromResult(new LitestreamStatusModel());
    }

    public async Task DisposeAsync()
    {
        await _coreDb.DisposeAsync();
        await _connection.DisposeAsync();
        SqliteConnection.ClearAllPools();
    }

    // ── Dashboard ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDashboardAsync_ReturnsTenantCount()
    {
        var dashboard = await _service.GetDashboardAsync();

        Assert.Equal(2, dashboard.TenantCount);
        Assert.Equal(1, dashboard.ActiveSubscriptions);
        Assert.True(dashboard.RecentRegistrations >= 2);
        Assert.Equal(2, dashboard.RecentTenants.Count);
    }

    // ── Tenant Management ────────────────────────────────────────────────────

    [Fact]
    public async Task GetTenantsAsync_ReturnsAllTenants()
    {
        var tenants = await _service.GetTenantsAsync();

        Assert.Equal(2, tenants.Items.Count);
    }

    [Fact]
    public async Task GetTenantsAsync_WithSearch_FiltersByName()
    {
        var tenants = await _service.GetTenantsAsync("testcorp");

        Assert.Single(tenants.Items);
        Assert.Equal("Test Corp", tenants.Items[0].Name);
    }

    [Fact]
    public async Task GetTenantsAsync_WithSearch_FiltersByEmail()
    {
        var tenants = await _service.GetTenantsAsync("other.com");

        Assert.Single(tenants.Items);
        Assert.Equal("Other Inc", tenants.Items[0].Name);
    }

    [Fact]
    public async Task SuspendTenantAsync_ChangesStatusToSuspended()
    {
        var tenants = await _service.GetTenantsAsync("testcorp");
        var tenantId = tenants.Items[0].Id;

        var success = await _service.SuspendTenantAsync(tenantId);

        Assert.True(success);
        var tenant = await _coreDb.Tenants.FindAsync(tenantId);
        Assert.Equal(TenantStatus.Suspended, tenant!.Status);
    }

    [Fact]
    public async Task ActivateTenantAsync_ChangesStatusToActive()
    {
        var tenants = await _service.GetTenantsAsync("testcorp");
        var tenantId = tenants.Items[0].Id;

        await _service.SuspendTenantAsync(tenantId);
        var success = await _service.ActivateTenantAsync(tenantId);

        Assert.True(success);
        var tenant = await _coreDb.Tenants.FindAsync(tenantId);
        Assert.Equal(TenantStatus.Active, tenant!.Status);
    }

    [Fact]
    public async Task SuspendTenantAsync_NonExistent_ReturnsFalse()
    {
        var success = await _service.SuspendTenantAsync(Guid.NewGuid());
        Assert.False(success);
    }

    // ── Plan Management ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetPlansAsync_ReturnsSortedPlans()
    {
        var plans = await _service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("Free", plans[0].Name);
        Assert.Equal("Professional", plans[1].Name);
    }

    [Fact]
    public async Task SavePlanAsync_CreateNewPlan()
    {
        var model = new PlanEditModel
        {
            Name = "Enterprise", Slug = "enterprise",
            MonthlyPrice = 999, SortOrder = 2, IsActive = true
        };

        await _service.SavePlanAsync(model);

        var plans = await _coreDb.Plans.OrderBy(p => p.SortOrder).ToListAsync();
        Assert.Equal(3, plans.Count);
        Assert.Equal("Enterprise", plans[2].Name);
        Assert.Equal(999m, plans[2].MonthlyPrice);
    }

    [Fact]
    public async Task SavePlanAsync_UpdateExistingPlan()
    {
        var plan = await _coreDb.Plans.FirstAsync(p => p.Slug == "professional");
        var model = new PlanEditModel
        {
            Id = plan.Id,
            Name = plan.Name, Slug = plan.Slug,
            MonthlyPrice = 599, SortOrder = plan.SortOrder, IsActive = plan.IsActive
        };

        await _service.SavePlanAsync(model);

        var updated = await _coreDb.Plans.FindAsync(plan.Id);
        Assert.Equal(599m, updated!.MonthlyPrice);
    }

    // ── Feature Management ───────────────────────────────────────────────────

    [Fact]
    public async Task GetFeatureMatrixAsync_ReturnsMatrix()
    {
        var matrix = await _service.GetFeatureMatrixAsync();

        Assert.Equal(2, matrix.Plans.Count);
        Assert.Equal(2, matrix.Features.Count);
        // Notes enabled for professional plan
        var proPlan = matrix.Plans.First(p => p.Slug == "professional");
        var notesFeature = matrix.Features.First(f => f.Key == "notes");
        Assert.True(matrix.IsEnabled(proPlan.Id, notesFeature.Id));
    }

    [Fact]
    public async Task TogglePlanFeatureAsync_RemovesExisting()
    {
        var proPlan = await _coreDb.Plans.FirstAsync(p => p.Slug == "professional");
        var notesFeature = await _coreDb.Features.FirstAsync(f => f.Key == "notes");

        // Should remove (currently enabled)
        await _service.TogglePlanFeatureAsync(proPlan.Id, notesFeature.Id);

        var exists = await _coreDb.PlanFeatures
            .AnyAsync(pf => pf.PlanId == proPlan.Id && pf.FeatureId == notesFeature.Id);
        Assert.False(exists);
    }

    [Fact]
    public async Task TogglePlanFeatureAsync_AddsNew()
    {
        var freePlan = await _coreDb.Plans.FirstAsync(p => p.Slug == "free");
        var notesFeature = await _coreDb.Features.FirstAsync(f => f.Key == "notes");

        // Should add (not currently enabled for free plan)
        await _service.TogglePlanFeatureAsync(freePlan.Id, notesFeature.Id);

        var exists = await _coreDb.PlanFeatures
            .AnyAsync(pf => pf.PlanId == freePlan.Id && pf.FeatureId == notesFeature.Id);
        Assert.True(exists);
    }

    [Fact]
    public async Task SaveTenantFeatureOverrideAsync_CreatesNew()
    {
        var tenant = await _coreDb.Tenants.FirstAsync(t => t.Slug == "testcorp");
        var feature = await _coreDb.Features.FirstAsync(f => f.Key == "notes");

        await _service.SaveTenantFeatureOverrideAsync(new TenantFeatureOverrideModel
        {
            TenantId = tenant.Id,
            FeatureId = feature.Id,
            IsEnabled = true,
            Reason = "Trial access"
        });

        var overrides = await _coreDb.TenantFeatureOverrides.ToListAsync();
        Assert.Single(overrides);
        Assert.True(overrides[0].IsEnabled);
        Assert.Equal("Trial access", overrides[0].Reason);
    }

    [Fact]
    public async Task SaveTenantFeatureOverrideAsync_UpdatesExisting()
    {
        var tenant = await _coreDb.Tenants.FirstAsync(t => t.Slug == "testcorp");
        var feature = await _coreDb.Features.FirstAsync(f => f.Key == "notes");

        // Create
        await _service.SaveTenantFeatureOverrideAsync(new TenantFeatureOverrideModel
        {
            TenantId = tenant.Id, FeatureId = feature.Id, IsEnabled = true
        });

        // Update
        await _service.SaveTenantFeatureOverrideAsync(new TenantFeatureOverrideModel
        {
            TenantId = tenant.Id, FeatureId = feature.Id, IsEnabled = false, Reason = "Disabled"
        });

        var overrides = await _coreDb.TenantFeatureOverrides.ToListAsync();
        Assert.Single(overrides);
        Assert.False(overrides[0].IsEnabled);
    }
}
