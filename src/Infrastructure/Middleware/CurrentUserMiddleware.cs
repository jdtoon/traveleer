using System.Security.Claims;
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
    private readonly ILogger<CurrentUserMiddleware> _logger;

    public CurrentUserMiddleware(RequestDelegate next, ILogger<CurrentUserMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Session validation failed for tenant {Slug}", tenantContext.Slug);
                    }
                }

                try
                {
                    var tenantDb = context.RequestServices.GetRequiredService<TenantDbContext>();
                    await RefreshTenantClaimsIfNeededAsync(context, tenantDb, tenantContext.Slug);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Claim refresh failed for tenant {Slug}", tenantContext.Slug);
                }
            }

            current.SetFromClaims(context.User, isSuperAdmin);
        }

        await _next(context);
    }

    private async Task RefreshTenantClaimsIfNeededAsync(HttpContext context, TenantDbContext tenantDb, string? tenantSlug)
    {
        if (!(context.User.Identity?.IsAuthenticated ?? false))
        {
            return;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var userRoles = await tenantDb.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .Join(
                tenantDb.Roles,
                userRole => userRole.RoleId,
                role => role.Id,
                (userRole, role) => new { userRole.RoleId, role.Name })
            .ToListAsync();

        var roleNames = userRoles
            .Select(userRole => userRole.Name)
            .Where(roleName => !string.IsNullOrWhiteSpace(roleName))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var roleIds = userRoles
            .Select(userRole => userRole.RoleId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var permissionKeys = roleIds.Count == 0
            ? []
            : await tenantDb.RolePermissions
                .Where(rolePermission => roleIds.Contains(rolePermission.RoleId))
                .Select(rolePermission => rolePermission.Permission.Key)
                .Distinct()
                .ToListAsync();

        var effectivePermissionKeys = ExpandAdminPermissions(context, roleNames, permissionKeys);

        if (ClaimsMatch(context.User, roleNames, effectivePermissionKeys))
        {
            return;
        }

        var refreshedClaims = context.User.Claims
            .Where(claim => claim.Type != ClaimTypes.Role && claim.Type != AuthClaims.Permission)
            .Concat(roleNames.Select(roleName => new Claim(ClaimTypes.Role, roleName)))
            .Concat(effectivePermissionKeys.Select(permission => new Claim(AuthClaims.Permission, permission)))
            .ToList();

        var authType = context.User.Identity?.AuthenticationType ?? AuthSchemes.Tenant;
        var refreshedPrincipal = new ClaimsPrincipal(new ClaimsIdentity(refreshedClaims, authType));

        context.User = refreshedPrincipal;
        await context.SignInAsync(AuthSchemes.Tenant, refreshedPrincipal);

        _logger.LogInformation(
            "Refreshed tenant auth claims for user {UserId} in tenant {Slug}",
            userId,
            tenantSlug ?? "unknown");
    }

    private static bool ClaimsMatch(ClaimsPrincipal principal, IReadOnlyCollection<string> roleNames, IReadOnlyCollection<string> permissionKeys)
    {
        var claimedRoles = principal.FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var claimedPermissions = principal.FindAll(AuthClaims.Permission)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return claimedRoles.SetEquals(roleNames) && claimedPermissions.SetEquals(permissionKeys);
    }

    private static List<string> ExpandAdminPermissions(HttpContext context, IReadOnlyCollection<string> roleNames, IReadOnlyCollection<string> permissionKeys)
    {
        var effectivePermissionKeys = new HashSet<string>(permissionKeys, StringComparer.OrdinalIgnoreCase);
        if (!roleNames.Contains("Admin", StringComparer.OrdinalIgnoreCase))
        {
            return effectivePermissionKeys.ToList();
        }

        var modules = context.RequestServices.GetRequiredService<IReadOnlyList<IModule>>();
        foreach (var permission in modules.SelectMany(module => module.Permissions))
        {
            effectivePermissionKeys.Add(permission.Key);
        }

        return effectivePermissionKeys.ToList();
    }
}
