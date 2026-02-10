using Microsoft.AspNetCore.Identity;
using saas.Data;

namespace saas.Modules.Auth.Entities;

public class AppUser : IdentityUser, IAuditableEntity
{
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
