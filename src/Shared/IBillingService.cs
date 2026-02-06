using saas.Data.Core;

namespace saas.Shared;

/// <summary>
/// Billing operations abstraction. Implementations: MockBillingService (dev), PaystackBillingService (prod).
/// </summary>
public interface IBillingService
{
    Task<SubscriptionInitResult> InitializeSubscriptionAsync(SubscriptionInitRequest request);
    Task<SubscriptionStatus?> GetSubscriptionStatusAsync(Guid tenantId);
    Task<bool> CancelSubscriptionAsync(Guid tenantId);
    Task<PlanChangeResult> ChangePlanAsync(Guid tenantId, Guid newPlanId);
    Task SyncPlansAsync();
    Task<WebhookResult> ProcessWebhookAsync(string payload, string signature);
}

public record SubscriptionInitRequest(
    Guid TenantId,
    string Email,
    Guid PlanId,
    BillingCycle BillingCycle,
    string? CallbackUrl = null
);

public record SubscriptionInitResult(
    bool Success,
    string? PaymentUrl = null,
    string? Error = null
);

public record PlanChangeResult(
    bool Success,
    string? PaymentUrl = null,
    string? Error = null
);

public record WebhookResult(
    bool Success,
    string? Error = null
);
