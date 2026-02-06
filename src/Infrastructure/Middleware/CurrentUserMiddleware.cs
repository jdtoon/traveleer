using saas.Modules.Auth;
using saas.Modules.Auth.Services;
using saas.Shared;

namespace saas.Infrastructure.Middleware;

/// <summary>
/// Placeholder for Phase 3. Will populate ICurrentUser from auth claims.
/// </summary>
public class CurrentUserMiddleware
{
    private readonly RequestDelegate _next;

    public CurrentUserMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, ITenantContext tenantContext)
    {
        if (currentUser is CurrentUser current)
        {
            var isSuperAdmin = context.User.HasClaim(AuthClaims.IsSuperAdmin, "true");
            var tenantSlug = context.User.FindFirst(AuthClaims.TenantSlug)?.Value;

            if (tenantContext.IsTenantRequest && !string.IsNullOrEmpty(tenantContext.Slug))
            {
                if (!string.Equals(tenantSlug, tenantContext.Slug, StringComparison.OrdinalIgnoreCase))
                {
                    current.Clear();
                    await _next(context);
                    return;
                }
            }

            current.SetFromClaims(context.User, isSuperAdmin);
        }

        await _next(context);
    }
}
