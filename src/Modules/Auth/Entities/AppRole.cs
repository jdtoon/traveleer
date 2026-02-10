using Microsoft.AspNetCore.Identity;
using saas.Data;

namespace saas.Modules.Auth.Entities;

public class AppRole : IdentityRole, IAuditableEntity
{
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public ICollection<RolePermission> RolePermissions { get; set; } = [];

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
