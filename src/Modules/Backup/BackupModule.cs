using saas.Infrastructure.Services;
using saas.Shared;

namespace saas.Modules.Backup;

public class BackupModule : IModule
{
    public string Name => "Backup";

    public Dictionary<string, string> ControllerViewPaths => new();

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BackupOptions>()
            .BindConfiguration(BackupOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHostedService<LitestreamConfigSyncService>();
    }

    public void RegisterMiddleware(WebApplication app)
    {
        // No middleware needed
    }
}
