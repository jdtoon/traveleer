using saas.Modules.RateCards.Services;
using saas.Shared;

namespace saas.Modules.RateCards;

public static class RateCardFeatures
{
    public const string RateCards = "ratecards";
}

public static class RateCardPermissions
{
    public const string RateCardsRead = "ratecards.read";
    public const string RateCardsCreate = "ratecards.create";
    public const string RateCardsEdit = "ratecards.edit";
    public const string RateCardsDelete = "ratecards.delete";
}

public class RateCardsModule : IModule
{
    public string Name => "RateCards";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["RateCard"] = "RateCards"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["RateCard"];

    public IReadOnlyList<ModuleFeature> Features =>
    [
        new(RateCardFeatures.RateCards, "Rate Cards", "Seasonal supplier contracts and room pricing for hotel inventory", MinPlanSlug: "starter")
    ];

    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new(RateCardPermissions.RateCardsRead, "View Rate Cards", "Rate Cards", 0),
        new(RateCardPermissions.RateCardsCreate, "Create Rate Cards", "Rate Cards", 1),
        new(RateCardPermissions.RateCardsEdit, "Edit Rate Cards", "Rate Cards", 2),
        new(RateCardPermissions.RateCardsDelete, "Delete Rate Cards", "Rate Cards", 3)
    ];

    public IReadOnlyList<RolePermissionMapping> DefaultRolePermissions =>
    [
        new("Member", RateCardPermissions.RateCardsRead)
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IRateCardService, RateCardService>();
    }
}
