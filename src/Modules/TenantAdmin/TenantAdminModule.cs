using saas.Modules.TenantAdmin.Services;
using saas.Shared;

namespace saas.Modules.TenantAdmin;

public static class TenantAdminPermissions
{
    // User Management
    public const string UsersRead = "users.read";
    public const string UsersCreate = "users.create";
    public const string UsersEdit = "users.edit";
    public const string UsersDelete = "users.delete";

    // Role Management
    public const string RolesRead = "roles.read";
    public const string RolesCreate = "roles.create";
    public const string RolesEdit = "roles.edit";
    public const string RolesDelete = "roles.delete";

    // Settings
    public const string SettingsRead = "settings.read";
    public const string SettingsEdit = "settings.edit";
}

public class TenantAdminModule : IModule
{
    public string Name => "TenantAdmin";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["TenantAdmin"] = "TenantAdmin",
        ["TenantBilling"] = "TenantAdmin",
        ["TenantSettings"] = "TenantAdmin",
        ["Invitation"] = "TenantAdmin"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["TenantAdmin", "TenantBilling", "TenantSettings", "Invitation"];

    public IReadOnlyList<ModuleFeature> Features =>
    [
        new("custom_roles", "Custom Roles", MinPlanSlug: "starter")
    ];

    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new(TenantAdminPermissions.UsersRead, "View Users", "Users", 0),
        new(TenantAdminPermissions.UsersCreate, "Invite Users", "Users", 1),
        new(TenantAdminPermissions.UsersEdit, "Edit Users", "Users", 2),
        new(TenantAdminPermissions.UsersDelete, "Deactivate Users", "Users", 3),
        new(TenantAdminPermissions.RolesRead, "View Roles", "Roles", 0),
        new(TenantAdminPermissions.RolesCreate, "Create Roles", "Roles", 1),
        new(TenantAdminPermissions.RolesEdit, "Edit Roles", "Roles", 2),
        new(TenantAdminPermissions.RolesDelete, "Delete Roles", "Roles", 3),
        new(TenantAdminPermissions.SettingsRead, "View Settings", "Settings", 0),
        new(TenantAdminPermissions.SettingsEdit, "Edit Settings", "Settings", 1),
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITenantAdminService, TenantAdminService>();
        services.AddScoped<ITenantLifecycleService, TenantLifecycleService>();
    }
}
