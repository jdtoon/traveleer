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
    private readonly ILogger<PaystackBillingService> _logger;

    public PaystackBillingService(
        PaystackClient paystack,
        CoreDbContext coreDb,
        InvoiceGenerator invoiceGenerator,
        IOptions<PaystackOptions> options,
        IAuditWriter audit,
        ILogger<PaystackBillingService> logger)
    {
        _paystack = paystack;
        _coreDb = coreDb;
        _invoiceGenerator = invoiceGenerator;
        _options = options.Value;
        _audit = audit;
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
        var amount = request.BillingCycle == BillingCycle.Annual && plan.AnnualPrice.HasValue
            ? plan.AnnualPrice.Value * 100
            : plan.MonthlyPrice * 100;

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
            .Where(s => s.TenantId == tenantId && s.Status == SubscriptionStatus.Active)
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

        // Update the tenant's plan
        tenant.PlanId = newPlanId;

        // If free plan, just update directly
        if (plan.MonthlyPrice == 0)
        {
            await _coreDb.SaveChangesAsync();
            return new PlanChangeResult(true);
        }

        // For paid plan changes, initialize a new payment
        var amount = plan.MonthlyPrice * 100;
        try
        {
            var paystackResult = await _paystack.InitializeTransactionAsync(new PaystackInitializeRequest
            {
                Email = tenant.ContactEmail,
                Amount = (int)amount,
                Currency = plan.Currency ?? "ZAR",
                CallbackUrl = $"{_options.CallbackBaseUrl}/{tenant.Slug}/billing",
                Plan = plan.PaystackPlanCode,
                Metadata = new Dictionary<string, object>
                {
                    ["tenant_id"] = tenantId.ToString(),
                    ["plan_id"] = newPlanId.ToString(),
                    ["action"] = "plan_change"
                }
            });

            await _coreDb.SaveChangesAsync();

            return new PlanChangeResult(true, PaymentUrl: paystackResult?.AuthorizationUrl);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to change plan for tenant {TenantId}", tenantId);
            return new PlanChangeResult(false, Error: "Payment gateway error");
        }
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

                // Create annual plan if different price exists
                if (dbPlan.AnnualPrice.HasValue)
                {
                    await _paystack.CreatePlanAsync(new PaystackCreatePlanRequest
                    {
                        Name = $"{dbPlan.Name} (Annual)",
                        Interval = "annually",
                        Amount = (int)(dbPlan.AnnualPrice.Value * 100),
                        Currency = dbPlan.Currency ?? "ZAR"
                    });
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
            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.EndDate = DateTime.UtcNow;
            await _coreDb.SaveChangesAsync();
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
            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.EndDate = DateTime.UtcNow;
            await _coreDb.SaveChangesAsync();
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

    public async Task<bool> UpdatePlanInGatewayAsync(Guid planId)
    {
        var plan = await _coreDb.Plans.FindAsync(planId);
        if (plan is null || string.IsNullOrEmpty(plan.PaystackPlanCode))
            return false;

        try
        {
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
            _logger.LogError(ex, "Failed to update Paystack plan {PlanCode}", plan.PaystackPlanCode);
            return false;
        }
    }

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
