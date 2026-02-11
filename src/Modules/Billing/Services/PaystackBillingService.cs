using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using saas.Data.Audit;
using saas.Data.Core;
using saas.Modules.Billing.DTOs;
using saas.Modules.Billing.Models;
using saas.Shared;

namespace saas.Modules.Billing.Services;

/// <summary>
/// Production IBillingService implementation that integrates with Paystack.
/// Handles subscriptions, payments, webhooks, and plan syncing.
/// </summary>
public class PaystackBillingService : IBillingService
{
    private readonly PaystackClient _paystack;
    private readonly CoreDbContext _coreDb;
    private readonly InvoiceGenerator _invoiceGenerator;
    private readonly PaystackOptions _options;
    private readonly IAuditWriter _audit;
    private readonly IEmailService _emailService;
    private readonly ILogger<PaystackBillingService> _logger;

    public PaystackBillingService(
        PaystackClient paystack,
        CoreDbContext coreDb,
        InvoiceGenerator invoiceGenerator,
        IOptions<PaystackOptions> options,
        IAuditWriter audit,
        IEmailService emailService,
        ILogger<PaystackBillingService> logger)
    {
        _paystack = paystack;
        _coreDb = coreDb;
        _invoiceGenerator = invoiceGenerator;
        _options = options.Value;
        _audit = audit;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<SubscriptionInitResult> InitializeSubscriptionAsync(SubscriptionInitRequest request)
    {
        var plan = await _coreDb.Plans.FindAsync(request.PlanId);
        if (plan is null)
            return new SubscriptionInitResult(false, Error: "Plan not found");

        // Free plans don't need payment
        if (plan.MonthlyPrice == 0)
        {
            await CreateFreeSubscriptionAsync(request.TenantId, plan, request.BillingCycle);
            return new SubscriptionInitResult(true);
        }

        // Calculate amount in kobo/cents (Paystack requires integer cents)
        var amount = plan.MonthlyPrice * 100;

        var callbackUrl = request.CallbackUrl
            ?? $"{_options.CallbackBaseUrl}/register/callback";

        try
        {
            var paystackResult = await _paystack.InitializeTransactionAsync(new PaystackInitializeRequest
            {
                Email = request.Email,
                Amount = (int)amount,
                Currency = plan.Currency ?? "ZAR",
                CallbackUrl = callbackUrl,
                Plan = plan.PaystackPlanCode,
                Metadata = new Dictionary<string, object>
                {
                    ["tenant_id"] = request.TenantId.ToString(),
                    ["plan_id"] = request.PlanId.ToString(),
                    ["billing_cycle"] = request.BillingCycle.ToString()
                }
            });

            if (paystackResult is null)
                return new SubscriptionInitResult(false, Error: "Payment initialization failed");

            // Create pending subscription linked to the Paystack reference
            var subscription = new Subscription
            {
                TenantId = request.TenantId,
                PlanId = request.PlanId,
                Status = SubscriptionStatus.Active,
                BillingCycle = request.BillingCycle,
                StartDate = DateTime.UtcNow,
                PaystackSubscriptionCode = paystackResult.Reference
            };
            _coreDb.Subscriptions.Add(subscription);
            await _coreDb.SaveChangesAsync();

            _logger.LogInformation(
                "Paystack transaction initialized for tenant {TenantId}, reference {Reference}",
                request.TenantId, paystackResult.Reference);

            return new SubscriptionInitResult(true, PaymentUrl: paystackResult.AuthorizationUrl);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Paystack API error initializing subscription for tenant {TenantId}",
                request.TenantId);
            return new SubscriptionInitResult(false, Error: "Payment gateway error. Please try again.");
        }
    }

    public async Task<SubscriptionStatus?> GetSubscriptionStatusAsync(Guid tenantId)
    {
        var subscription = await _coreDb.Subscriptions
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.StartDate)
            .FirstOrDefaultAsync();

        return subscription?.Status;
    }

    public async Task<bool> CancelSubscriptionAsync(Guid tenantId)
    {
        var subscription = await _coreDb.Subscriptions
            .Where(s => s.TenantId == tenantId
                && (s.Status == SubscriptionStatus.Active
                    || s.Status == SubscriptionStatus.NonRenewing
                    || s.Status == SubscriptionStatus.PastDue))
            .OrderByDescending(s => s.StartDate)
            .FirstOrDefaultAsync();

        if (subscription is null)
            return false;

        // Cancel in Paystack if we have the subscription code
        if (!string.IsNullOrEmpty(subscription.PaystackSubscriptionCode))
        {
            try
            {
                // Fetch the subscription to get the email_token required for disabling
                var subDetail = await _paystack.FetchSubscriptionAsync(
                    subscription.PaystackSubscriptionCode);

                if (subDetail is null || string.IsNullOrEmpty(subDetail.EmailToken))
                {
                    _logger.LogError("Could not fetch email_token for subscription {Code}",
                        subscription.PaystackSubscriptionCode);
                    return false;
                }

                await _paystack.DisableSubscriptionAsync(
                    subscription.PaystackSubscriptionCode,
                    subDetail.EmailToken);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to disable Paystack subscription {Code}",
                    subscription.PaystackSubscriptionCode);
                return false;
            }
        }

        subscription.Status = SubscriptionStatus.Cancelled;
        subscription.CancelledAt = DateTime.UtcNow;
        await _coreDb.SaveChangesAsync();

        await _audit.WriteAsync(new AuditEntry
        {
            EntityType = "Subscription",
            EntityId = subscription.Id.ToString(),
            Action = "Cancelled",
            Timestamp = DateTime.UtcNow
        });

        _logger.LogInformation("Subscription cancelled for tenant {TenantId}", tenantId);
        return true;
    }

    public async Task<PlanChangeResult> ChangePlanAsync(Guid tenantId, Guid newPlanId)
    {
        var plan = await _coreDb.Plans.FindAsync(newPlanId);
        if (plan is null)
            return new PlanChangeResult(false, Error: "Plan not found");

        var tenant = await _coreDb.Tenants.FindAsync(tenantId);
        if (tenant is null)
            return new PlanChangeResult(false, Error: "Tenant not found");

        // Find existing subscription (Active, NonRenewing, or PastDue — all valid for plan change)
        var existingSub = await _coreDb.Subscriptions
            .Where(s => s.TenantId == tenantId
                && (s.Status == SubscriptionStatus.Active
                    || s.Status == SubscriptionStatus.NonRenewing
                    || s.Status == SubscriptionStatus.PastDue))
            .OrderByDescending(s => s.StartDate)
            .FirstOrDefaultAsync();

        // Cancel any existing Paystack subscription first
        if (existingSub is not null
            && !string.IsNullOrEmpty(existingSub.PaystackSubscriptionCode)
            && existingSub.PaystackSubscriptionCode.StartsWith("SUB_"))
        {
            try
            {
                var subDetail = await _paystack.FetchSubscriptionAsync(
                    existingSub.PaystackSubscriptionCode);

                if (subDetail is not null && !string.IsNullOrEmpty(subDetail.EmailToken))
                {
                    await _paystack.DisableSubscriptionAsync(
                        existingSub.PaystackSubscriptionCode,
                        subDetail.EmailToken);

                    _logger.LogInformation(
                        "Cancelled previous Paystack subscription {Code} during plan change for tenant {TenantId}",
                        existingSub.PaystackSubscriptionCode, tenantId);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Failed to disable old Paystack subscription {Code} during plan change",
                    existingSub.PaystackSubscriptionCode);
                // Continue anyway — the plan change should proceed
            }
        }

        // Update the tenant's plan and ensure active status
        tenant.PlanId = newPlanId;
        if (tenant.Status == TenantStatus.Suspended)
        {
            tenant.Status = TenantStatus.Active;
            _logger.LogInformation("Reactivated suspended tenant {TenantId} during plan change", tenantId);
        }

        // ── Free plan: update subscription locally, no payment needed ──
        if (plan.MonthlyPrice == 0)
        {
            if (existingSub is not null)
            {
                existingSub.PlanId = newPlanId;
                existingSub.Status = SubscriptionStatus.Active;
                existingSub.PaystackSubscriptionCode = null;
                existingSub.PaystackCustomerCode = existingSub.PaystackCustomerCode; // preserve
                existingSub.StartDate = DateTime.UtcNow;
            }
            else
            {
                _coreDb.Subscriptions.Add(new Subscription
                {
                    TenantId = tenantId,
                    PlanId = newPlanId,
                    Status = SubscriptionStatus.Active,
                    BillingCycle = BillingCycle.Monthly,
                    StartDate = DateTime.UtcNow
                });
            }

            await _coreDb.SaveChangesAsync();

            await _audit.WriteAsync(new AuditEntry
            {
                EntityType = "Subscription",
                EntityId = existingSub?.Id.ToString() ?? tenantId.ToString(),
                Action = "PlanChanged",
                NewValues = $"Switched to free plan {plan.Name}",
                Timestamp = DateTime.UtcNow
            });

            return new PlanChangeResult(true);
        }

        // ── Paid plan: redirect to Paystack checkout ──
        var preview = await PreviewPlanChangeAsync(tenantId, newPlanId);
        var chargeAmount = preview.IsValid && preview.AmountDue > 0
            ? preview.AmountDue * 100
            : plan.MonthlyPrice * 100;
        try
        {
            var callbackUrl = $"{_options.CallbackBaseUrl}/{tenant.Slug}/billing/callback";

            var paystackResult = await _paystack.InitializeTransactionAsync(new PaystackInitializeRequest
            {
                Email = tenant.ContactEmail,
                Amount = (int)chargeAmount,
                Currency = plan.Currency ?? "ZAR",
                CallbackUrl = callbackUrl,
                Plan = plan.PaystackPlanCode,
                Metadata = new Dictionary<string, object>
                {
                    ["tenant_id"] = tenantId.ToString(),
                    ["plan_id"] = newPlanId.ToString(),
                    ["action"] = "plan_change"
                }
            });

            if (paystackResult is null)
                return new PlanChangeResult(false, Error: "Payment initialization failed");

            // Update existing subscription or create new one with the Paystack reference
            if (existingSub is not null)
            {
                existingSub.PlanId = newPlanId;
                existingSub.Status = SubscriptionStatus.Active;
                existingSub.PaystackSubscriptionCode = paystackResult.Reference;
                existingSub.StartDate = DateTime.UtcNow;
                existingSub.EndDate = null;
                existingSub.CancelledAt = null;
            }
            else
            {
                _coreDb.Subscriptions.Add(new Subscription
                {
                    TenantId = tenantId,
                    PlanId = newPlanId,
                    Status = SubscriptionStatus.Active,
                    BillingCycle = BillingCycle.Monthly,
                    StartDate = DateTime.UtcNow,
                    PaystackSubscriptionCode = paystackResult.Reference
                });
            }

            await _coreDb.SaveChangesAsync();

            _logger.LogInformation(
                "Plan change initialized for tenant {TenantId}: new plan {PlanId}, reference {Reference}",
                tenantId, newPlanId, paystackResult.Reference);

            return new PlanChangeResult(true, PaymentUrl: paystackResult.AuthorizationUrl);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to change plan for tenant {TenantId}", tenantId);
            return new PlanChangeResult(false, Error: "Payment gateway error");
        }
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(Guid tenantId, Guid newPlanId)
    {
        var tenant = await _coreDb.Tenants.Include(t => t.Plan).FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return new PlanChangePreview(false, Error: "Tenant not found");

        var newPlan = await _coreDb.Plans.FindAsync(newPlanId);
        if (newPlan is null) return new PlanChangePreview(false, Error: "Plan not found");

        var currentPlan = tenant.Plan ?? await _coreDb.Plans.FindAsync(tenant.PlanId);
        if (currentPlan is null)
            return new PlanChangePreview(false, Error: "Current plan not found");
        if (currentPlan.Id == newPlanId)
            return new PlanChangePreview(false, Error: "Already on this plan");

        var existingSub = await _coreDb.Subscriptions
            .Where(s => s.TenantId == tenantId && (s.Status == SubscriptionStatus.Active
                || s.Status == SubscriptionStatus.NonRenewing
                || s.Status == SubscriptionStatus.PastDue))
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

    public async Task SyncPlansAsync()
    {
        _logger.LogInformation("Syncing plans with Paystack...");

        try
        {
            var paystackPlans = await _paystack.ListPlansAsync();
            var dbPlans = await _coreDb.Plans
                .Where(p => p.IsActive && p.MonthlyPrice > 0)
                .ToListAsync();

            foreach (var dbPlan in dbPlans)
            {
                // Check if plan already exists in Paystack
                if (!string.IsNullOrEmpty(dbPlan.PaystackPlanCode))
                {
                    var exists = paystackPlans.Any(p =>
                        p.PlanCode == dbPlan.PaystackPlanCode);
                    if (exists) continue;
                }

                // Create monthly plan in Paystack
                var monthlyResult = await _paystack.CreatePlanAsync(new PaystackCreatePlanRequest
                {
                    Name = $"{dbPlan.Name} (Monthly)",
                    Interval = "monthly",
                    Amount = (int)(dbPlan.MonthlyPrice * 100),
                    Currency = dbPlan.Currency ?? "ZAR"
                });

                if (monthlyResult is not null)
                {
                    dbPlan.PaystackPlanCode = monthlyResult.PlanCode;
                    _logger.LogInformation("Created Paystack plan {PlanCode} for {Name}",
                        monthlyResult.PlanCode, dbPlan.Name);
                }
            }

            await _coreDb.SaveChangesAsync();
            _logger.LogInformation("Plan sync complete. {Count} plans processed.", dbPlans.Count);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to sync plans with Paystack");
        }
    }

    public async Task<WebhookResult> ProcessWebhookAsync(string payload, string signature)
    {
        // Verify webhook signature
        if (!WebhookSignatureValidator.IsValid(payload, signature, _options.WebhookSecret))
        {
            _logger.LogWarning("Invalid Paystack webhook signature");
            return new WebhookResult(false, Error: "Invalid signature");
        }

        // Parse event
        PaystackWebhookEvent? webhookEvent;
        try
        {
            webhookEvent = JsonSerializer.Deserialize<PaystackWebhookEvent>(payload);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Paystack webhook payload");
            return new WebhookResult(false, Error: "Invalid payload");
        }

        if (webhookEvent is null)
            return new WebhookResult(false, Error: "Invalid payload");

        _logger.LogInformation("Processing Paystack webhook: {Event}", webhookEvent.Event);

        return webhookEvent.Event switch
        {
            "charge.success" => await HandleChargeSuccessAsync(webhookEvent),
            "subscription.create" => await HandleSubscriptionCreatedAsync(webhookEvent),
            "subscription.not_renew" => await HandleSubscriptionNotRenewAsync(webhookEvent),
            "subscription.disable" => await HandleSubscriptionDisabledAsync(webhookEvent),
            "subscription.expiring_cards" => await HandleExpiringCardsAsync(webhookEvent),
            "invoice.create" => await HandleInvoiceCreatedAsync(webhookEvent),
            "invoice.update" => await HandleInvoiceUpdatedAsync(webhookEvent),
            "invoice.payment_failed" => await HandlePaymentFailedAsync(webhookEvent),
            _ => new WebhookResult(true) // Acknowledge unhandled events
        };
    }

    // ── Webhook Handlers ───────────────────────────────────────────

    private async Task<WebhookResult> HandleChargeSuccessAsync(PaystackWebhookEvent webhookEvent)
    {
        var data = webhookEvent.Data;
        var reference = data.Reference;

        // Idempotency — don't process the same transaction twice
        if (await _coreDb.Payments.AnyAsync(p => p.PaystackReference == reference))
            return new WebhookResult(true);

        // Extract tenant ID from metadata
        if (data.Metadata is null || !data.Metadata.TryGetValue("tenant_id", out var tenantIdObj)
            || !Guid.TryParse(tenantIdObj.ToString(), out var tenantId))
        {
            _logger.LogWarning("charge.success webhook missing tenant_id metadata");
            return new WebhookResult(true); // Acknowledge but skip
        }

        // Create payment record
        var payment = new Payment
        {
            TenantId = tenantId,
            Amount = data.Amount / 100m,
            Currency = data.Currency?.ToUpperInvariant() ?? "ZAR",
            Status = PaymentStatus.Success,
            PaystackReference = reference,
            PaystackTransactionId = data.Id?.ToString(),
            GatewayResponse = data.GatewayResponse,
            TransactionDate = DateTime.UtcNow
        };
        _coreDb.Payments.Add(payment);

        // Update related invoice if exists
        var invoice = await _coreDb.Invoices
            .FirstOrDefaultAsync(i => i.PaystackReference == reference);
        if (invoice is not null)
        {
            invoice.Status = InvoiceStatus.Paid;
            invoice.PaidDate = DateTime.UtcNow;
            payment.InvoiceId = invoice.Id;
        }

        // Ensure tenant is active
        var tenant = await _coreDb.Tenants.FindAsync(tenantId);
        if (tenant is not null && tenant.Status == TenantStatus.Suspended)
        {
            tenant.Status = TenantStatus.Active;
        }

        // Also link the real subscription code if we can get it from transaction verification
        // This serves as a belt-and-suspenders with the subscription.create webhook
        await TryLinkSubscriptionFromReferenceAsync(reference, tenantId);

        await _coreDb.SaveChangesAsync();

        await _audit.WriteAsync(new AuditEntry
        {
            TenantSlug = tenant?.Slug,
            EntityType = "Payment",
            EntityId = payment.Id.ToString(),
            Action = "Created",
            Timestamp = DateTime.UtcNow
        });

        _logger.LogInformation("Payment recorded for tenant {TenantId}, reference {Reference}",
            tenantId, reference);

        return new WebhookResult(true);
    }

    private async Task<WebhookResult> HandleSubscriptionCreatedAsync(PaystackWebhookEvent webhookEvent)
    {
        var data = webhookEvent.Data;
        // For subscription.create events, subscription_code is at the data level
        var subscriptionCode = data.SubscriptionCode ?? data.Subscription?.Code;

        if (string.IsNullOrEmpty(subscriptionCode))
        {
            _logger.LogWarning("subscription.create webhook missing subscription_code");
            return new WebhookResult(true);
        }

        // Strategy 1: Find by the transaction reference initially stored as PaystackSubscriptionCode
        var subscription = await _coreDb.Subscriptions
            .FirstOrDefaultAsync(s => s.PaystackSubscriptionCode == data.Reference
                || s.PaystackSubscriptionCode == subscriptionCode);

        // Strategy 2: Find by customer email + active subscription without a real SUB_ code
        if (subscription is null && data.Customer is not null)
        {
            subscription = await _coreDb.Subscriptions
                .Include(s => s.Tenant)
                .Where(s => s.Tenant!.ContactEmail == data.Customer.Email
                    && s.Status == SubscriptionStatus.Active
                    && (s.PaystackSubscriptionCode == null
                        || !s.PaystackSubscriptionCode.StartsWith("SUB_")))
                .OrderByDescending(s => s.StartDate)
                .FirstOrDefaultAsync();
        }

        if (subscription is not null)
        {
            subscription.PaystackSubscriptionCode = subscriptionCode;
            subscription.Status = SubscriptionStatus.Active;

            if (data.Customer is not null)
                subscription.PaystackCustomerCode = data.Customer.Code;

            await _coreDb.SaveChangesAsync();

            _logger.LogInformation(
                "Subscription {Code} linked to tenant {TenantId}",
                subscriptionCode, subscription.TenantId);
        }
        else
        {
            _logger.LogWarning(
                "Could not find matching subscription for subscription.create {Code}",
                subscriptionCode);
        }

        return new WebhookResult(true);
    }

    private async Task<WebhookResult> HandleSubscriptionNotRenewAsync(PaystackWebhookEvent webhookEvent)
    {
        var subscriptionCode = webhookEvent.Data.SubscriptionCode ?? webhookEvent.Data.Subscription?.Code;
        if (string.IsNullOrEmpty(subscriptionCode))
            return new WebhookResult(true);

        var subscription = await _coreDb.Subscriptions
            .FirstOrDefaultAsync(s => s.PaystackSubscriptionCode == subscriptionCode);

        if (subscription is not null)
        {
            subscription.Status = SubscriptionStatus.NonRenewing;
            await _coreDb.SaveChangesAsync();

            await _audit.WriteAsync(new AuditEntry
            {
                EntityType = "Subscription",
                EntityId = subscription.Id.ToString(),
                Action = "NonRenewing",
                NewValues = "Subscription will not renew at end of billing period",
                Timestamp = DateTime.UtcNow
            });

            _logger.LogInformation(
                "Subscription {Code} marked as non-renewing for tenant {TenantId}",
                subscriptionCode, subscription.TenantId);
        }

        return new WebhookResult(true);
    }

    private async Task<WebhookResult> HandleSubscriptionDisabledAsync(PaystackWebhookEvent webhookEvent)
    {
        var subscriptionCode = webhookEvent.Data.SubscriptionCode ?? webhookEvent.Data.Subscription?.Code;
        if (string.IsNullOrEmpty(subscriptionCode))
            return new WebhookResult(true);

        var subscription = await _coreDb.Subscriptions
            .Include(s => s.Tenant)
            .FirstOrDefaultAsync(s => s.PaystackSubscriptionCode == subscriptionCode);

        if (subscription is not null)
        {
            var previousStatus = subscription.Status;
            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.EndDate = DateTime.UtcNow;
            subscription.CancelledAt = DateTime.UtcNow;
            await _coreDb.SaveChangesAsync();

            await _audit.WriteAsync(new AuditEntry
            {
                EntityType = "Subscription",
                EntityId = subscription.Id.ToString(),
                Action = "Disabled",
                NewValues = $"Subscription disabled (was {previousStatus})",
                Timestamp = DateTime.UtcNow
            });

            _logger.LogInformation(
                "Subscription {Code} disabled for tenant {TenantId} (was {PreviousStatus})",
                subscriptionCode, subscription.TenantId, previousStatus);
        }

        return new WebhookResult(true);
    }

    private async Task<WebhookResult> HandleInvoiceCreatedAsync(PaystackWebhookEvent webhookEvent)
    {
        var data = webhookEvent.Data;

        // Invoice events may not carry the original transaction metadata.
        // Look up subscription by subscription_code first, then fall back to metadata.
        Subscription? subscription = null;
        var subscriptionCode = data.SubscriptionCode ?? data.Subscription?.Code;

        if (!string.IsNullOrEmpty(subscriptionCode))
        {
            subscription = await _coreDb.Subscriptions
                .FirstOrDefaultAsync(s => s.PaystackSubscriptionCode == subscriptionCode
                    && s.Status == SubscriptionStatus.Active);
        }

        // Fall back to metadata-based lookup if subscription code didn't match
        if (subscription is null && data.Metadata is not null
            && data.Metadata.TryGetValue("tenant_id", out var tenantIdObj)
            && Guid.TryParse(tenantIdObj.ToString(), out var tenantId))
        {
            subscription = await _coreDb.Subscriptions
                .Where(s => s.TenantId == tenantId && s.Status == SubscriptionStatus.Active)
                .OrderByDescending(s => s.StartDate)
                .FirstOrDefaultAsync();
        }

        if (subscription is not null)
        {
            var invoice = await _invoiceGenerator.GenerateAsync(
                subscription.TenantId,
                subscription.Id,
                data.Amount / 100m,
                data.Currency?.ToUpperInvariant() ?? "ZAR");

            invoice.PaystackReference = data.Reference;
            await _coreDb.SaveChangesAsync();
        }

        return new WebhookResult(true);
    }

    private async Task<WebhookResult> HandleInvoiceUpdatedAsync(PaystackWebhookEvent webhookEvent)
    {
        var data = webhookEvent.Data;

        // Update the invoice status based on the final charge result.
        if (!string.IsNullOrEmpty(data.Reference))
        {
            var invoice = await _coreDb.Invoices
                .FirstOrDefaultAsync(i => i.PaystackReference == data.Reference);

            if (invoice is not null)
            {
                // If status is "success" or paid is true, mark as paid
                if (data.Status == "success")
                {
                    invoice.Status = InvoiceStatus.Paid;
                    invoice.PaidDate = DateTime.UtcNow;
                }
                await _coreDb.SaveChangesAsync();
            }
        }

        _logger.LogInformation("Processed invoice.update for reference {Reference}", data.Reference);
        return new WebhookResult(true);
    }

    private async Task<WebhookResult> HandlePaymentFailedAsync(PaystackWebhookEvent webhookEvent)
    {
        var subscriptionCode = webhookEvent.Data.SubscriptionCode ?? webhookEvent.Data.Subscription?.Code;
        if (string.IsNullOrEmpty(subscriptionCode))
            return new WebhookResult(true);

        var subscription = await _coreDb.Subscriptions
            .Include(s => s.Tenant)
            .FirstOrDefaultAsync(s => s.PaystackSubscriptionCode == subscriptionCode);

        if (subscription is not null)
        {
            subscription.Status = SubscriptionStatus.PastDue;
            await _coreDb.SaveChangesAsync();

            _logger.LogWarning(
                "Payment failed for subscription {Code}, tenant {TenantId}. Status set to PastDue.",
                subscriptionCode, subscription.TenantId);
        }

        return new WebhookResult(true);
    }

    private async Task<WebhookResult> HandleExpiringCardsAsync(PaystackWebhookEvent webhookEvent)
    {
        var subscriptionCode = webhookEvent.Data.SubscriptionCode ?? webhookEvent.Data.Subscription?.Code;
        if (string.IsNullOrEmpty(subscriptionCode))
            return new WebhookResult(true);

        var subscription = await _coreDb.Subscriptions
            .Include(s => s.Tenant)
            .FirstOrDefaultAsync(s => s.PaystackSubscriptionCode == subscriptionCode);

        if (subscription?.Tenant is null)
        {
            _logger.LogWarning("subscription.expiring_cards: no tenant found for {Code}", subscriptionCode);
            return new WebhookResult(true);
        }

        var tenant = subscription.Tenant;

        try
        {
            // Fetch manage link so tenant can update their card
            var manageUrl = "";
            try
            {
                var subDetail = await _paystack.FetchSubscriptionAsync(subscriptionCode);
                manageUrl = subDetail?.ManageLink ?? "";
            }
            catch (HttpRequestException)
            {
                // Non-critical — send email without link
            }

            var updateCardSection = !string.IsNullOrEmpty(manageUrl)
                ? $"<p><a href=\"{manageUrl}\" style=\"display:inline-block;padding:10px 20px;background-color:#570df8;color:#ffffff;text-decoration:none;border-radius:6px;\">Update Card Now</a></p>"
                : "<p>Please log in to your billing page to update your card details.</p>";

            await _emailService.SendAsync(new EmailMessage(
                To: tenant.ContactEmail,
                Subject: "Action required: Your card is expiring soon",
                HtmlBody: $"""
                    <h2>Card Expiring Soon</h2>
                    <p>Hi,</p>
                    <p>The card on file for your <strong>{tenant.Name}</strong> subscription is expiring soon.
                    Please update your payment details to avoid any interruption in service.</p>
                    {updateCardSection}
                    <p>If you have any questions, please contact our support team.</p>
                    """,
                PlainTextBody: $"The card on file for your {tenant.Name} subscription is expiring soon. " +
                    "Please update your payment details to avoid any interruption in service."
            ));

            _logger.LogInformation(
                "Expiring card notification sent to {Email} for tenant {TenantId}",
                tenant.ContactEmail, tenant.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to send expiring card notification for tenant {TenantId}",
                subscription.TenantId);
        }

        return new WebhookResult(true);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private async Task CreateFreeSubscriptionAsync(Guid tenantId, Plan plan, BillingCycle billingCycle)
    {
        var subscription = new Subscription
        {
            TenantId = tenantId,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            BillingCycle = billingCycle,
            StartDate = DateTime.UtcNow
        };
        _coreDb.Subscriptions.Add(subscription);
        await _coreDb.SaveChangesAsync();
    }

    /// <summary>
    /// Verify a transaction and link the real SUB_ subscription code.
    /// Called from the payment callback to ensure we have correct data even before webhooks arrive.
    /// </summary>
    public async Task VerifyAndLinkSubscriptionAsync(string reference)
    {
        try
        {
            var verification = await _paystack.VerifyTransactionAsync(reference);
            if (verification is null || verification.Status != "success")
            {
                _logger.LogWarning("Transaction verification failed for {Reference}", reference);
                return;
            }

            // Find the subscription that has this transaction reference
            var subscription = await _coreDb.Subscriptions
                .FirstOrDefaultAsync(s => s.PaystackSubscriptionCode == reference);

            if (subscription is null)
            {
                // Try by tenant_id from metadata
                if (verification.Metadata is not null
                    && verification.Metadata.TryGetValue("tenant_id", out var tidObj)
                    && Guid.TryParse(tidObj.ToString(), out var tid))
                {
                    subscription = await _coreDb.Subscriptions
                        .Where(s => s.TenantId == tid && s.Status == SubscriptionStatus.Active
                            && (s.PaystackSubscriptionCode == null || !s.PaystackSubscriptionCode.StartsWith("SUB_")))
                        .OrderByDescending(s => s.StartDate)
                        .FirstOrDefaultAsync();
                }
            }

            if (subscription is null) return;

            // Now fetch subscription list for this customer to find the real subscription code
            if (verification.Customer is not null && !string.IsNullOrEmpty(verification.Customer.CustomerCode))
            {
                subscription.PaystackCustomerCode = verification.Customer.CustomerCode;

                // Fetch the actual subscription via Paystack API using the plan code
                // The customer's most recent subscription is what we want
                try
                {
                    var plan = await _coreDb.Plans.FindAsync(subscription.PlanId);
                    if (plan is not null && !string.IsNullOrEmpty(plan.PaystackPlanCode))
                    {
                        var subs = await _paystack.ListSubscriptionsAsync(
                            verification.Customer.CustomerCode, plan.PaystackPlanCode);
                        var match = subs.FirstOrDefault();
                        if (match is not null && !string.IsNullOrEmpty(match.SubscriptionCode))
                        {
                            subscription.PaystackSubscriptionCode = match.SubscriptionCode;
                            _logger.LogInformation(
                                "Linked subscription {Code} via transaction verify for reference {Ref}",
                                match.SubscriptionCode, reference);
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "Could not fetch subscriptions for customer during verify");
                }
            }

            await _coreDb.SaveChangesAsync();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Transaction verification failed for {Reference}", reference);
        }
    }

    public async Task<string?> GetManageLinkAsync(Guid tenantId)
    {
        var subscription = await _coreDb.Subscriptions
            .Where(s => s.TenantId == tenantId
                && s.PaystackSubscriptionCode != null
                && s.PaystackSubscriptionCode.StartsWith("SUB_"))
            .OrderByDescending(s => s.StartDate)
            .FirstOrDefaultAsync();

        if (subscription is null)
            return null;

        try
        {
            var detail = await _paystack.FetchSubscriptionAsync(subscription.PaystackSubscriptionCode!);
            return detail?.ManageLink;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch manage link for subscription {Code}",
                subscription.PaystackSubscriptionCode);
            return null;
        }
    }

    public async Task<bool> UpdatePlanInGatewayAsync(Guid planId)
    {
        var plan = await _coreDb.Plans.FindAsync(planId);
        if (plan is null || plan.MonthlyPrice <= 0)
            return false;

        try
        {
            // If no Paystack plan code yet, create a new plan on Paystack
            if (string.IsNullOrEmpty(plan.PaystackPlanCode))
            {
                var createResult = await _paystack.CreatePlanAsync(new PaystackCreatePlanRequest
                {
                    Name = $"{plan.Name} (Monthly)",
                    Interval = "monthly",
                    Amount = (int)(plan.MonthlyPrice * 100),
                    Currency = plan.Currency ?? "ZAR"
                });

                if (createResult is not null)
                {
                    plan.PaystackPlanCode = createResult.PlanCode;
                    await _coreDb.SaveChangesAsync();
                    _logger.LogInformation("Created Paystack plan {PlanCode} for {Name}",
                        createResult.PlanCode, plan.Name);
                    return true;
                }

                _logger.LogWarning("Failed to create Paystack plan for {Name}", plan.Name);
                return false;
            }

            // Existing plan — update name/amount on Paystack
            var result = await _paystack.UpdatePlanAsync(plan.PaystackPlanCode, new PaystackUpdatePlanRequest
            {
                Name = $"{plan.Name} (Monthly)",
                Amount = (int)(plan.MonthlyPrice * 100),
                Currency = plan.Currency ?? "ZAR"
            });

            if (result)
                _logger.LogInformation("Updated Paystack plan {PlanCode} for {Name}", plan.PaystackPlanCode, plan.Name);
            else
                _logger.LogWarning("Failed to update Paystack plan {PlanCode}", plan.PaystackPlanCode);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to update/create Paystack plan for {Name}", plan.Name);
            return false;
        }
    }

    public async Task ReconcileSubscriptionsAsync()
    {
        _logger.LogInformation("Starting subscription reconciliation...");

        var subscriptions = await _coreDb.Subscriptions
            .Include(s => s.Tenant)
            .Where(s => s.PaystackSubscriptionCode != null
                && s.PaystackSubscriptionCode.StartsWith("SUB_")
                && (s.Status == SubscriptionStatus.Active
                    || s.Status == SubscriptionStatus.NonRenewing
                    || s.Status == SubscriptionStatus.PastDue))
            .ToListAsync();

        var reconciled = 0;

        foreach (var sub in subscriptions)
        {
            try
            {
                var detail = await _paystack.FetchSubscriptionAsync(sub.PaystackSubscriptionCode!);
                if (detail is null) continue;

                var gatewayStatus = MapPaystackStatus(detail.Status);
                if (gatewayStatus == sub.Status) continue;

                var previousStatus = sub.Status;
                sub.Status = gatewayStatus;

                if (gatewayStatus == SubscriptionStatus.Cancelled)
                {
                    sub.EndDate ??= DateTime.UtcNow;
                    sub.CancelledAt ??= DateTime.UtcNow;
                }

                await _audit.WriteAsync(new AuditEntry
                {
                    EntityType = "Subscription",
                    EntityId = sub.Id.ToString(),
                    Action = "Reconciled",
                    NewValues = $"Status changed from {previousStatus} to {gatewayStatus} (gateway: {detail.Status})",
                    Timestamp = DateTime.UtcNow
                });

                reconciled++;

                _logger.LogInformation(
                    "Reconciled subscription {Code} for tenant {TenantId}: {Old} → {New}",
                    sub.PaystackSubscriptionCode, sub.TenantId, previousStatus, gatewayStatus);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to reconcile subscription {Code} for tenant {TenantId}",
                    sub.PaystackSubscriptionCode, sub.TenantId);
            }
        }

        if (reconciled > 0)
            await _coreDb.SaveChangesAsync();

        _logger.LogInformation(
            "Subscription reconciliation complete. {Reconciled}/{Total} updated.",
            reconciled, subscriptions.Count);
    }

    /// <summary>
    /// Map Paystack subscription status strings to our SubscriptionStatus enum.
    /// </summary>
    private static SubscriptionStatus MapPaystackStatus(string paystackStatus) => paystackStatus switch
    {
        "active" => SubscriptionStatus.Active,
        "non-renewing" => SubscriptionStatus.NonRenewing,
        "attention" => SubscriptionStatus.PastDue,
        "completed" => SubscriptionStatus.Cancelled,
        "cancelled" => SubscriptionStatus.Cancelled,
        _ => SubscriptionStatus.Active
    };

    /// <summary>
    /// Try to link the real subscription code by verifying the transaction reference with Paystack.
    /// Used as a fallback from charge.success since subscription.create webhooks may arrive late.
    /// </summary>
    private async Task TryLinkSubscriptionFromReferenceAsync(string reference, Guid tenantId)
    {
        try
        {
            var subscription = await _coreDb.Subscriptions
                .Where(s => s.TenantId == tenantId && s.Status == SubscriptionStatus.Active
                    && (s.PaystackSubscriptionCode == null
                        || !s.PaystackSubscriptionCode.StartsWith("SUB_")))
                .OrderByDescending(s => s.StartDate)
                .FirstOrDefaultAsync();

            if (subscription is null) return;

            var verification = await _paystack.VerifyTransactionAsync(reference);
            if (verification?.Customer is not null)
            {
                subscription.PaystackCustomerCode = verification.Customer.CustomerCode;

                var plan = await _coreDb.Plans.FindAsync(subscription.PlanId);
                if (plan is not null && !string.IsNullOrEmpty(plan.PaystackPlanCode))
                {
                    var subs = await _paystack.ListSubscriptionsAsync(
                        verification.Customer.CustomerCode, plan.PaystackPlanCode);
                    var match = subs.FirstOrDefault();
                    if (match is not null && !string.IsNullOrEmpty(match.SubscriptionCode))
                    {
                        subscription.PaystackSubscriptionCode = match.SubscriptionCode;
                        _logger.LogInformation(
                            "Linked subscription {Code} from charge.success for tenant {TenantId}",
                            match.SubscriptionCode, tenantId);
                    }
                }
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Could not link subscription from charge.success for reference {Ref}", reference);
        }
    }
}
