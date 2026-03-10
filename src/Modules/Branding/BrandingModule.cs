using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Branding.Entities;
using saas.Modules.Branding.Services;
using saas.Shared;

namespace saas.Modules.Branding;

public static class BrandingFeatures
{
    public const string Branding = "branding";
}

public static class BrandingPermissions
{
    public const string BrandingRead = "branding.read";
    public const string BrandingEdit = "branding.edit";
}

public class BrandingModule : IModule
{
    public string Name => "Branding";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["Branding"] = "Branding"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["Branding"];

    public IReadOnlyList<ModuleFeature> Features =>
    [
        new(BrandingFeatures.Branding, "Branding", "Tenant visual identity and quote defaults", MinPlanSlug: "starter")
    ];

    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new(BrandingPermissions.BrandingRead, "View Branding", "Branding", 0),
        new(BrandingPermissions.BrandingEdit, "Edit Branding", "Branding", 1)
    ];

    public IReadOnlyList<RolePermissionMapping> DefaultRolePermissions =>
    [
        new("Member", BrandingPermissions.BrandingRead)
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IBrandingService, BrandingService>();
    }

    public async Task SeedTenantAsync(IServiceProvider scopedServices)
    {
        var db = scopedServices.GetRequiredService<TenantDbContext>();
        if (await db.Set<BrandingSettings>().AnyAsync())
        {
            return;
        }

        db.Set<BrandingSettings>().Add(new BrandingSettings());
        await db.SaveChangesAsync();
    }
}
