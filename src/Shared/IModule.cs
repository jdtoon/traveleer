using Microsoft.AspNetCore.Mvc;

namespace saas.Shared;

/// <summary>
/// Contract for self-contained vertical-slice modules.
/// Every module implements this and is registered in Program.cs.
/// </summary>
public interface IModule
{
    /// <summary>
    /// Human-readable module name for logging and diagnostics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Maps controller names to module folder names for the Razor view locator.
    /// Key = controller name (e.g. "Notes"), Value = module folder (e.g. "Notes").
    /// Collected at startup and fed to ModuleViewLocationExpander.
    /// </summary>
    IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>();

    /// <summary>
    /// Swap.Htmx partial view search paths contributed by this module.
    /// Collected at startup and registered with AddSwapHtmx options.
    /// </summary>
    IReadOnlyList<string> PartialViewSearchPaths => [];

    /// <summary>
    /// Feature definitions owned by this module. Collected at startup for core DB seeding.
    /// Uses lightweight records — mapped to Feature entities by the seeder.
    /// </summary>
    IReadOnlyList<ModuleFeature> Features => [];

    /// <summary>
    /// Permission definitions owned by this module. Collected at startup for tenant DB provisioning.
    /// Uses lightweight records — mapped to Permission entities by the provisioner.
    /// </summary>
    IReadOnlyList<ModulePermission> Permissions => [];

    /// <summary>
    /// Default roles this module contributes for tenant provisioning.
    /// Deduplicated by name across modules. Admin role always gets all permissions.
    /// </summary>
    IReadOnlyList<RoleDefinition> DefaultRoles => [];

    /// <summary>
    /// Maps this module's permissions to non-admin roles during tenant provisioning.
    /// Admin role is automatic (gets everything). These mappings target other roles like "Member".
    /// </summary>
    IReadOnlyList<RolePermissionMapping> DefaultRolePermissions => [];

    /// <summary>
    /// URL path prefixes that this module handles as public (non-tenant) routes.
    /// Collected at startup and used by TenantResolutionMiddleware to bypass tenant resolution.
    /// Example: Marketing contributes ["pricing", "about", "contact"].
    /// </summary>
    IReadOnlyList<string> PublicRoutePrefixes => [];

    /// <summary>
    /// Slug values this module reserves from tenant registration.
    /// Merged with configured reserved slugs and used by the provisioner for validation.
    /// </summary>
    IReadOnlyList<string> ReservedSlugs => [];

    /// <summary>
    /// Called after tenant roles, permissions, and admin user are seeded during provisioning.
    /// Use this to seed module-specific tenant data (e.g. default settings, templates).
    /// Receives the scoped IServiceProvider with TenantDbContext already wired to the new tenant DB.
    /// </summary>
    Task SeedTenantAsync(IServiceProvider scopedServices) => Task.CompletedTask;

    /// <summary>
    /// Called during dev seeding only (when DevSeed:Enabled is true).
    /// Use this to create demo/sample data for local development.
    /// </summary>
    Task SeedDemoDataAsync(IServiceProvider scopedServices) => Task.CompletedTask;

    /// <summary>
    /// Register services (DI), entity configurations, and options for this module.
    /// Called during ConfigureServices.
    /// </summary>
    void RegisterServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// Register middleware specific to this module (if any).
    /// Called during Configure, after global middleware is registered.
    /// </summary>
    void RegisterMiddleware(IApplicationBuilder app) { }

    /// <summary>
    /// Register MVC-related configuration: partial view paths, view location mappings.
    /// Called during AddControllersWithViews configuration.
    /// </summary>
    void RegisterMvc(MvcOptions mvcOptions, IMvcBuilder mvcBuilder) { }
}
