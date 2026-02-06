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
