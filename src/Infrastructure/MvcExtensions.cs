using Swap.Htmx;
using saas.Modules.Notes.Events;

namespace saas.Infrastructure;

public static class MvcExtensions
{
    public static IServiceCollection AddMvcConfig(
        this IServiceCollection services,
        IReadOnlyDictionary<string, string> controllerViewPaths)
    {
        services.AddControllersWithViews(options =>
        {
            options.ModelBinderProviders.Insert(0, new InvariantDecimalModelBinderProvider());
        });

        services.Configure<Microsoft.AspNetCore.Mvc.Razor.RazorViewEngineOptions>(options =>
        {
            options.AddModuleViewLocations(controllerViewPaths);
        });

        return services;
    }

    public static IServiceCollection AddSwapHtmxConfig(this IServiceCollection services)
    {
        services.AddSwapHtmx(options =>
        {
            // Auto-suppress layout for HTMX requests - modules don't need their own _ViewStart.cshtml!
            options.AutoSuppressLayout = true;
            
            // Default navigation target for <swap-nav> tag helper
            options.DefaultNavigationTarget = "#main-content";
            
            options.PartialViewSearchPaths.Add("Notes");
            options.PartialViewSearchPaths.Add("Marketing");
            options.PartialViewSearchPaths.Add("SuperAdmin");
            options.PartialViewSearchPaths.Add("TenantAdmin");
            options.PartialViewSearchPaths.Add("TenantBilling");
            options.AddConfig<NotesEventConfig>();
        });

        return services;
    }
}
