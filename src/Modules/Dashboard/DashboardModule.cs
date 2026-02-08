using saas.Shared;

namespace saas.Modules.Dashboard;

public class DashboardModule : IModule
{
    public string Name => "Dashboard";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["Dashboard"] = "Dashboard"
    };

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // No services needed — Dashboard is a lightweight view-only module
    }
}
