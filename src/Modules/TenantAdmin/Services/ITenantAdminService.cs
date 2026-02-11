using saas.Data;
using saas.Data.Tenant;

namespace saas.Modules.TenantAdmin.Services;

public interface ITenantAdminService
{
    // Users
    Task<PaginatedList<UserListItem>> GetUsersAsync(int page = 1, int pageSize = 20);
    Task<InviteUserResult> InviteUserAsync(string email, string? roleId = null);
    Task<bool> DeactivateUserAsync(string userId);
    Task<bool> ActivateUserAsync(string userId);

    // Roles
    Task<List<RoleListItem>> GetRolesAsync();
    Task<List<Permission>> GetPermissionsAsync();
    Task<bool> AssignRoleAsync(string userId, string roleId);
    Task<bool> RemoveRoleAsync(string userId, string roleId);
    Task<RoleListItem?> CreateRoleAsync(string name, string? description);
    Task<bool> UpdateRoleAsync(string roleId, string name, string? description);
    Task<bool> DeleteRoleAsync(string roleId);
    Task<bool> ToggleRolePermissionAsync(string roleId, Guid permissionId);
    Task<List<string>> GetUserRoleIdsAsync(string userId);
    Task<bool> SetUserRolesAsync(string userId, List<string> roleIds);
}

public record InviteUserResult(bool Success, string? Error = null);

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
