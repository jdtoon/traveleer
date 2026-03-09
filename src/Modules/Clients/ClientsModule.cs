using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Clients.Entities;
using saas.Modules.Clients.Services;
using saas.Shared;

namespace saas.Modules.Clients;

public static class ClientFeatures
{
    public const string Clients = "clients";
}

public static class ClientPermissions
{
    public const string ClientsRead = "clients.read";
    public const string ClientsCreate = "clients.create";
    public const string ClientsEdit = "clients.edit";
    public const string ClientsDelete = "clients.delete";
}

public class ClientsModule : IModule
{
    public string Name => "Clients";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["Client"] = "Clients"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["Client"];

    public IReadOnlyList<ModuleFeature> Features =>
    [
        new(ClientFeatures.Clients, "Clients", "Client records and CRM basics for a tenant", MinPlanSlug: "starter")
    ];

    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new(ClientPermissions.ClientsRead, "View Clients", "Clients", 0),
        new(ClientPermissions.ClientsCreate, "Create Clients", "Clients", 1),
        new(ClientPermissions.ClientsEdit, "Edit Clients", "Clients", 2),
        new(ClientPermissions.ClientsDelete, "Delete Clients", "Clients", 3)
    ];

    public IReadOnlyList<RolePermissionMapping> DefaultRolePermissions =>
    [
        new("Member", ClientPermissions.ClientsRead)
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IClientService, ClientService>();
    }

    public async Task SeedDemoDataAsync(IServiceProvider scopedServices)
    {
        var db = scopedServices.GetRequiredService<TenantDbContext>();

        if (await db.Clients.AnyAsync())
        {
            return;
        }

        db.Clients.AddRange(
            new Client
            {
                Id = Guid.NewGuid(),
                Name = "Acacia Travel Group",
                Company = "Acacia Travel Group",
                Email = "hello@acaciatravel.test",
                Phone = "+27 11 555 0101",
                Country = "South Africa",
                Notes = "VIP agency account with frequent safari requests.",
                CreatedAt = DateTime.UtcNow
            },
            new Client
            {
                Id = Guid.NewGuid(),
                Name = "Lena Hoffmann",
                Company = "Hoffmann Private Travel",
                Email = "lena@hoffmann-travel.test",
                Phone = "+49 30 555 0199",
                Country = "Germany",
                Notes = "Prefers premium lodge options and detailed room notes.",
                CreatedAt = DateTime.UtcNow
            },
            new Client
            {
                Id = Guid.NewGuid(),
                Name = "Karabo Maseko",
                Company = null,
                Email = "karabo@example.test",
                Phone = "+27 82 555 0147",
                Country = "South Africa",
                Notes = "Repeat FIT traveler interested in family itineraries.",
                CreatedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync();
    }
}
