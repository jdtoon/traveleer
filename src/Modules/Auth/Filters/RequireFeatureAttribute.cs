using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using saas.Shared;

namespace saas.Modules.Auth.Filters;

/// <summary>
/// MVC filter that gates controller/action access on an <see cref="IFeatureService"/> check.
/// Unlike Microsoft.FeatureManagement's [FeatureGate], this uses our IFeatureService
/// which respects the AllEnabledLocally dev override.
/// Returns 404 when the feature is disabled (hides the feature entirely).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequireFeatureAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _featureKey;

    public RequireFeatureAttribute(string featureKey)
    {
        _featureKey = featureKey;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var featureService = context.HttpContext.RequestServices.GetRequiredService<IFeatureService>();

        if (!await featureService.IsEnabledAsync(_featureKey))
        {
            context.Result = new NotFoundResult();
            return;
        }

        await next();
    }
}
