using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Auth;
using saas.Modules.Auth.Entities;
using saas.Modules.Auth.Services;
using saas.Shared;

namespace saas.Infrastructure.Middleware;

/// <summary>
/// Populates <see cref="ICurrentUser"/> from the authenticated principal's claims.
/// Blocks cross-tenant access: if the user's tenant-slug claim doesn't match the
/// URL slug, the tenant cookie is signed out and the request is redirected to the
/// correct tenant login page.
/// Also enforces session revocation: if the session has been revoked or expired,
/// the user is signed out.
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
                if (!string.IsNullOrEmpty(tenantSlug) &&
                    !string.Equals(tenantSlug, tenantContext.Slug, StringComparison.OrdinalIgnoreCase))
                {
                    current.Clear();
                    await context.SignOutAsync(AuthSchemes.Tenant);
                    context.Response.Redirect($"/{tenantContext.Slug}/login");
                    return;
                }

                // Enforce session revocation / expiry
                var sessionClaim = context.User.FindFirst(AuthClaims.SessionId)?.Value;
                if (!string.IsNullOrEmpty(sessionClaim) && Guid.TryParse(sessionClaim, out var sessionId))
                {
                    try
                    {
                        var tenantDb = context.RequestServices.GetRequiredService<TenantDbContext>();
                        var session = await tenantDb.Set<UserSession>()
                            .AsNoTracking()
                            .FirstOrDefaultAsync(s => s.Id == sessionId);

                        if (session is null || session.IsRevoked || (session.ExpiresAt.HasValue && session.ExpiresAt < DateTime.UtcNow))
                        {
                            current.Clear();
                            await context.SignOutAsync(AuthSchemes.Tenant);
                            context.Response.Redirect($"/{tenantContext.Slug}/login");
                            return;
                        }

                        // Update last activity (fire-and-forget, non-blocking)
                        session = await tenantDb.Set<UserSession>().FindAsync(sessionId);
                        if (session is not null)
                        {
                            session.LastActivityAt = DateTime.UtcNow;
                            await tenantDb.SaveChangesAsync();
                        }
                    }
                    catch { /* Don't break auth if session check fails */ }
                }
            }

            current.SetFromClaims(context.User, isSuperAdmin);
        }

        await _next(context);
    }
}
