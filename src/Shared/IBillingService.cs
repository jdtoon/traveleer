using saas.Data.Core;

namespace saas.Shared;

/// <summary>
/// Billing operations abstraction. Implementations: MockBillingService (dev), PaystackBillingService (prod).
/// </summary>
public interface IBillingService
{
    // Subscriptions
    Task<SubscriptionInitResult> InitializeSubscriptionAsync(SubscriptionInitRequest request);
    Task<SubscriptionStatus?> GetSubscriptionStatusAsync(Guid tenantId);
    Task<bool> CancelSubscriptionAsync(Guid tenantId);
    Task<PlanChangeResult> ChangePlanAsync(Guid tenantId, Guid newPlanId, BillingCycle? newCycle = null);
    Task<PlanChangePreview> PreviewPlanChangeAsync(Guid tenantId, Guid newPlanId, BillingCycle? newCycle = null);

    // Seat management
    Task<SeatChangeResult> UpdateSeatCountAsync(Guid tenantId, int newSeatCount);
    Task<SeatChangePreview> PreviewSeatChangeAsync(Guid tenantId, int newSeatCount);

    // One-off charges
    Task<ChargeResult> ChargeOneOffAsync(Guid tenantId, decimal amount, string description);

    // Variable charges (seats + usage — collected via charge_authorization at renewal)
    Task<ChargeResult> ChargeVariableAsync(Guid tenantId);

    // Refunds
    Task<RefundResult> IssueRefundAsync(Guid paymentId, decimal? amount = null);

    // Discounts
    Task<DiscountResult> ApplyDiscountAsync(Guid tenantId, string discountCode);

    // Usage billing
    Task<UsageBillingResult> ProcessUsageBillingAsync(Guid tenantId);

    // Paystack sync
    Task SyncPlansAsync();
    Task<bool> UpdatePlanInGatewayAsync(Guid planId);
    Task ReconcileSubscriptionsAsync();

    // Webhooks
    Task<WebhookResult> ProcessWebhookAsync(string payload, string signature);
    Task<bool> VerifyAndLinkSubscriptionAsync(string reference);

    // Customer portal
    Task<string?> GetManageLinkAsync(Guid tenantId);
    Task<BillingDashboard> GetBillingDashboardAsync(Guid tenantId);
}

// ── Supporting Records ───────────────────────────────────────────

public record SubscriptionInitRequest(
    Guid TenantId,
    string Email,
    Guid PlanId,
    BillingCycle BillingCycle,
    int SeatCount = 1,
    string? DiscountCode = null,
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
    decimal CreditApplied = 0,
    decimal AmountCharged = 0,
    string? Error = null
);

public record PlanChangePreview(
    bool IsValid,
    string? Error = null,
    string CurrentPlanName = "",
    string NewPlanName = "",
    decimal CurrentPlanPrice = 0,
    decimal NewPlanPrice = 0,
    BillingCycle CurrentCycle = BillingCycle.Monthly,
    BillingCycle NewCycle = BillingCycle.Monthly,
    int RemainingDays = 0,
    int TotalCycleDays = 30,
    decimal UnusedCredit = 0,
    decimal ProratedNewCost = 0,
    decimal AmountDue = 0,
    bool IsUpgrade = false,
    decimal CreditForNextCycle = 0
);

public record SeatChangeResult(
    bool Success,
    int PreviousSeats = 0,
    int NewSeats = 0,
    decimal AmountCharged = 0,
    decimal CreditIssued = 0,
    string? Error = null
);

public record SeatChangePreview(
    bool IsValid,
    int CurrentSeats = 0,
    int NewSeats = 0,
    int SeatDifference = 0,
    decimal PricePerSeat = 0,
    int RemainingDays = 0,
    int TotalCycleDays = 30,
    decimal ProratedAmount = 0,
    bool IsIncrease = true,
    string? Error = null
);

public record ChargeResult(
    bool Success,
    Guid? InvoiceId = null,
    Guid? PaymentId = null,
    string? PaymentUrl = null,
    string? Error = null
);

public record RefundResult(
    bool Success,
    decimal AmountRefunded = 0,
    string? PaystackRefundReference = null,
    string? Error = null
);

public record DiscountResult(
    bool Success,
    string? DiscountName = null,
    decimal? DiscountValue = null,
    DiscountType? Type = null,
    string? Error = null
);

public record UsageBillingResult(
    bool Success,
    Guid? InvoiceId = null,
    decimal TotalUsageCharge = 0,
    Dictionary<string, UsageChargeLine>? UsageBreakdown = null,
    string? Error = null
);

public record UsageChargeLine(
    string MetricDisplayName,
    long IncludedQuantity,
    long ActualQuantity,
    long OverageQuantity,
    decimal PricePerUnit,
    decimal TotalCharge
);

public record WebhookResult(bool Success, string? Error = null);

public record BillingDashboard(
    string PlanName,
    string PlanSlug,
    BillingCycle BillingCycle,
    decimal CurrentPrice,
    SubscriptionStatus Status,
    DateTime? NextBillingDate,
    DateTime? TrialEndsAt,
    bool IsTrialing,
    int CurrentSeats,
    int? IncludedSeats,
    int? MaxSeats,
    decimal? PerSeatPrice,
    decimal CreditBalance,
    decimal EstimatedNextInvoice,
    decimal EstimatedVariableCharges,
    List<UsageSummaryLine>? UsageSummary,
    List<ActiveAddOnLine>? ActiveAddOns,
    List<ActiveDiscountLine>? ActiveDiscounts,
    List<InvoiceSummaryLine> RecentInvoices,
    List<PaymentMethodLine> PaymentMethods
);

public record UsageSummaryLine(string Metric, string DisplayName, long Used, long? Included, decimal OverageCharge);
public record ActiveAddOnLine(string Name, decimal Price, AddOnInterval Interval, DateTime ActivatedAt);
public record ActiveDiscountLine(string Name, string Code, DiscountType Type, decimal Value, int? RemainingCycles);
public record InvoiceSummaryLine(string InvoiceNumber, decimal Total, InvoiceStatus Status, DateTime IssuedDate);
public record PaymentMethodLine(string Last4, string CardType, string Bank, string ExpiryMonth, string ExpiryYear, bool IsDefault);
