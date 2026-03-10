using saas.Modules.Email.Services;
using saas.Shared;

namespace saas.Modules.Email;

public static class EmailFeatures
{
    public const string Email = "email";
}

public static class EmailPermissions
{
    public const string EmailRead = "email.read";
    public const string EmailSend = "email.send";
}

public class EmailModule : IModule
{
    public string Name => "Email";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["QuoteEmail"] = "Email"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["QuoteEmail"];

    public IReadOnlyList<ModuleFeature> Features =>
    [
        new(EmailFeatures.Email, "Email", "Branded quote email sending and delivery history", MinPlanSlug: "starter")
    ];

    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new(EmailPermissions.EmailRead, "View Email History", "Email", 0),
        new(EmailPermissions.EmailSend, "Send Quote Emails", "Email", 1)
    ];

    public IReadOnlyList<RolePermissionMapping> DefaultRolePermissions =>
    [
        new("Member", EmailPermissions.EmailRead),
        new("Member", EmailPermissions.EmailSend)
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IQuoteEmailService, QuoteEmailService>();
    }
}
