using saas.Shared;

namespace saas.Modules.Tenancy;

/// <summary>
/// Core framework module that owns the Tenant entity, tenant resolution middleware,
/// tenant provisioning, and slug reservation. Other modules contribute their
/// reserved slugs and public route prefixes via IModule properties.
/// </summary>
public class TenancyModule : IModule
{
    public string Name => "Tenancy";

    /// <summary>
    /// Framework-level reserved slugs. Combined with module-contributed slugs at startup.
    /// </summary>
    public IReadOnlyList<string> ReservedSlugs =>
    [
        "www", "app", "cdn", "docs", "help", "support", "blog", "status"
    ];

    /// <summary>
    /// Framework-level non-tenant route prefixes.
    /// </summary>
    public IReadOnlyList<string> PublicRoutePrefixes =>
    [
        "", "health", "api", "static", "assets", "favicon.ico"
    ];

    /// <summary>
    /// The Admin role is created for every tenant. Gets all permissions automatically.
    /// </summary>
    public IReadOnlyList<RoleDefinition> DefaultRoles =>
    [
        new("Admin", "Full access to all features", IsSystemRole: true),
        new("Member", "Default member role", IsSystemRole: true)
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Provisioner is registered by the Registration module which depends on it.
        // TenantContext and middleware are registered by infrastructure (ServiceCollectionExtensions).
        // This module primarily exists to define the Tenant entity, roles, and route contributions.
    }
}
