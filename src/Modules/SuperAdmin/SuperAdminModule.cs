using Microsoft.AspNetCore.Mvc;
using saas.Modules.SuperAdmin.Services;
using saas.Shared;

namespace saas.Modules.SuperAdmin;

public class SuperAdminModule : IModule
{
    public string Name => "SuperAdmin";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["SuperAdmin"] = "SuperAdmin"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["SuperAdmin"];

    public IReadOnlyList<string> PublicRoutePrefixes => ["super-admin"];

    public IReadOnlyList<string> ReservedSlugs => ["super-admin", "admin"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISuperAdminService, SuperAdminService>();
    }

    public void RegisterMiddleware(IApplicationBuilder app)
    {
    }

    public void RegisterMvc(MvcOptions mvcOptions, IMvcBuilder mvcBuilder)
    {
    }
}
