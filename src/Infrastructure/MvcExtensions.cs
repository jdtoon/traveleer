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

    public static IServiceCollection AddSwapHtmxConfig(
        this IServiceCollection services,
        IReadOnlyList<string> partialViewSearchPaths)
    {
        services.AddSwapHtmx(options =>
        {
            // Auto-suppress layout for HTMX requests - modules don't need their own _ViewStart.cshtml!
            options.AutoSuppressLayout = true;
            
            // Default navigation target for <swap-nav> tag helper
            options.DefaultNavigationTarget = "#main-content";
            
            // Module-driven partial view search paths
            foreach (var path in partialViewSearchPaths)
                options.PartialViewSearchPaths.Add(path);

            options.AddConfig<NotesEventConfig>();
        });

        return services;
    }
}
