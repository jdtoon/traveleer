using Microsoft.AspNetCore.Mvc;
using saas.Data.Core;
using saas.Data.Tenant;

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
    /// Feature definitions owned by this module. Collected at startup for core DB seeding.
    /// </summary>
    IReadOnlyList<Feature> Features => [];

    /// <summary>
    /// Permission definitions owned by this module. Collected at startup for tenant DB seeding.
    /// </summary>
    IReadOnlyList<Permission> Permissions => [];

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
