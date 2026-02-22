using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Modules.Billing.Entities;
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
            Quantity = request.SeatCount,
            StartDate = DateTime.UtcNow,
            NextBillingDate = request.BillingCycle == BillingCycle.Monthly
                ? DateTime.UtcNow.AddMonths(1)
                : DateTime.UtcNow.AddYears(1),
            PaystackSubscriptionCode = $"MOCK-{Guid.NewGuid()}"
        };

        _db.Subscriptions.Add(subscription);

        var plan = await _db.Plans.FindAsync(request.PlanId);
        var amount = plan?.MonthlyPrice ?? 0;

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            Amount = amount,
            Currency = "ZAR",
            Status = PaymentStatus.Success,
            TransactionDate = DateTime.UtcNow,
            GatewayResponse = "MOCK",
            PaystackReference = $"MOCK-TXN-{Guid.NewGuid():N}"
        };
        _db.Payments.Add(payment);

        // Create mock invoice
        var invoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
        _db.Invoices.Add(new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            SubscriptionId = subscription.Id,
            InvoiceNumber = invoiceNumber,
            Subtotal = amount,
            Total = amount,
            Currency = "ZAR",
            Status = InvoiceStatus.Paid,
            IssuedDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow,
            PaidDate = DateTime.UtcNow,
            Description = $"Subscription to {plan?.Name ?? "plan"} ({request.BillingCycle})",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("[MOCK BILLING] Subscription created (no redirect needed): {SubCode}", subscription.PaystackSubscriptionCode);
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

    public async Task<PlanChangeResult> ChangePlanAsync(Guid tenantId, Guid newPlanId, BillingCycle? newCycle = null)
    {
        _logger.LogInformation("[MOCK BILLING] ChangePlan tenant={TenantId} plan={PlanId}", tenantId, newPlanId);

        var preview = await PreviewPlanChangeAsync(tenantId, newPlanId, newCycle);
        if (!preview.IsValid)
            return new PlanChangeResult(false, Error: preview.Error);

        // Update tenant plan
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant is not null) tenant.PlanId = newPlanId;

        // Update existing subscription in-place (include all non-terminal statuses)
        var activeStatuses = new[] { SubscriptionStatus.Active, SubscriptionStatus.Trialing, SubscriptionStatus.NonRenewing, SubscriptionStatus.PastDue };
        var existingSub = await _db.Subscriptions
            .Where(s => s.TenantId == tenantId && activeStatuses.Contains(s.Status))
            .OrderByDescending(s => s.StartDate)
            .FirstOrDefaultAsync();

        // Fallback: find cancelled/suspended subscription to reactivate (avoids UNIQUE constraint on TenantId)
        existingSub ??= await _db.Subscriptions
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.StartDate)
            .FirstOrDefaultAsync();

        if (existingSub is not null)
        {
            existingSub.PlanId = newPlanId;
            existingSub.Status = SubscriptionStatus.Active;
            existingSub.BillingCycle = newCycle ?? existingSub.BillingCycle;
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
                BillingCycle = newCycle ?? BillingCycle.Monthly,
                StartDate = DateTime.UtcNow,
                NextBillingDate = DateTime.UtcNow.AddMonths(1)
            });
        }

        // Record prorated payment (upgrade) or credit (downgrade)
        decimal creditApplied = 0;
        decimal amountCharged = 0;

        if (preview.AmountDue > 0)
        {
            amountCharged = preview.AmountDue;
            var paymentRef = $"MOCK-PRORATE-{Guid.NewGuid():N}";
            _db.Payments.Add(new Payment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Amount = preview.AmountDue,
                Currency = "ZAR",
                Status = PaymentStatus.Success,
                TransactionDate = DateTime.UtcNow,
                GatewayResponse = $"MOCK-PRORATE: Upgrade from {preview.CurrentPlanName} to {preview.NewPlanName}",
                PaystackReference = paymentRef
            });

            var invoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
            _db.Invoices.Add(new Invoice
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                InvoiceNumber = invoiceNumber,
                Subtotal = preview.AmountDue,
                Total = preview.AmountDue,
                Currency = "ZAR",
                Status = InvoiceStatus.Paid,
                IssuedDate = DateTime.UtcNow,
                DueDate = DateTime.UtcNow,
                PaidDate = DateTime.UtcNow,
                PaystackReference = paymentRef,
                Description = $"Plan change: {preview.CurrentPlanName} → {preview.NewPlanName} (prorated)",
                CreatedAt = DateTime.UtcNow
            });
        }
        else if (preview.CreditForNextCycle > 0)
        {
            creditApplied = preview.CreditForNextCycle;
            _db.TenantCredits.Add(new TenantCredit
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Amount = preview.CreditForNextCycle,
                RemainingAmount = preview.CreditForNextCycle,
                Currency = "ZAR",
                Reason = CreditReason.PlanChangeCredit,
                Description = $"Credit from plan change: {preview.CurrentPlanName} → {preview.NewPlanName}",
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        return new PlanChangeResult(true, CreditApplied: creditApplied, AmountCharged: amountCharged);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(Guid tenantId, Guid newPlanId, BillingCycle? newCycle = null)
    {
        var tenant = await _db.Tenants.Include(t => t.Plan).FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return new PlanChangePreview(false, Error: "Tenant not found");

        var newPlan = await _db.Plans.FindAsync(newPlanId);
        if (newPlan is null) return new PlanChangePreview(false, Error: "Plan not found");

        var currentPlan = tenant.Plan;
        if (currentPlan.Id == newPlanId)
            return new PlanChangePreview(false, Error: "Already on this plan");

        var activeStatuses = new[] { SubscriptionStatus.Active, SubscriptionStatus.Trialing, SubscriptionStatus.NonRenewing, SubscriptionStatus.PastDue };
        var existingSub = await _db.Subscriptions
            .Where(s => s.TenantId == tenantId && activeStatuses.Contains(s.Status))
            .OrderByDescending(s => s.StartDate)
            .FirstOrDefaultAsync();

        var currentCycle = existingSub?.BillingCycle ?? BillingCycle.Monthly;
        var targetCycle = newCycle ?? currentCycle;

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
            CurrentCycle: currentCycle,
            NewCycle: targetCycle,
            RemainingDays: remainingDays,
            TotalCycleDays: totalCycleDays,
            UnusedCredit: unusedCredit,
            ProratedNewCost: proratedNewCost,
            AmountDue: amountDue,
            IsUpgrade: isUpgrade,
            CreditForNextCycle: creditForNextCycle
        );
    }

    public async Task<SeatChangeResult> UpdateSeatCountAsync(Guid tenantId, int newSeatCount)
    {
        _logger.LogInformation("[MOCK BILLING] UpdateSeatCount tenant={TenantId} seats={Seats}", tenantId, newSeatCount);

        var sub = await _db.Subscriptions
            .Where(s => s.TenantId == tenantId && s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.StartDate)
            .FirstOrDefaultAsync();

        if (sub is null)
            return new SeatChangeResult(false, Error: "No active subscription");

        var previous = sub.Quantity;
        sub.Quantity = newSeatCount;
        await _db.SaveChangesAsync();

        return new SeatChangeResult(true, PreviousSeats: previous, NewSeats: newSeatCount);
    }

    public async Task<SeatChangePreview> PreviewSeatChangeAsync(Guid tenantId, int newSeatCount)
    {
        var sub = await _db.Subscriptions
            .Include(s => s.Plan)
            .Where(s => s.TenantId == tenantId && s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.StartDate)
            .FirstOrDefaultAsync();

        if (sub is null)
            return new SeatChangePreview(false, Error: "No active subscription");

        var diff = newSeatCount - sub.Quantity;
        var pricePerSeat = sub.Plan.PerSeatMonthlyPrice ?? 0;

        return new SeatChangePreview(
            IsValid: true,
            CurrentSeats: sub.Quantity,
            NewSeats: newSeatCount,
            SeatDifference: diff,
            PricePerSeat: pricePerSeat,
            ProratedAmount: Math.Abs(diff) * pricePerSeat,
            IsIncrease: diff > 0
        );
    }

    public Task<ChargeResult> ChargeOneOffAsync(Guid tenantId, decimal amount, string description)
    {
        _logger.LogInformation("[MOCK BILLING] ChargeOneOff tenant={TenantId} amount={Amount}", tenantId, amount);
        return Task.FromResult(new ChargeResult(true));
    }

    public Task<RefundResult> IssueRefundAsync(Guid paymentId, decimal? amount = null)
    {
        _logger.LogInformation("[MOCK BILLING] IssueRefund payment={PaymentId} amount={Amount}", paymentId, amount);
        return Task.FromResult(new RefundResult(true, AmountRefunded: amount ?? 0));
    }

    public async Task<DiscountResult> ApplyDiscountAsync(Guid tenantId, string discountCode)
    {
        _logger.LogInformation("[MOCK BILLING] ApplyDiscount tenant={TenantId} code={Code}", tenantId, discountCode);

        var discount = await _db.Discounts.FirstOrDefaultAsync(d => d.Code == discountCode && d.IsActive);
        if (discount is null)
            return new DiscountResult(false, Error: "Invalid discount code");

        _db.TenantDiscounts.Add(new TenantDiscount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DiscountId = discount.Id,
            AppliedAt = DateTime.UtcNow,
            RemainingCycles = discount.DurationInCycles,
            IsActive = true
        });

        discount.CurrentRedemptions++;
        await _db.SaveChangesAsync();

        return new DiscountResult(true, DiscountName: discount.Name, DiscountValue: discount.Value, Type: discount.Type);
    }

    public Task<UsageBillingResult> ProcessUsageBillingAsync(Guid tenantId)
    {
        _logger.LogInformation("[MOCK BILLING] ProcessUsageBilling tenant={TenantId}", tenantId);
        return Task.FromResult(new UsageBillingResult(true, TotalUsageCharge: 0));
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

    public async Task<BillingDashboard> GetBillingDashboardAsync(Guid tenantId)
    {
        var tenant = await _db.Tenants
            .Include(t => t.Plan)
            .Include(t => t.ActiveSubscription)
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant is null)
            return new BillingDashboard(
                PlanName: "Unknown", PlanSlug: "", BillingCycle: BillingCycle.Monthly,
                CurrentPrice: 0, Status: SubscriptionStatus.Cancelled,
                NextBillingDate: null, TrialEndsAt: null, IsTrialing: false,
                CurrentSeats: 1, IncludedSeats: null, MaxSeats: null, PerSeatPrice: null,
                CreditBalance: 0, EstimatedNextInvoice: 0,
                UsageSummary: null, ActiveAddOns: null, ActiveDiscounts: null,
                RecentInvoices: [], PaymentMethods: []);

        var sub = tenant.ActiveSubscription;
        var plan = tenant.Plan;
        var creditBalance = await _db.TenantCredits
            .Where(c => c.TenantId == tenantId && c.RemainingAmount > 0)
            .SumAsync(c => c.RemainingAmount);

        var recentInvoices = await _db.Invoices
            .Where(i => i.TenantId == tenantId)
            .OrderByDescending(i => i.IssuedDate)
            .Take(10)
            .Select(i => new InvoiceSummaryLine(i.InvoiceNumber, i.Total, i.Status, i.IssuedDate))
            .ToListAsync();

        return new BillingDashboard(
            PlanName: plan.Name,
            PlanSlug: plan.Slug,
            BillingCycle: sub?.BillingCycle ?? BillingCycle.Monthly,
            CurrentPrice: plan.MonthlyPrice,
            Status: sub?.Status ?? SubscriptionStatus.Active,
            NextBillingDate: sub?.NextBillingDate,
            TrialEndsAt: sub?.TrialEndsAt,
            IsTrialing: sub?.Status == SubscriptionStatus.Trialing,
            CurrentSeats: sub?.Quantity ?? 1,
            IncludedSeats: plan.IncludedSeats,
            MaxSeats: plan.MaxUsers,
            PerSeatPrice: plan.PerSeatMonthlyPrice,
            CreditBalance: creditBalance,
            EstimatedNextInvoice: plan.MonthlyPrice,
            UsageSummary: null,
            ActiveAddOns: null,
            ActiveDiscounts: null,
            RecentInvoices: recentInvoices,
            PaymentMethods: []);
    }
}
