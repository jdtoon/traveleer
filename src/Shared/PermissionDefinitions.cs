using saas.Data.Tenant;

namespace saas.Shared;

/// <summary>
/// Cross-cutting permission definitions that don't belong to any specific module yet.
/// Module-owned permissions are defined in each module's IModule.Permissions property.
/// These will move to their respective modules as they are built (e.g. TenantAdminModule).
/// </summary>
public static class PermissionDefinitions
{
    // User Management (future TenantAdmin module)
    public const string UsersRead = "users.read";
    public const string UsersCreate = "users.create";
    public const string UsersEdit = "users.edit";
    public const string UsersDelete = "users.delete";

    // Role Management (future TenantAdmin module)
    public const string RolesRead = "roles.read";
    public const string RolesCreate = "roles.create";
    public const string RolesEdit = "roles.edit";
    public const string RolesDelete = "roles.delete";

    // Settings (future TenantAdmin module)
    public const string SettingsRead = "settings.read";
    public const string SettingsEdit = "settings.edit";

    /// <summary>
    /// Returns only the cross-cutting permissions not owned by any module.
    /// Combined with IModule.Permissions at startup for seeding.
    /// </summary>
    public static List<Permission> GetAll() =>
    [
        new() { Id = Guid.NewGuid(), Key = UsersRead, Name = "View Users", Group = "Users", SortOrder = 0 },
        new() { Id = Guid.NewGuid(), Key = UsersCreate, Name = "Invite Users", Group = "Users", SortOrder = 1 },
        new() { Id = Guid.NewGuid(), Key = UsersEdit, Name = "Edit Users", Group = "Users", SortOrder = 2 },
        new() { Id = Guid.NewGuid(), Key = UsersDelete, Name = "Deactivate Users", Group = "Users", SortOrder = 3 },
        new() { Id = Guid.NewGuid(), Key = RolesRead, Name = "View Roles", Group = "Roles", SortOrder = 0 },
        new() { Id = Guid.NewGuid(), Key = RolesCreate, Name = "Create Roles", Group = "Roles", SortOrder = 1 },
        new() { Id = Guid.NewGuid(), Key = RolesEdit, Name = "Edit Roles", Group = "Roles", SortOrder = 2 },
        new() { Id = Guid.NewGuid(), Key = RolesDelete, Name = "Delete Roles", Group = "Roles", SortOrder = 3 },
        new() { Id = Guid.NewGuid(), Key = SettingsRead, Name = "View Settings", Group = "Settings", SortOrder = 0 },
        new() { Id = Guid.NewGuid(), Key = SettingsEdit, Name = "Edit Settings", Group = "Settings", SortOrder = 1 },
    ];
}
