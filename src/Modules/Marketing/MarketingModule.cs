using saas.Shared;

namespace saas.Modules.Marketing;

public class MarketingModule : IModule
{
    public string Name => "Marketing";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["Marketing"] = "Marketing"
    };

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
