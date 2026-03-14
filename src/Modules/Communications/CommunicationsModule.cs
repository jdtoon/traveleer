using saas.Modules.Communications.Services;
using saas.Shared;

namespace saas.Modules.Communications;

public class CommunicationsModule : IModule
{
    public string Name => "Communications";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["Communication"] = "Communications"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["Communication"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICommunicationService, CommunicationService>();
    }
}
