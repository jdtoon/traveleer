using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Inventory.Entities;
using saas.Modules.Inventory.Services;
using saas.Shared;

namespace saas.Modules.Inventory;

public static class InventoryFeatures
{
    public const string Inventory = "inventory";
}

public static class InventoryPermissions
{
    public const string InventoryRead = "inventory.read";
    public const string InventoryCreate = "inventory.create";
    public const string InventoryEdit = "inventory.edit";
    public const string InventoryDelete = "inventory.delete";
}

public class InventoryModule : IModule
{
    public string Name => "Inventory";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["Inventory"] = "Inventory"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["Inventory"];

    public IReadOnlyList<ModuleFeature> Features =>
    [
        new(InventoryFeatures.Inventory, "Inventory", "Inventory catalogue for hotels, flights, excursions, transfers, visas, and other products", MinPlanSlug: "starter")
    ];

    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new(InventoryPermissions.InventoryRead, "View Inventory", "Inventory", 0),
        new(InventoryPermissions.InventoryCreate, "Create Inventory", "Inventory", 1),
        new(InventoryPermissions.InventoryEdit, "Edit Inventory", "Inventory", 2),
        new(InventoryPermissions.InventoryDelete, "Delete Inventory", "Inventory", 3)
    ];

    public IReadOnlyList<RolePermissionMapping> DefaultRolePermissions =>
    [
        new("Member", InventoryPermissions.InventoryRead)
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IInventoryService, InventoryService>();
    }

    public async Task SeedDemoDataAsync(IServiceProvider scopedServices)
    {
        var db = scopedServices.GetRequiredService<TenantDbContext>();
        if (await db.InventoryItems.AnyAsync())
        {
            return;
        }

        var makkah = await db.Destinations.AsNoTracking().FirstOrDefaultAsync(x => x.Name == "Makkah");
        var dubai = await db.Destinations.AsNoTracking().FirstOrDefaultAsync(x => x.Name == "Dubai");
        var cairo = await db.Destinations.AsNoTracking().FirstOrDefaultAsync(x => x.Name == "Cairo");
        var hotelSupplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.Name == "Al Haram Hotels");
        var transportSupplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.Name == "Hajj Tours Transport");
        var visaSupplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.Name == "Saudi Visa Services");

        db.InventoryItems.AddRange(
            new InventoryItem
            {
                Name = "Grand Haram Hotel",
                Kind = InventoryItemKind.Hotel,
                Description = "Five-star property close to the Haram with family and quad room options.",
                BaseCost = 18500m,
                Address = "Ibrahim Al Khalil Road, Makkah",
                Rating = 5,
                DestinationId = makkah?.Id,
                SupplierId = hotelSupplier?.Id,
                CreatedAt = DateTime.UtcNow
            },
            new InventoryItem
            {
                Name = "Dubai Desert Explorer",
                Kind = InventoryItemKind.Excursion,
                Description = "Shared red-dune safari with dinner, dune bashing, and cultural show.",
                BaseCost = 950m,
                DestinationId = dubai?.Id,
                SupplierId = transportSupplier?.Id,
                CreatedAt = DateTime.UtcNow
            },
            new InventoryItem
            {
                Name = "Saudi Tourist Visa Express",
                Kind = InventoryItemKind.Visa,
                Description = "Priority processing package for urgent outbound bookings.",
                BaseCost = 1450m,
                DestinationId = cairo?.Id,
                SupplierId = visaSupplier?.Id,
                CreatedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync();
    }
}
