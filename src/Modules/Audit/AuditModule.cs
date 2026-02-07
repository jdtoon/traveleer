using saas.Modules.Audit.Services;
using saas.Shared;

namespace saas.Modules.Audit;

public class AuditModule : IModule
{
    public string Name => "Audit";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // ChannelAuditWriter replaces the NullAuditWriter registered in AddCoreServices.
        // Last registration wins in DI, so this takes precedence.
        services.AddSingleton<ChannelAuditWriter>();
        services.AddScoped<IAuditWriter>(sp => sp.GetRequiredService<ChannelAuditWriter>());
        services.AddHostedService(sp => sp.GetRequiredService<ChannelAuditWriter>());
    }
}
