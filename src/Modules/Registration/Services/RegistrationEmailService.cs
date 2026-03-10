using Microsoft.Extensions.Options;
using saas.Infrastructure.Services;
using saas.Shared;

namespace saas.Modules.Registration.Services;

/// <summary>
/// Composes and sends registration-related emails using email templates and SiteSettings.
/// </summary>
public class RegistrationEmailService : IRegistrationEmailService
{
    private readonly IEmailService _email;
    private readonly IEmailTemplateService _templateService;
    private readonly SiteSettings _site;
    private readonly ILogger<RegistrationEmailService> _logger;

    public RegistrationEmailService(
        IEmailService email,
        IEmailTemplateService templateService,
        IOptions<SiteSettings> siteOptions,
        ILogger<RegistrationEmailService> logger)
    {
        _email = email;
        _templateService = templateService;
        _site = siteOptions.Value;
        _logger = logger;
    }

    public async Task SendVerificationEmailAsync(string email, string slug, string verificationToken)
    {
        var baseUrl = _site.BaseUrl.TrimEnd('/');
        var verifyUrl = $"{baseUrl}/register/verify?token={verificationToken}";

        var htmlBody = _templateService.Render("EmailVerification", new Dictionary<string, string>
        {
            ["VerificationUrl"] = verifyUrl
        });

        try
        {
            var result = await _email.SendAsync(new EmailMessage(
                To: email,
                Subject: $"Verify your email — {_site.Name}",
                HtmlBody: htmlBody));

            if (!result.Success)
            {
                _logger.LogError("Verification email failed for {Email} in tenant {Slug}: {Error}", email, slug, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send verification email to {Email} for slug {Slug}", email, slug);
        }
    }

    public async Task SendWelcomeEmailAsync(string adminEmail, string tenantSlug)
    {
        var baseUrl = _site.BaseUrl.TrimEnd('/');
        var loginUrl = $"{baseUrl}/{tenantSlug}/login";

        var htmlBody = _templateService.Render("Welcome", new Dictionary<string, string>
        {
            ["TenantSlug"] = tenantSlug,
            ["LoginUrl"] = loginUrl
        });

        try
        {
            var result = await _email.SendAsync(new EmailMessage(
                To: adminEmail,
                Subject: $"Welcome to {_site.Name} — Your Workspace is Ready",
                HtmlBody: htmlBody));

            if (!result.Success)
            {
                _logger.LogError("Welcome email failed for {Email} in tenant {Slug}: {Error}", adminEmail, tenantSlug, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send welcome email to {Email} for tenant {Slug}", adminEmail, tenantSlug);
        }
    }
}
