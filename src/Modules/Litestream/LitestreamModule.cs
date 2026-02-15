using saas.Infrastructure.Services;
using saas.Shared;

namespace saas.Modules.Litestream;

public class LitestreamModule : IModule
{
    public string Name => "Litestream";

    public Dictionary<string, string> ControllerViewPaths => new();

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Always bind options + status service so SuperAdmin Backups page works
        services.AddOptions<LitestreamOptions>()
            .BindConfiguration(LitestreamOptions.SectionName);
        services.AddSingleton<ILitestreamStatusService, LitestreamStatusService>();

        if (!configuration.GetValue<bool>("Litestream:Enabled", false)) return;

        services.AddSingleton<LitestreamConfigSyncService>();
        services.AddHostedService(sp => sp.GetRequiredService<LitestreamConfigSyncService>());
        services.AddSingleton<ILitestreamConfigSync>(sp => sp.GetRequiredService<LitestreamConfigSyncService>());
        services.AddSingleton<ILitestreamRestoreService, LitestreamRestoreService>();
        services.AddHostedService<KeyRingBackupService>();
    }

    public void RegisterMiddleware(WebApplication app)
    {
        // No middleware needed
    }
}
