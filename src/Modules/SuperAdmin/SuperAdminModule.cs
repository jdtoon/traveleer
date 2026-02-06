using Microsoft.AspNetCore.Mvc;
using saas.Shared;

namespace saas.Modules.SuperAdmin;

public class SuperAdminModule : IModule
{
    public string Name => "SuperAdmin";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void RegisterMiddleware(IApplicationBuilder app)
    {
    }

    public void RegisterMvc(MvcOptions mvcOptions, IMvcBuilder mvcBuilder)
    {
    }
}
