using saas.Modules.Portal.Services;
using saas.Shared;

namespace saas.Modules.Portal;

public static class PortalFeatures
{
    public const string Portal = "portal";
}

public static class PortalPermissions
{
    public const string PortalManage = "portal.manage";
}

public class PortalModule : IModule
{
    public string Name => "Portal";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["PortalLink"] = "Portal",
        ["Portal"] = "Portal"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["PortalLink", "Portal"];

    public IReadOnlyList<string> PublicRoutePrefixes => ["portal"];

    public IReadOnlyList<ModuleFeature> Features =>
    [
        new(PortalFeatures.Portal, "Client Portal", "Branded portal for clients to view bookings, quotes, and documents", MinPlanSlug: "starter")
    ];

    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new(PortalPermissions.PortalManage, "Manage Portal Links", "Portal", 0)
    ];

    public IReadOnlyList<RolePermissionMapping> DefaultRolePermissions =>
    [
        new("Member", PortalPermissions.PortalManage)
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPortalService, PortalService>();
    }
}
