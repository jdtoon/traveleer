using Microsoft.AspNetCore.Authentication;
using saas.Modules.Auth;
using saas.Modules.Auth.Services;
using saas.Shared;

namespace saas.Infrastructure.Middleware;

/// <summary>
/// Populates <see cref="ICurrentUser"/> from the authenticated principal's claims.
/// Blocks cross-tenant access: if the user's tenant-slug claim doesn't match the
/// URL slug, the tenant cookie is signed out and the request is redirected to the
/// correct tenant login page.
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
                // CRITICAL: block cross-tenant access.
                // If the cookie carries a slug for a DIFFERENT tenant, sign out
                // and redirect to the target tenant's login page.
                if (!string.IsNullOrEmpty(tenantSlug) &&
                    !string.Equals(tenantSlug, tenantContext.Slug, StringComparison.OrdinalIgnoreCase))
                {
                    current.Clear();
                    await context.SignOutAsync(AuthSchemes.Tenant);
                    context.Response.Redirect($"/{tenantContext.Slug}/login");
                    return; // short-circuit — do NOT call _next
                }
            }

            current.SetFromClaims(context.User, isSuperAdmin);
        }

        await _next(context);
    }
}
