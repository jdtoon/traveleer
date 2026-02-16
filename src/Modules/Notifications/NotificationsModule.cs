using saas.Modules.Notifications.Services;
using saas.Shared;

namespace saas.Modules.Notifications;

public class NotificationsModule : IModule
{
    public string Name => "Notifications";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["Notification"] = "Notifications"
    };

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<INotificationService, NotificationService>();
    }
}
