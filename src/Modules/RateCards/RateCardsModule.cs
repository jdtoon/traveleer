using saas.Modules.RateCards.Services;
using saas.Shared;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Inventory.Entities;
using saas.Modules.RateCards.Entities;

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
        services.AddScoped<IRateCardTemplateService, RateCardTemplateService>();
        services.AddScoped<IRateCardImportExportService, RateCardImportExportService>();
    }

    public async Task SeedTenantAsync(IServiceProvider scopedServices)
    {
        var templates = scopedServices.GetRequiredService<IRateCardTemplateService>();
        await templates.EnsureSystemTemplatesAsync();
    }

    public async Task SeedDemoDataAsync(IServiceProvider scopedServices)
    {
        var db = scopedServices.GetRequiredService<TenantDbContext>();
        if (await db.RateCardTemplates.AnyAsync(x => !x.IsSystemTemplate))
        {
            return;
        }

        var hotelCard = await db.RateCards
            .AsNoTracking()
            .Include(x => x.InventoryItem)
            .Include(x => x.Seasons)
            .OrderByDescending(x => x.Seasons.Count)
            .FirstOrDefaultAsync(x => x.InventoryItem != null && x.InventoryItem.Kind == InventoryItemKind.Hotel && x.Seasons.Count > 0);

        if (hotelCard is null)
        {
            return;
        }

        var templateService = scopedServices.GetRequiredService<IRateCardTemplateService>();
        await templateService.CreateFromRateCardAsync(hotelCard.Id, $"{hotelCard.Name} Template", "Seeded from the demo tenant's existing hotel contract.");
    }
}
