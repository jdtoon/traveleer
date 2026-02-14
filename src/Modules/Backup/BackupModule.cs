using saas.Infrastructure.Services;
using saas.Shared;

namespace saas.Modules.Backup;

public class BackupModule : IModule
{
    public string Name => "Backup";

    public Dictionary<string, string> ControllerViewPaths => new();

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Always bind options + status service so SuperAdmin Backups page works
        services.AddOptions<BackupOptions>()
            .BindConfiguration(BackupOptions.SectionName);
        services.AddSingleton<IBackupStatusService, BackupStatusService>();

        if (!configuration.GetValue<bool>("Backup:Enabled", false)) return;

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
