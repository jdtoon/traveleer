using saas.Modules.SuperAdmin.Services;
using saas.Shared;

namespace saas.Modules.SuperAdmin;

public class SuperAdminModule : IModule
{
    public string Name => "SuperAdmin";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["SuperAdmin"] = "SuperAdmin",
        ["Infrastructure"] = "SuperAdmin",
        ["SuperAdminBilling"] = "SuperAdmin"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["SuperAdmin", "Infrastructure", "SuperAdminBilling"];

    public IReadOnlyList<string> PublicRoutePrefixes => ["super-admin"];

    public IReadOnlyList<string> ReservedSlugs => ["super-admin", "admin"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISuperAdminService, SuperAdminService>();
        services.AddScoped<IInfrastructureService, InfrastructureService>();
        services.AddScoped<ITenantInspectionService, TenantInspectionService>();
        services.AddScoped<ISuperAdminAuditService, SuperAdminAuditService>();
        services.AddScoped<IAnnouncementService, AnnouncementService>();
        services.AddHttpClient();
    }

    public void RegisterMiddleware(IApplicationBuilder app)
    {
    }
}
