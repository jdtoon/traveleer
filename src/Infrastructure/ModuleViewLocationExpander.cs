using Microsoft.AspNetCore.Mvc.Razor;

namespace saas.Infrastructure;

/// <summary>
/// Teaches the Razor view engine where to find views for module-based controllers.
/// The controller→module mapping is populated at startup from IModule.ControllerViewPaths.
/// </summary>
public class ModuleViewLocationExpander : IViewLocationExpander
{
    private readonly Dictionary<string, string> _controllerModuleMap;

    public ModuleViewLocationExpander(IReadOnlyDictionary<string, string> controllerModuleMap)
    {
        _controllerModuleMap = new Dictionary<string, string>(controllerModuleMap, StringComparer.OrdinalIgnoreCase);
    }

    public void PopulateValues(ViewLocationExpanderContext context) { }

    public IEnumerable<string> ExpandViewLocations(
        ViewLocationExpanderContext context,
        IEnumerable<string> viewLocations)
    {
        if (context.ControllerName != null &&
            _controllerModuleMap.TryGetValue(context.ControllerName, out var moduleName))
        {
            var moduleLocations = new[]
            {
                $"/Modules/{moduleName}/Views/{{0}}.cshtml",
                $"/Modules/{moduleName}/Views/Shared/{{0}}.cshtml"
            };

            return moduleLocations.Concat(viewLocations);
        }

        return viewLocations;
    }
}

public static class ModuleViewLocationExtensions
{
    public static void AddModuleViewLocations(
        this RazorViewEngineOptions options,
        IReadOnlyDictionary<string, string> controllerModuleMap)
    {
        options.ViewLocationExpanders.Add(new ModuleViewLocationExpander(controllerModuleMap));
    }
}
