using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using saas.Data.Core;
using saas.Modules.Billing.Entities;
using saas.Shared;

namespace saas.Modules.Billing.Services;

public interface IDunningService
{
    Task OnPaymentFailedAsync(Guid tenantId, Guid? invoiceId = null);
    Task<bool> RetryChargeAsync(Guid tenantId);
    Task ProcessGracePeriodsAsync();
    Task ReactivateAsync(Guid tenantId);
}

public class DunningService : IDunningService
{
    private readonly CoreDbContext _db;
    private readonly BillingOptions _options;
    private readonly IEmailService _emailService;
    private readonly ILogger<DunningService> _logger;

    public DunningService(
        CoreDbContext db,
        IOptions<BillingOptions> options,
        IEmailService emailService,
        ILogger<DunningService> logger)
    {
        _db = db;
        _options = options.Value;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task OnPaymentFailedAsync(Guid tenantId, Guid? invoiceId = null)
    {
        var sub = await _db.Subscriptions
            .Include(s => s.Tenant)
            .Where(s => s.TenantId == tenantId && s.Status == SubscriptionStatus.Active)
            .FirstOrDefaultAsync();

        if (sub is null) return;

        sub.Status = SubscriptionStatus.PastDue;
        sub.GracePeriodEndsAt = DateTime.UtcNow.AddDays(_options.GracePeriod.Days);

        if (invoiceId.HasValue)
        {
            var invoice = await _db.Invoices.FindAsync(invoiceId.Value);
            if (invoice is not null)
                invoice.Status = InvoiceStatus.Overdue;
        }

        await _db.SaveChangesAsync();

        // Send payment failed email
        if (sub.Tenant is not null)
        {
            try
            {
                await _emailService.SendAsync(new EmailMessage(
                    To: sub.Tenant.ContactEmail,
                    Subject: "Payment failed — action required",
                    HtmlBody: $"""
                        <h2>Payment Failed</h2>
                        <p>We were unable to process the payment for your <strong>{sub.Tenant.Name}</strong> subscription.</p>
                        <p>Please update your payment method within {_options.GracePeriod.Days} days to avoid service interruption.</p>
                        <p>We will automatically retry the payment. If the issue persists, please contact support.</p>
                        """,
                    PlainTextBody: $"Payment failed for {sub.Tenant.Name}. Please update your payment method within {_options.GracePeriod.Days} days."
                ));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send payment failure email to {Email}", sub.Tenant.ContactEmail);
            }
        }

        _logger.LogWarning("Payment failed for tenant {TenantId}, grace period until {GracePeriodEnd}",
            tenantId, sub.GracePeriodEndsAt);
    }

    public async Task<bool> RetryChargeAsync(Guid tenantId)
    {
        var sub = await _db.Subscriptions
            .Include(s => s.Tenant)
            .Where(s => s.TenantId == tenantId && s.Status == SubscriptionStatus.PastDue)
            .FirstOrDefaultAsync();

        if (sub is null) return false;

        // Check if we have a saved authorization to retry
        if (string.IsNullOrEmpty(sub.PaystackAuthorizationCode))
        {
            _logger.LogWarning("Cannot retry charge for tenant {TenantId}: no saved authorization", tenantId);
            return false;
        }

        // Note: Actual Paystack charge_authorization would be called here by PaystackBillingService.
        // For now, mark it as needing external retry via the billing service.
        _logger.LogInformation("Retry charge requested for tenant {TenantId}", tenantId);
        return false; // Will be wired to actual Paystack charge in PaystackBillingService
    }

    public async Task ProcessGracePeriodsAsync()
    {
        var now = DateTime.UtcNow;

        // Find subscriptions past grace period
        var expired = await _db.Subscriptions
            .Include(s => s.Tenant)
            .Where(s => s.Status == SubscriptionStatus.PastDue
                && s.GracePeriodEndsAt.HasValue
                && s.GracePeriodEndsAt <= now)
            .ToListAsync();

        foreach (var sub in expired)
        {
            sub.Status = SubscriptionStatus.Cancelled;
            sub.CancelledAt = now;

            if (sub.Tenant is not null)
            {
                sub.Tenant.Status = TenantStatus.Suspended;

                try
                {
                    await _emailService.SendAsync(new EmailMessage(
                        To: sub.Tenant.ContactEmail,
                        Subject: "Account suspended — payment overdue",
                        HtmlBody: $"""
                            <h2>Account Suspended</h2>
                            <p>Your <strong>{sub.Tenant.Name}</strong> account has been suspended due to an overdue payment.</p>
                            <p>Please update your payment method and settle the outstanding balance to reactivate your account.</p>
                            """,
                        PlainTextBody: $"Your {sub.Tenant.Name} account has been suspended due to an overdue payment."
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send suspension email to {Email}", sub.Tenant.ContactEmail);
                }
            }

            _logger.LogWarning("Tenant {TenantId} suspended after grace period expiry", sub.TenantId);
        }

        if (expired.Count > 0)
            await _db.SaveChangesAsync();

        // Find subscriptions needing retry — past due but within grace period
        var dunningIntervalHours = _options.GracePeriod.DunningIntervalHours;
        var pastDue = await _db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.PastDue
                && s.GracePeriodEndsAt.HasValue
                && s.GracePeriodEndsAt > now)
            .ToListAsync();

        foreach (var sub in pastDue)
        {
            // Check if enough time has passed since last retry (using UpdatedAt as proxy)
            var lastAttempt = sub.UpdatedAt ?? sub.CreatedAt;
            if ((now - lastAttempt).TotalHours < dunningIntervalHours)
                continue;

            await RetryChargeAsync(sub.TenantId);
        }

        _logger.LogInformation("Grace period processing complete: {Expired} expired, {PastDue} in dunning",
            expired.Count, pastDue.Count);
    }

    public async Task ReactivateAsync(Guid tenantId)
    {
        var sub = await _db.Subscriptions
            .Include(s => s.Tenant)
            .Where(s => s.TenantId == tenantId
                && (s.Status == SubscriptionStatus.PastDue || s.Status == SubscriptionStatus.Cancelled))
            .OrderByDescending(s => s.StartDate)
            .FirstOrDefaultAsync();

        if (sub is null) return;

        sub.Status = SubscriptionStatus.Active;
        sub.GracePeriodEndsAt = null;

        if (sub.Tenant is not null && sub.Tenant.Status == TenantStatus.Suspended)
            sub.Tenant.Status = TenantStatus.Active;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Reactivated subscription for tenant {TenantId}", tenantId);
    }
}
