using Microsoft.AspNetCore.Identity;
using saas.Data;

namespace saas.Data.Tenant;

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

public class Permission
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Group { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}

public class RolePermission
{
    public string RoleId { get; set; } = string.Empty;
    public AppRole Role { get; set; } = null!;
    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
}
