using Microsoft.AspNetCore.Http;
using saas.Shared;

namespace saas.Infrastructure.Middleware;

public class FeatureFlagMiddleware
{
    private readonly RequestDelegate _next;

    public FeatureFlagMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IFeatureService featureService, ITenantContext tenantContext)
    {
        if (tenantContext.IsTenantRequest && tenantContext.TenantId.HasValue)
        {
            var enabledFeatures = await featureService.GetEnabledFeaturesAsync();
            context.Items["TenantFeatureFlags"] = enabledFeatures;
        }

        await _next(context);
    }
}
