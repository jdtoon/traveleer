using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Infrastructure.Provisioning;
using saas.Modules.Registration.Entities;
using saas.Modules.Registration.Models;
using saas.Modules.Registration.Services;
using saas.Shared;
using saas.Modules.Tenancy.Entities;
using Swap.Htmx;

namespace saas.Modules.Registration.Controllers;

public class RegistrationController : SwapController
{
    private readonly ITenantProvisioner _provisioner;
    private readonly CoreDbContext _coreDb;
    private readonly IBillingService _billingService;
    private readonly IBotProtection _botProtection;
    private readonly IRegistrationEmailService _registrationEmail;
    private readonly ILogger<RegistrationController> _logger;

    public RegistrationController(
        ITenantProvisioner provisioner,
        CoreDbContext coreDb,
        IBillingService billingService,
        IBotProtection botProtection,
        IRegistrationEmailService registrationEmail,
        ILogger<RegistrationController> logger)
    {
        _provisioner = provisioner;
        _coreDb = coreDb;
        _billingService = billingService;
        _botProtection = botProtection;
        _registrationEmail = registrationEmail;
        _logger = logger;
    }

    [HttpGet("/register")]
    public async Task<IActionResult> Index([FromQuery] Guid? plan)
    {
        var plans = await _coreDb.Plans
            .Where(p => p.IsActive)
            .OrderBy(p => p.MonthlyPrice)
            .ToListAsync();

        var model = new RegistrationViewModel
        {
            Plans = plans,
            SelectedPlanId = plan
        };

        return SwapView(model);
    }

    [HttpGet("/register/check-slug")]
    public async Task<IActionResult> CheckSlug([FromQuery] string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return PartialView("_SlugValidation", new { IsValid = false, Message = "" });
        }

        var result = await _provisioner.ValidateSlugAsync(slug);

        return PartialView("_SlugValidation", new
        {
            IsValid = result.IsValid,
            Message = result.ErrorMessage ?? "✓ Available"
        });
    }

    [HttpPost("/register")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("registration")]
    public async Task<IActionResult> Register([FromForm] RegisterRequest request)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .First();
            return PartialView("_RegistrationError", new { Message = errors });
        }

        var botCheck = await _botProtection.ValidateAsync(request.CaptchaToken ?? string.Empty);
        if (!botCheck)
        {
            _logger.LogWarning("Bot protection failed for registration attempt: {Slug}", request.Slug);
            return PartialView("_RegistrationError", new { Message = "Bot protection verification failed" });
        }

        // Look up the selected plan
        var plan = await _coreDb.Plans.FindAsync(request.PlanId);
        if (plan is null)
        {
            return PartialView("_RegistrationError", new { Message = "Invalid plan selected" });
        }

        // Validate slug uniqueness
        var slugValidation = await _provisioner.ValidateSlugAsync(request.Slug);
        if (!slugValidation.IsValid)
        {
            return PartialView("_RegistrationError", new { Message = slugValidation.ErrorMessage });
        }

        // Check if there's already a pending (unexpired) registration for this slug
        var existingPending = await _coreDb.PendingRegistrations
            .FirstOrDefaultAsync(p => p.Slug == request.Slug.ToLowerInvariant() && !p.IsVerified && p.ExpiresAt > DateTime.UtcNow);

        if (existingPending is not null)
        {
            // Resend verification email for existing pending registration
            await _registrationEmail.SendVerificationEmailAsync(existingPending.Email, existingPending.Slug, existingPending.VerificationToken);
            _logger.LogInformation("Resent verification email for pending slug {Slug}", request.Slug);
            return PartialView("_VerifyEmailSent", new { Email = request.Email });
        }

        // Clean up any expired pending registrations for this slug
        var expired = await _coreDb.PendingRegistrations
            .Where(p => p.Slug == request.Slug.ToLowerInvariant() && p.ExpiresAt <= DateTime.UtcNow)
            .ToListAsync();
        if (expired.Count > 0)
        {
            _coreDb.PendingRegistrations.RemoveRange(expired);
        }

        // Create a pending registration with a verification token
        var token = GenerateToken();
        var pending = new PendingRegistration
        {
            Id = Guid.NewGuid(),
            Slug = request.Slug.ToLowerInvariant(),
            Email = request.Email,
            PlanId = request.PlanId,
            BillingCycle = request.BillingCycle,
            VerificationToken = token,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            CreatedAt = DateTime.UtcNow
        };
        _coreDb.PendingRegistrations.Add(pending);
        await _coreDb.SaveChangesAsync();

        // Send verification email
        await _registrationEmail.SendVerificationEmailAsync(request.Email, request.Slug, token);

        _logger.LogInformation("Pending registration created for {Slug}, verification email sent to {Email}", request.Slug, request.Email);

        return PartialView("_VerifyEmailSent", new { Email = request.Email });
    }

    /// <summary>
    /// Email verification callback. When the user clicks the verification link,
    /// this endpoint provisions their tenant (free plan) or redirects to payment (paid plan).
    /// </summary>
    [HttpGet("/register/verify")]
    public async Task<IActionResult> Verify([FromQuery] string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return SwapView("VerifyResult", new { Success = false, Slug = (string?)null, Email = (string?)null,
                ErrorMessage = "Invalid verification link." });
        }

        var pending = await _coreDb.PendingRegistrations
            .FirstOrDefaultAsync(p => p.VerificationToken == token && !p.IsVerified);

        if (pending is null)
        {
            return SwapView("VerifyResult", new { Success = false, Slug = (string?)null, Email = (string?)null,
                ErrorMessage = "This verification link is invalid or has already been used." });
        }

        if (pending.ExpiresAt < DateTime.UtcNow)
        {
            return SwapView("VerifyResult", new { Success = false, Slug = (string?)null, Email = (string?)null,
                ErrorMessage = "This verification link has expired. Please register again." });
        }

        // Mark as verified
        pending.IsVerified = true;
        await _coreDb.SaveChangesAsync();

        // Look up the plan
        var plan = await _coreDb.Plans.FindAsync(pending.PlanId);
        if (plan is null)
        {
            return SwapView("VerifyResult", new { Success = false, Slug = (string?)null, Email = (string?)null,
                ErrorMessage = "The selected plan is no longer available." });
        }

        // ── Paid plan: create PendingSetup tenant, redirect to payment ──
        if (plan.MonthlyPrice > 0)
        {
            var billingCycle = pending.BillingCycle == "Annual" ? BillingCycle.Annual : BillingCycle.Monthly;

            var tenant = new Tenant
            {
                Id = Guid.NewGuid(),
                Slug = pending.Slug,
                Name = pending.Slug,
                ContactEmail = pending.Email,
                PlanId = pending.PlanId,
                Status = TenantStatus.PendingSetup,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _coreDb.Tenants.Add(tenant);
            await _coreDb.SaveChangesAsync();

            var billingResult = await _billingService.InitializeSubscriptionAsync(
                new SubscriptionInitRequest(
                    tenant.Id,
                    pending.Email,
                    pending.PlanId,
                    billingCycle));

            if (!billingResult.Success)
            {
                _logger.LogError("Billing initialization failed for {Slug}: {Error}", pending.Slug, billingResult.Error);
                _coreDb.Tenants.Remove(tenant);
                await _coreDb.SaveChangesAsync();

                return SwapView("VerifyResult", new { Success = false, Slug = (string?)null, Email = (string?)null,
                    ErrorMessage = billingResult.Error ?? "Payment initialization failed. Please try again." });
            }

            // Mock/dev billing: no redirect needed — provision immediately
            if (!billingResult.RequiresRedirect)
            {
                var provisionResult = await _provisioner.ProvisionTenantAsync(pending.Slug, pending.Email, pending.PlanId);
                if (!provisionResult.Success)
                {
                    return SwapView("VerifyResult", new { Success = false, Slug = (string?)null, Email = (string?)null,
                        ErrorMessage = provisionResult.ErrorMessage });
                }
                await _registrationEmail.SendWelcomeEmailAsync(pending.Email, pending.Slug);
                _logger.LogInformation("Verified and provisioned paid tenant {Slug} (mock billing)", pending.Slug);
                return SwapView("VerifyResult", new { Success = true, Slug = pending.Slug, Email = pending.Email,
                    ErrorMessage = (string?)null });
            }

            // Real billing: redirect to payment gateway
            if (string.IsNullOrEmpty(billingResult.PaymentUrl))
            {
                _coreDb.Tenants.Remove(tenant);
                await _coreDb.SaveChangesAsync();
                return SwapView("VerifyResult", new { Success = false, Slug = (string?)null, Email = (string?)null,
                    ErrorMessage = "Payment initialization failed. Please try again." });
            }

            _logger.LogInformation("Email verified for {Slug}, redirecting to payment: {Url}", pending.Slug, billingResult.PaymentUrl);
            return Redirect(billingResult.PaymentUrl);
        }

        // ── Free plan: provision immediately ──
        var result = await _provisioner.ProvisionTenantAsync(pending.Slug, pending.Email, pending.PlanId);

        if (!result.Success)
        {
            _logger.LogWarning("Post-verification provisioning failed for {Slug}: {Error}", pending.Slug, result.ErrorMessage);
            return SwapView("VerifyResult", new { Success = false, Slug = (string?)null, Email = (string?)null,
                ErrorMessage = result.ErrorMessage });
        }

        // Set 14-day trial on free plan tenants
        var freeTenant = await _coreDb.Tenants.FirstOrDefaultAsync(t => t.Slug == pending.Slug);
        if (freeTenant is not null)
        {
            freeTenant.TrialEndsAt = DateTime.UtcNow.AddDays(14);
            await _coreDb.SaveChangesAsync();
        }

        await _registrationEmail.SendWelcomeEmailAsync(pending.Email, pending.Slug);

        _logger.LogInformation("Email verified and tenant {Slug} provisioned successfully", pending.Slug);

        return SwapView("VerifyResult", new { Success = true, Slug = pending.Slug, Email = pending.Email,
            ErrorMessage = (string?)null });
    }

    /// <summary>
    /// Callback endpoint for Paystack redirect after payment.
    /// This is a full-page GET request (not HTMX), so it returns a full SwapView.
    /// </summary>
    [HttpGet("/register/callback")]
    public async Task<IActionResult> Callback([FromQuery] string? reference, [FromQuery] string? trxref)
    {
        // Paystack sends both 'reference' and 'trxref' — use whichever is available
        var ref_ = reference ?? trxref;

        if (string.IsNullOrEmpty(ref_))
        {
            _logger.LogWarning("Registration callback received without reference parameter");
            return SwapView("Callback", new { Success = false, Slug = (string?)null, Email = (string?)null,
                ErrorMessage = "Invalid payment reference. Please contact support." });
        }

        // Find the subscription by Paystack reference
        var subscription = await _coreDb.Subscriptions
            .Include(s => s.Tenant)
            .FirstOrDefaultAsync(s => s.PaystackSubscriptionCode == ref_);

        if (subscription is null)
        {
            _logger.LogWarning("Registration callback: no subscription found for reference {Reference}", ref_);
            return SwapView("Callback", new { Success = false, Slug = (string?)null, Email = (string?)null,
                ErrorMessage = "Could not find your registration. Please contact support." });
        }

        var tenant = subscription.Tenant;

        // Verify the transaction and link the real subscription code (SUB_xxx)
        // before provisioning, so cancellation works later
        await _billingService.VerifyAndLinkSubscriptionAsync(ref_);

        // If tenant is still pending setup, provision now
        if (tenant.Status == TenantStatus.PendingSetup)
        {
            var provisionResult = await _provisioner.ProvisionTenantAsync(
                tenant.Slug, tenant.ContactEmail, tenant.PlanId);

            if (!provisionResult.Success)
            {
                _logger.LogError("Post-payment provisioning failed for {Slug}: {Error}",
                    tenant.Slug, provisionResult.ErrorMessage);
                return SwapView("Callback", new { Success = false, Slug = (string?)null, Email = (string?)null,
                    ErrorMessage = "Account setup is in progress. You'll receive an email shortly." });
            }
        }

        // Send welcome email
        await _registrationEmail.SendWelcomeEmailAsync(tenant.ContactEmail, tenant.Slug);

        _logger.LogInformation("Payment callback successful for tenant {Slug}", tenant.Slug);

        return SwapView("Callback", new { Success = true, Slug = tenant.Slug, Email = tenant.ContactEmail,
            ErrorMessage = (string?)null });
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
}
