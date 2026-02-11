using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Shared;

namespace saas.Infrastructure.Services;

public class MockBillingService : IBillingService
{
    private readonly CoreDbContext _db;
    private readonly ILogger<MockBillingService> _logger;

    public MockBillingService(CoreDbContext db, ILogger<MockBillingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<SubscriptionInitResult> InitializeSubscriptionAsync(SubscriptionInitRequest request)
    {
        _logger.LogInformation("[MOCK BILLING] InitializeSubscription tenant={TenantId} plan={PlanId}", request.TenantId, request.PlanId);

        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            PlanId = request.PlanId,
            Status = SubscriptionStatus.Active,
            BillingCycle = request.BillingCycle,
            StartDate = DateTime.UtcNow,
            NextBillingDate = request.BillingCycle == BillingCycle.Monthly
                ? DateTime.UtcNow.AddMonths(1)
                : DateTime.UtcNow.AddYears(1),
            PaystackSubscriptionCode = $"MOCK-{Guid.NewGuid()}"
        };

        _db.Subscriptions.Add(subscription);

        var plan = await _db.Plans.FindAsync(request.PlanId);
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            Amount = plan?.MonthlyPrice ?? 0,
            Currency = "ZAR",
            Status = PaymentStatus.Success,
            TransactionDate = DateTime.UtcNow,
            GatewayResponse = "MOCK",
            PaystackReference = $"MOCK-TXN-{Guid.NewGuid():N}"
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();

        _logger.LogInformation("[MOCK BILLING] Subscription created (no redirect needed): {SubCode}", subscription.PaystackSubscriptionCode);

        // Mock provider provisions inline — no payment redirect needed
        return new SubscriptionInitResult(true, RequiresRedirect: false);
    }

    public async Task<SubscriptionStatus?> GetSubscriptionStatusAsync(Guid tenantId)
    {
        var subscription = await _db.Subscriptions
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.StartDate)
            .FirstOrDefaultAsync();

        return subscription?.Status ?? SubscriptionStatus.Active;
    }

    public async Task<bool> CancelSubscriptionAsync(Guid tenantId)
    {
        var subscription = await _db.Subscriptions
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.StartDate)
            .FirstOrDefaultAsync();

        if (subscription is null)
            return false;

        subscription.Status = SubscriptionStatus.Cancelled;
        subscription.CancelledAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<PlanChangeResult> ChangePlanAsync(Guid tenantId, Guid newPlanId)
    {
        _logger.LogInformation("[MOCK BILLING] ChangePlan tenant={TenantId} plan={PlanId}", tenantId, newPlanId);

        var preview = await PreviewPlanChangeAsync(tenantId, newPlanId);
        if (!preview.IsValid)
            return new PlanChangeResult(false, Error: preview.Error);

        // Update tenant plan
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant is not null) tenant.PlanId = newPlanId;

        // Update existing subscription in-place
        var existingSub = await _db.Subscriptions
            .Where(s => s.TenantId == tenantId && s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.StartDate)
            .FirstOrDefaultAsync();

        if (existingSub is not null)
        {
            existingSub.PlanId = newPlanId;
            existingSub.StartDate = DateTime.UtcNow;
            existingSub.NextBillingDate = DateTime.UtcNow.AddMonths(1);
        }
        else
        {
            _db.Subscriptions.Add(new Subscription
            {
                TenantId = tenantId,
                PlanId = newPlanId,
                Status = SubscriptionStatus.Active,
                BillingCycle = BillingCycle.Monthly,
                StartDate = DateTime.UtcNow,
                NextBillingDate = DateTime.UtcNow.AddMonths(1)
            });
        }

        // Record prorated payment (upgrade) or credit (downgrade)
        if (preview.AmountDue > 0)
        {
            _db.Payments.Add(new Payment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Amount = preview.AmountDue,
                Currency = "ZAR",
                Status = PaymentStatus.Success,
                TransactionDate = DateTime.UtcNow,
                GatewayResponse = $"MOCK-PRORATE: Upgrade from {preview.CurrentPlanName} to {preview.NewPlanName}",
                PaystackReference = $"MOCK-PRORATE-{Guid.NewGuid():N}"
            });
        }

        await _db.SaveChangesAsync();
        return new PlanChangeResult(true);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(Guid tenantId, Guid newPlanId)
    {
        var tenant = await _db.Tenants.Include(t => t.Plan).FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return new PlanChangePreview(false, Error: "Tenant not found");

        var newPlan = await _db.Plans.FindAsync(newPlanId);
        if (newPlan is null) return new PlanChangePreview(false, Error: "Plan not found");

        var currentPlan = tenant.Plan;
        if (currentPlan.Id == newPlanId)
            return new PlanChangePreview(false, Error: "Already on this plan");

        var existingSub = await _db.Subscriptions
            .Where(s => s.TenantId == tenantId && s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.StartDate)
            .FirstOrDefaultAsync();

        var now = DateTime.UtcNow;
        int totalCycleDays = 30;
        int remainingDays = totalCycleDays;

        if (existingSub?.NextBillingDate is not null)
        {
            remainingDays = Math.Max(0, (int)(existingSub.NextBillingDate.Value - now).TotalDays);
            if (existingSub.StartDate > DateTime.MinValue)
                totalCycleDays = Math.Max(1, (int)(existingSub.NextBillingDate.Value - existingSub.StartDate).TotalDays);
        }

        var dailyOldRate = totalCycleDays > 0 ? currentPlan.MonthlyPrice / totalCycleDays : 0;
        var dailyNewRate = totalCycleDays > 0 ? newPlan.MonthlyPrice / totalCycleDays : 0;
        var unusedCredit = Math.Round(dailyOldRate * remainingDays, 2);
        var proratedNewCost = Math.Round(dailyNewRate * remainingDays, 2);
        var amountDue = Math.Max(0, proratedNewCost - unusedCredit);
        var creditForNextCycle = Math.Max(0, unusedCredit - proratedNewCost);
        var isUpgrade = newPlan.MonthlyPrice > currentPlan.MonthlyPrice;

        return new PlanChangePreview(
            IsValid: true,
            CurrentPlanName: currentPlan.Name,
            NewPlanName: newPlan.Name,
            CurrentPlanPrice: currentPlan.MonthlyPrice,
            NewPlanPrice: newPlan.MonthlyPrice,
            RemainingDays: remainingDays,
            TotalCycleDays: totalCycleDays,
            UnusedCredit: unusedCredit,
            ProratedNewCost: proratedNewCost,
            AmountDue: amountDue,
            IsUpgrade: isUpgrade,
            CreditForNextCycle: creditForNextCycle
        );
    }

    public Task SyncPlansAsync()
    {
        _logger.LogInformation("[MOCK BILLING] SyncPlans");
        return Task.CompletedTask;
    }

    public Task<WebhookResult> ProcessWebhookAsync(string payload, string signature)
    {
        _logger.LogInformation("[MOCK BILLING] ProcessWebhook");
        return Task.FromResult(new WebhookResult(true));
    }

    public Task VerifyAndLinkSubscriptionAsync(string reference)
    {
        _logger.LogInformation("[MOCK BILLING] VerifyAndLinkSubscription reference={Reference}", reference);
        return Task.CompletedTask;
    }

    public Task<bool> UpdatePlanInGatewayAsync(Guid planId)
    {
        _logger.LogInformation("[MOCK BILLING] UpdatePlanInGateway plan={PlanId}", planId);
        return Task.FromResult(true);
    }

    public Task<string?> GetManageLinkAsync(Guid tenantId)
    {
        _logger.LogInformation("[MOCK BILLING] GetManageLink tenant={TenantId}", tenantId);
        return Task.FromResult<string?>(null);
    }

    public Task ReconcileSubscriptionsAsync()
    {
        _logger.LogInformation("[MOCK BILLING] ReconcileSubscriptions");
        return Task.CompletedTask;
    }
}
