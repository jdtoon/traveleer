using saas.Shared;

namespace saas.Modules.Registration.Services;

/// <summary>
/// Composes and sends registration-related emails (verification, welcome, provisioning confirmation).
/// </summary>
public interface IRegistrationEmailService
{
    /// <summary>
    /// Sends a verification email with a link the user must click to confirm their email before provisioning.
    /// </summary>
    Task SendVerificationEmailAsync(string email, string slug, string verificationToken);

    /// <summary>
    /// Sends the welcome email to the newly-registered tenant admin, including login URL and trial details.
    /// </summary>
    Task SendWelcomeEmailAsync(string adminEmail, string tenantSlug);
}
