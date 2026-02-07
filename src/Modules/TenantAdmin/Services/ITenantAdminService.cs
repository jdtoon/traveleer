using saas.Data.Tenant;

namespace saas.Modules.TenantAdmin.Services;

public interface ITenantAdminService
{
    // Users
    Task<List<UserListItem>> GetUsersAsync();
    Task<bool> InviteUserAsync(string email);
    Task<bool> DeactivateUserAsync(string userId);
    Task<bool> ActivateUserAsync(string userId);

    // Roles
    Task<List<RoleListItem>> GetRolesAsync();
    Task<List<Permission>> GetPermissionsAsync();
    Task<bool> AssignRoleAsync(string userId, string roleId);
    Task<bool> RemoveRoleAsync(string userId, string roleId);
}

public class UserListItem
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> Roles { get; set; } = [];
}

public class RoleListItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public List<string> Permissions { get; set; } = [];
}
