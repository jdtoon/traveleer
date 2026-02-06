using saas.Shared;

namespace saas.Modules.Registration.Services;

/// <summary>
/// Composes and sends registration-related emails (welcome, provisioning confirmation).
/// </summary>
public interface IRegistrationEmailService
{
    /// <summary>
    /// Sends the welcome email to the newly-registered tenant admin, including login URL and trial details.
    /// </summary>
    Task SendWelcomeEmailAsync(string adminEmail, string tenantSlug);
}
