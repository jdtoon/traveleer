using saas.Modules.Audit.Services;
using saas.Shared;

namespace saas.Modules.Audit;

/// <summary>Feature key constant for the Audit module.</summary>
public static class AuditFeatures
{
    public const string AuditLog = "audit_log";
}

public static class AuditPermissions
{
    public const string AuditRead = "audit.read";
}

public class AuditModule : IModule
{
    public string Name => "Audit";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["AuditLog"] = "Audit",
        ["AuditDashboard"] = "Audit"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["AuditLog", "AuditDashboard"];

    public IReadOnlyList<ModuleFeature> Features =>
    [
        new(AuditFeatures.AuditLog, "Audit Log", MinPlanSlug: "starter")
    ];

    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new(AuditPermissions.AuditRead, "View Audit Log", "Audit", 0)
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // ChannelAuditWriter — singleton channel + background consumer
        services.AddSingleton<ChannelAuditWriter>();
        services.AddSingleton<IAuditWriter>(sp => sp.GetRequiredService<ChannelAuditWriter>());
        services.AddHostedService(sp => sp.GetRequiredService<ChannelAuditWriter>());

        // EF Core interceptor — singleton, resolves scoped services at call time via HttpContext
        services.AddSingleton<AuditSaveChangesInterceptor>();

        // Audit dashboard service — scoped (reads from AuditDbContext)
        services.AddScoped<IAuditDashboardService, AuditDashboardService>();
    }
}
