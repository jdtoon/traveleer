using saas.Infrastructure;
using saas.Infrastructure.Provisioning;
using saas.Modules.Registration.Services;
using saas.Shared;

namespace saas.Modules.Registration;

public class RegistrationModule : IModule
{
    public string Name => "Registration";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITenantProvisioner, TenantProvisionerService>();
        services.AddScoped<IRegistrationEmailService, RegistrationEmailService>();
    }

    public void RegisterMiddleware(IApplicationBuilder app)
    {
        // No middleware needed
    }
}
