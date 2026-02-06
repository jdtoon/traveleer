using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Infrastructure.Provisioning;
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
    public async Task<IActionResult> Index()
    {
        var plans = await _coreDb.Plans
            .Where(p => p.IsActive)
            .OrderBy(p => p.MonthlyPrice)
            .ToListAsync();

        return SwapView(plans);
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
    public async Task<IActionResult> Register(
        [FromForm] string slug,
        [FromForm] string email,
        [FromForm] Guid planId,
        [FromForm] string? captchaToken)
    {
        var botCheck = await _botProtection.ValidateAsync(captchaToken ?? string.Empty);
        if (!botCheck)
        {
            _logger.LogWarning("Bot protection failed for registration attempt: {Slug}", slug);
            return PartialView("_RegistrationError", new { Message = "Bot protection verification failed" });
        }

        var result = await _provisioner.ProvisionTenantAsync(slug, email, planId);

        if (!result.Success)
        {
            _logger.LogWarning("Registration failed for {Slug}: {Error}", slug, result.ErrorMessage);
            return PartialView("_RegistrationError", new { Message = result.ErrorMessage });
        }

        // Delegate email to dedicated service (swallows errors internally)
        await _registrationEmail.SendWelcomeEmailAsync(email, slug);

        _logger.LogInformation("Successfully registered tenant {Slug} with admin {Email}", slug, email);

        return PartialView("_RegistrationSuccess", new { Slug = slug, Email = email });
    }
}
