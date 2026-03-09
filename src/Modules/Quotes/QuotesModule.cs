using saas.Modules.Quotes.Services;
using saas.Shared;

namespace saas.Modules.Quotes;

public static class QuoteFeatures
{
    public const string Quotes = "quotes";
}

public static class QuotePermissions
{
    public const string QuotesRead = "quotes.read";
    public const string QuotesCreate = "quotes.create";
    public const string QuotesEdit = "quotes.edit";
    public const string QuotesDelete = "quotes.delete";
}

public class QuotesModule : IModule
{
    public string Name => "Quotes";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["Quote"] = "Quotes"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["Quote"];

    public IReadOnlyList<ModuleFeature> Features =>
    [
        new(QuoteFeatures.Quotes, "Quotes", "Client-facing quote builder backed by saved rate cards", MinPlanSlug: "starter")
    ];

    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new(QuotePermissions.QuotesRead, "View Quotes", "Quotes", 0),
        new(QuotePermissions.QuotesCreate, "Create Quotes", "Quotes", 1),
        new(QuotePermissions.QuotesEdit, "Edit Quotes", "Quotes", 2),
        new(QuotePermissions.QuotesDelete, "Delete Quotes", "Quotes", 3)
    ];

    public IReadOnlyList<RolePermissionMapping> DefaultRolePermissions =>
    [
        new("Member", QuotePermissions.QuotesRead)
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IQuoteService, QuoteService>();
        services.AddScoped<IQuoteNumberingService, QuoteNumberingService>();
    }
}
