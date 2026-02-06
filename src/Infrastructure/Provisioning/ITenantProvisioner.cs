namespace saas.Infrastructure.Provisioning;

/// <summary>
/// Service for provisioning new tenants with their isolated database and initial configuration
/// </summary>
public interface ITenantProvisioner
{
    /// <summary>
    /// Provision a new tenant with the specified slug, admin email, and plan
    /// </summary>
    /// <param name="slug">Unique tenant slug for URL and database identification</param>
    /// <param name="adminEmail">Email address for the initial admin user</param>
    /// <param name="planId">Plan ID to assign to the tenant</param>
    /// <returns>Result containing tenant ID or error messages</returns>
    Task<TenantProvisioningResult> ProvisionTenantAsync(string slug, string adminEmail, Guid planId);
    
    /// <summary>
    /// Validate if a slug is available and meets format requirements
    /// </summary>
    /// <param name="slug">Slug to validate</param>
    /// <returns>Validation result with error message if invalid</returns>
    Task<SlugValidationResult> ValidateSlugAsync(string slug);
}

public record TenantProvisioningResult(
    bool Success,
    Guid? TenantId = null,
    string? ErrorMessage = null
);

public record SlugValidationResult(
    bool IsValid,
    string? ErrorMessage = null
);
