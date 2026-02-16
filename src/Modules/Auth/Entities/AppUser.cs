using Microsoft.AspNetCore.Identity;
using saas.Data;

namespace saas.Modules.Auth.Entities;

public class AppUser : IdentityUser, IAuditableEntity
{
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }

    // Profile fields
    public string? AvatarUrl { get; set; }
    public string? TimeZone { get; set; }

    // Email verification
    public bool IsEmailVerified { get; set; }
    public string? EmailVerificationToken { get; set; }
    public DateTime? EmailVerifiedAt { get; set; }

    // Two-factor authentication
    public string? TwoFactorSecret { get; set; }
    public string? TwoFactorRecoveryCodes { get; set; }
    public bool IsTwoFactorEnabled { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
