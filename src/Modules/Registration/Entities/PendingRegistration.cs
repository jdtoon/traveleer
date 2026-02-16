namespace saas.Modules.Registration.Entities;

/// <summary>
/// Stores a registration request that is awaiting email verification.
/// Once the user verifies their email, the tenant is provisioned and this record can be cleaned up.
/// </summary>
public class PendingRegistration
{
    public Guid Id { get; set; }

    /// <summary>Desired tenant slug.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Admin email address to verify.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Selected plan ID.</summary>
    public Guid PlanId { get; set; }

    /// <summary>Monthly or Annual.</summary>
    public string BillingCycle { get; set; } = "Monthly";

    /// <summary>URL-safe verification token.</summary>
    public string VerificationToken { get; set; } = string.Empty;

    /// <summary>When the token expires (default: 24 hours after creation).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Whether the email has been verified (token used).</summary>
    public bool IsVerified { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
