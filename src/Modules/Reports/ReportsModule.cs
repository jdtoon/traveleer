using saas.Modules.Reports.Services;
using saas.Shared;

namespace saas.Modules.Reports;

public static class ReportFeatures
{
    public const string Reports = "reports";
}

public static class ReportPermissions
{
    public const string ReportsRead = "reports.read";
}

public class ReportsModule : IModule
{
    public string Name => "Reports";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["Report"] = "Reports"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["Report"];

    public IReadOnlyList<ModuleFeature> Features =>
    [
        new(ReportFeatures.Reports, "Reports & Analytics", "Dashboard widgets, revenue, profitability, and pipeline analysis", MinPlanSlug: "starter")
    ];

    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new(ReportPermissions.ReportsRead, "View Reports", "Reports", 0)
    ];

    public IReadOnlyList<RolePermissionMapping> DefaultRolePermissions =>
    [
        new("Member", ReportPermissions.ReportsRead)
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IReportService, ReportService>();
    }
}
