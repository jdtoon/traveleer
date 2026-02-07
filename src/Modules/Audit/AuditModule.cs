using saas.Data.Core;
using saas.Modules.Audit.Services;
using saas.Shared;

namespace saas.Modules.Audit;

/// <summary>Feature key constant for the Audit module.</summary>
public static class AuditFeatures
{
    public const string AuditLog = "audit_log";
}

public class AuditModule : IModule
{
    public string Name => "Audit";

    public IReadOnlyList<Feature> Features =>
    [
        new() { Id = Guid.NewGuid(), Key = AuditFeatures.AuditLog, Name = "Audit Log", Module = Name, IsGlobal = false, IsEnabled = true }
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // ChannelAuditWriter — singleton channel + background consumer
        services.AddSingleton<ChannelAuditWriter>();
        services.AddHostedService(sp => sp.GetRequiredService<ChannelAuditWriter>());

        // EF Core interceptor — singleton, resolves scoped services at call time via HttpContext
        services.AddSingleton<AuditSaveChangesInterceptor>();
    }
}
