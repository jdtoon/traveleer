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
    Task<PlanChangePreview> PreviewPlanChangeAsync(Guid tenantId, Guid newPlanId);
    Task SyncPlansAsync();
    Task<WebhookResult> ProcessWebhookAsync(string payload, string signature);
    /// <summary>
    /// Verify a transaction with the payment gateway and link the real subscription code.
    /// Called from the payment callback to ensure we have the correct subscription code
    /// even if the webhook hasn't arrived yet.
    /// </summary>
    Task VerifyAndLinkSubscriptionAsync(string reference);
    /// <summary>
    /// Push plan price/name changes to the payment gateway.
    /// Called when SuperAdmin edits a plan.
    /// </summary>
    Task<bool> UpdatePlanInGatewayAsync(Guid planId);
    /// <summary>
    /// Get the Paystack manage subscription link for updating card details.
    /// Returns null if not available.
    /// </summary>
    Task<string?> GetManageLinkAsync(Guid tenantId);
    /// <summary>
    /// Reconcile local subscription statuses with the payment gateway.
    /// Called periodically by a background service.
    /// </summary>
    Task ReconcileSubscriptionsAsync();
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
    string? Error = null,
    bool RequiresRedirect = true
);

public record PlanChangeResult(
    bool Success,
    string? PaymentUrl = null,
    string? Error = null
);

/// <summary>
/// Preview of a plan change with pro-rated amounts.
/// </summary>
public record PlanChangePreview(
    bool IsValid,
    string? Error = null,
    string CurrentPlanName = "",
    string NewPlanName = "",
    decimal CurrentPlanPrice = 0,
    decimal NewPlanPrice = 0,
    int RemainingDays = 0,
    int TotalCycleDays = 30,
    decimal UnusedCredit = 0,
    decimal ProratedNewCost = 0,
    decimal AmountDue = 0,
    bool IsUpgrade = false,
    decimal CreditForNextCycle = 0
);

public record WebhookResult(
    bool Success,
    string? Error = null
);
