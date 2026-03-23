using saas.Data;

namespace saas.Modules.SuperAdmin.Entities;

public class SuperAdmin : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }

    // Two-Factor Authentication
    public string? TwoFactorSecret { get; set; }
    public string? TwoFactorRecoveryCodes { get; set; }
    public bool IsTwoFactorEnabled { get; set; }

    // Password authentication (optional — null means password login is not configured)
    public string? PasswordHash { get; set; }
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpiry { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
