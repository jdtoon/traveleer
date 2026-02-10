using saas.Shared;

namespace saas.Modules.Marketing;

public class MarketingModule : IModule
{
    public string Name => "Marketing";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["Marketing"] = "Marketing"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["Marketing"];

    public IReadOnlyList<string> PublicRoutePrefixes =>
    [
        "pricing", "about", "contact", "legal", "sitemap.xml", "robots.txt"
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
