using saas.Modules.Onboarding.Services;
using saas.Shared;

namespace saas.Modules.Onboarding;

public static class OnboardingFeatures
{
    public const string Onboarding = "onboarding";
}

public static class OnboardingPermissions
{
    public const string OnboardingRead = "onboarding.read";
    public const string OnboardingEdit = "onboarding.edit";
}

public class OnboardingModule : IModule
{
    public string Name => "Onboarding";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["Onboarding"] = "Onboarding"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["Onboarding"];

    public IReadOnlyList<ModuleFeature> Features =>
    [
        new(OnboardingFeatures.Onboarding, "Onboarding", "First-use tenant setup wizard", MinPlanSlug: "starter")
    ];

    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new(OnboardingPermissions.OnboardingRead, "View Onboarding", "Onboarding", 0),
        new(OnboardingPermissions.OnboardingEdit, "Edit Onboarding", "Onboarding", 1)
    ];

    public IReadOnlyList<RolePermissionMapping> DefaultRolePermissions =>
    [
        new("Member", OnboardingPermissions.OnboardingRead),
        new("Member", OnboardingPermissions.OnboardingEdit)
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IOnboardingService, OnboardingService>();
    }
}
