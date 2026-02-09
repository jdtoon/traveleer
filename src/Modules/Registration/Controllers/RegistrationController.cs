using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Infrastructure.Provisioning;
using saas.Modules.Registration.Models;
using saas.Modules.Registration.Services;
using saas.Shared;
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

        var result = await _provisioner.ProvisionTenantAsync(request.Slug, request.Email, request.PlanId);

        if (!result.Success)
        {
            _logger.LogWarning("Registration failed for {Slug}: {Error}", request.Slug, result.ErrorMessage);
            return PartialView("_RegistrationError", new { Message = result.ErrorMessage });
        }

        // Delegate email to dedicated service (swallows errors internally)
        await _registrationEmail.SendWelcomeEmailAsync(request.Email, request.Slug);

        _logger.LogInformation("Successfully registered tenant {Slug} with admin {Email}", request.Slug, request.Email);

        return PartialView("_RegistrationSuccess", new { Slug = request.Slug, Email = request.Email });
    }

    /// <summary>
    /// Callback endpoint for Paystack redirect after payment.
    /// Verifies payment status and provisions the tenant if needed.
    /// </summary>
    [HttpGet("/register/callback")]
    public async Task<IActionResult> Callback([FromQuery] string? reference, [FromQuery] string? trxref)
    {
        // Paystack sends both 'reference' and 'trxref' — use whichever is available
        var ref_ = reference ?? trxref;

        if (string.IsNullOrEmpty(ref_))
        {
            _logger.LogWarning("Registration callback received without reference parameter");
            return PartialView("_RegistrationError", new { Message = "Invalid payment reference. Please contact support." });
        }

        // Find the subscription by Paystack reference
        var subscription = await _coreDb.Subscriptions
            .Include(s => s.Tenant)
            .FirstOrDefaultAsync(s => s.PaystackSubscriptionCode == ref_);

        if (subscription is null)
        {
            _logger.LogWarning("Registration callback: no subscription found for reference {Reference}", ref_);
            return PartialView("_RegistrationError", new { Message = "Could not find your registration. Please contact support." });
        }

        var tenant = subscription.Tenant;

        // If tenant is still pending setup, provision now
        if (tenant.Status == TenantStatus.PendingSetup)
        {
            var provisionResult = await _provisioner.ProvisionTenantAsync(
                tenant.Slug, tenant.ContactEmail, tenant.PlanId);

            if (!provisionResult.Success)
            {
                _logger.LogError("Post-payment provisioning failed for {Slug}: {Error}",
                    tenant.Slug, provisionResult.ErrorMessage);
                return PartialView("_RegistrationError", new
                {
                    Message = "Account setup is in progress. You'll receive an email shortly."
                });
            }
        }

        // Send welcome email
        await _registrationEmail.SendWelcomeEmailAsync(tenant.ContactEmail, tenant.Slug);

        _logger.LogInformation("Payment callback successful for tenant {Slug}", tenant.Slug);

        return PartialView("_RegistrationSuccess", new { Slug = tenant.Slug, Email = tenant.ContactEmail });
    }
}
