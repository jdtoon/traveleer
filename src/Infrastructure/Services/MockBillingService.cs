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
                : DateTime.UtcNow.AddYears(1)
        };

        _db.Subscriptions.Add(subscription);

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            Amount = 0,
            Currency = "ZAR",
            Status = PaymentStatus.Success,
            TransactionDate = DateTime.UtcNow,
            GatewayResponse = "MOCK"
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();

        return new SubscriptionInitResult(true);
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

    public Task<PlanChangeResult> ChangePlanAsync(Guid tenantId, Guid newPlanId)
    {
        _logger.LogInformation("[MOCK BILLING] ChangePlan tenant={TenantId} plan={PlanId}", tenantId, newPlanId);
        return Task.FromResult(new PlanChangeResult(true));
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
}
