using Microsoft.Extensions.Options;
using saas.Shared;

namespace saas.Modules.Registration.Services;

/// <summary>
/// Composes and sends registration-related emails using SiteSettings for dynamic URLs and branding.
/// </summary>
public class RegistrationEmailService : IRegistrationEmailService
{
    private readonly IEmailService _email;
    private readonly SiteSettings _site;
    private readonly ILogger<RegistrationEmailService> _logger;

    public RegistrationEmailService(
        IEmailService email,
        IOptions<SiteSettings> siteOptions,
        ILogger<RegistrationEmailService> logger)
    {
        _email = email;
        _site = siteOptions.Value;
        _logger = logger;
    }

    public async Task SendWelcomeEmailAsync(string adminEmail, string tenantSlug)
    {
        var baseUrl = _site.BaseUrl.TrimEnd('/');
        var workspaceUrl = $"{baseUrl}/{tenantSlug}";
        var loginUrl = $"{workspaceUrl}/login";
        var siteName = _site.Name;

        var htmlBody = $"""
            <div style="font-family: system-ui, -apple-system, sans-serif; max-width: 600px; margin: 0 auto;">
                <h1 style="color: #333;">Welcome to {siteName}!</h1>
                <p>Your workspace has been created successfully.</p>
                <table style="margin: 1.5rem 0; border-collapse: collapse;">
                    <tr>
                        <td style="padding: 0.5rem 1rem 0.5rem 0; font-weight: bold; color: #555;">Workspace URL</td>
                        <td style="padding: 0.5rem 0;"><a href="{workspaceUrl}" style="color: #6366f1;">{workspaceUrl}</a></td>
                    </tr>
                    <tr>
                        <td style="padding: 0.5rem 1rem 0.5rem 0; font-weight: bold; color: #555;">Login URL</td>
                        <td style="padding: 0.5rem 0;"><a href="{loginUrl}" style="color: #6366f1;">{loginUrl}</a></td>
                    </tr>
                </table>
                <p>Your workspace is on a <strong>14-day free trial</strong>. Log in using the magic link system — just enter your email address at the login page.</p>
                <p style="margin-top: 2rem;">
                    <a href="{loginUrl}" style="display: inline-block; padding: 0.75rem 1.5rem; background: #6366f1; color: #fff; text-decoration: none; border-radius: 0.5rem; font-weight: bold;">
                        Go to Login →
                    </a>
                </p>
                <hr style="margin-top: 2rem; border: none; border-top: 1px solid #eee;" />
                <p style="font-size: 0.875rem; color: #888;">You're receiving this because someone registered a workspace with this email on {siteName}. If this wasn't you, you can ignore this email.</p>
            </div>
            """;

        var plainBody = $"""
            Welcome to {siteName}!

            Your workspace has been created successfully.

            Workspace URL: {workspaceUrl}
            Login URL: {loginUrl}

            Your workspace is on a 14-day free trial.
            Log in using the magic link system — just enter your email at the login page.

            ---
            You're receiving this because someone registered a workspace with this email on {siteName}.
            If this wasn't you, you can ignore this email.
            """;

        try
        {
            await _email.SendAsync(new EmailMessage(
                To: adminEmail,
                Subject: $"Welcome to {siteName} — Your Workspace is Ready",
                HtmlBody: htmlBody,
                PlainTextBody: plainBody
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send welcome email to {Email} for tenant {Slug}", adminEmail, tenantSlug);
            // Don't fail registration if email fails — log and swallow
        }
    }
}
