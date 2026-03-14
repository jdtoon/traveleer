using saas.Modules.Suppliers.Services;
using saas.Shared;

namespace saas.Modules.Suppliers;

public static class SupplierFeatures
{
    public const string Suppliers = "suppliers";
}

public static class SupplierPermissions
{
    public const string SuppliersRead = "suppliers.read";
    public const string SuppliersCreate = "suppliers.create";
    public const string SuppliersEdit = "suppliers.edit";
    public const string SuppliersDelete = "suppliers.delete";
}

public class SuppliersModule : IModule
{
    public string Name => "Suppliers";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["Supplier"] = "Suppliers"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["Supplier"];

    public IReadOnlyList<ModuleFeature> Features =>
    [
        new(SupplierFeatures.Suppliers, "Supplier Management", "Full supplier profiles with contacts, contracts, and performance tracking", MinPlanSlug: "starter")
    ];

    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new(SupplierPermissions.SuppliersRead, "View Suppliers", "Suppliers", 0),
        new(SupplierPermissions.SuppliersCreate, "Create Suppliers", "Suppliers", 1),
        new(SupplierPermissions.SuppliersEdit, "Edit Suppliers", "Suppliers", 2),
        new(SupplierPermissions.SuppliersDelete, "Delete Suppliers", "Suppliers", 3)
    ];

    public IReadOnlyList<RolePermissionMapping> DefaultRolePermissions =>
    [
        new("Member", SupplierPermissions.SuppliersRead)
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISupplierService, SupplierService>();
    }
}
