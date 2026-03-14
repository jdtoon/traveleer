using saas.Modules.Itineraries.Services;
using saas.Shared;

namespace saas.Modules.Itineraries;

public static class ItineraryFeatures
{
    public const string Itineraries = "itineraries";
}

public static class ItineraryPermissions
{
    public const string ItinerariesRead = "itineraries.read";
    public const string ItinerariesCreate = "itineraries.create";
    public const string ItinerariesEdit = "itineraries.edit";
    public const string ItinerariesDelete = "itineraries.delete";
    public const string ItinerariesShare = "itineraries.share";
}

public class ItinerariesModule : IModule
{
    public string Name => "Itineraries";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["Itinerary"] = "Itineraries",
        ["SharedItinerary"] = "Itineraries"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["Itinerary", "SharedItinerary"];

    public IReadOnlyList<string> PublicRoutePrefixes => ["shared"];

    public IReadOnlyList<ModuleFeature> Features =>
    [
        new(ItineraryFeatures.Itineraries, "Itineraries", "Day-by-day trip builder with sharing and export", MinPlanSlug: "starter")
    ];

    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new(ItineraryPermissions.ItinerariesRead, "View Itineraries", "Itineraries", 0),
        new(ItineraryPermissions.ItinerariesCreate, "Create Itineraries", "Itineraries", 1),
        new(ItineraryPermissions.ItinerariesEdit, "Edit Itineraries", "Itineraries", 2),
        new(ItineraryPermissions.ItinerariesDelete, "Delete Itineraries", "Itineraries", 3),
        new(ItineraryPermissions.ItinerariesShare, "Share Itineraries", "Itineraries", 4)
    ];

    public IReadOnlyList<RolePermissionMapping> DefaultRolePermissions =>
    [
        new("Member", ItineraryPermissions.ItinerariesRead)
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IItineraryService, ItineraryService>();
    }
}
