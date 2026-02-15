using System.Reflection;
using Swap.Htmx;

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

            // Auto-discover and register all ISwapEventConfiguration implementations
            var configTypes = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false }
                         && typeof(ISwapEventConfiguration).IsAssignableFrom(t));

            foreach (var configType in configTypes)
            {
                if (Activator.CreateInstance(configType) is ISwapEventConfiguration config)
                    config.Configure(options.EventBus);
            }
        });

        return services;
    }
}
