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
