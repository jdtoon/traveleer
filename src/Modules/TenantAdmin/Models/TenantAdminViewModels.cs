using saas.Modules.Auth.Entities;
using saas.Modules.TenantAdmin.Entities;
using saas.Modules.TenantAdmin.Services;

namespace saas.Modules.TenantAdmin.Models;

public class RoleDetailViewModel
{
    public RoleListItem Role { get; set; } = null!;
    public List<Permission> AllPermissions { get; set; } = [];
}

public class InviteUserViewModel
{
    public List<RoleListItem> AvailableRoles { get; set; } = [];
}

public class ManageUserRolesViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public List<RoleListItem> AllRoles { get; set; } = [];
    public List<string> AssignedRoleIds { get; set; } = [];
}

public class TenantSettingsViewModel
{
    public string Name { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string? CustomDomain { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime? ScheduledDeletionAt { get; set; }
}

public class TenantSettingsUpdateModel
{
    public string Name { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
}
