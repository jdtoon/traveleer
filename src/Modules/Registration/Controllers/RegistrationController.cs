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
    private readonly IBotProtection _botProtection;
    private readonly IRegistrationEmailService _registrationEmail;
    private readonly ILogger<RegistrationController> _logger;

    public RegistrationController(
        ITenantProvisioner provisioner,
        CoreDbContext coreDb,
        IBotProtection botProtection,
        IRegistrationEmailService registrationEmail,
        ILogger<RegistrationController> logger)
    {
        _provisioner = provisioner;
        _coreDb = coreDb;
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
    /// Callback endpoint for external registration flows (e.g., payment provider redirect).
    /// Stubbed for now — will be implemented in Phase 8 (Billing &amp; Paystack).
    /// </summary>
    [HttpGet("/register/callback")]
    public IActionResult Callback()
    {
        return NotFound();
    }
}
