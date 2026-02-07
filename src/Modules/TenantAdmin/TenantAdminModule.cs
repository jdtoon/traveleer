using saas.Data.Core;
using saas.Data.Tenant;
using saas.Modules.TenantAdmin.Services;
using saas.Shared;

namespace saas.Modules.TenantAdmin;

public static class TenantAdminPermissions
{
    public const string UsersRead = PermissionDefinitions.UsersRead;
    public const string UsersCreate = PermissionDefinitions.UsersCreate;
    public const string UsersEdit = PermissionDefinitions.UsersEdit;
    public const string UsersDelete = PermissionDefinitions.UsersDelete;
    public const string RolesRead = PermissionDefinitions.RolesRead;
    public const string RolesCreate = PermissionDefinitions.RolesCreate;
    public const string RolesEdit = PermissionDefinitions.RolesEdit;
    public const string RolesDelete = PermissionDefinitions.RolesDelete;
}

public class TenantAdminModule : IModule
{
    public string Name => "TenantAdmin";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["TenantAdmin"] = "TenantAdmin",
        ["TenantBilling"] = "TenantAdmin"
    };

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITenantAdminService, TenantAdminService>();
    }
}
