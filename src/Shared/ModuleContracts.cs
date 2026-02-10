namespace saas.Shared;

/// <summary>
/// Lightweight feature definition declared by a module.
/// Mapped to the Feature entity during core DB seeding.
/// MinPlanSlug determines the minimum plan tier required (by SortOrder).
/// Null means available on ALL plans including Free.
/// </summary>
public record ModuleFeature(
    string Key,
    string Name,
    string? Description = null,
    bool IsGlobal = false,
    string? MinPlanSlug = null);

/// <summary>
/// Lightweight permission definition declared by a module.
/// Mapped to the Permission entity during tenant DB provisioning.
/// </summary>
public record ModulePermission(
    string Key,
    string Name,
    string Group,
    int SortOrder = 0,
    string? Description = null);

/// <summary>
/// Role definition declared by a module for tenant provisioning.
/// Modules contribute system roles that are seeded into every new tenant.
/// </summary>
public record RoleDefinition(
    string Name,
    string? Description = null,
    bool IsSystemRole = true);

/// <summary>
/// Maps a permission to a role. Modules declare which of their permissions
/// should be assigned to which role by default during tenant provisioning.
/// Admin role always gets all permissions automatically — these mappings
/// are for non-admin roles (e.g. "Member" gets "notes.read").
/// </summary>
public record RolePermissionMapping(
    string RoleName,
    string PermissionKey);
