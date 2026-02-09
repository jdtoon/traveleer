using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using saas.Data.Core;
using saas.Shared;

namespace saas.Infrastructure.Middleware;

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly HashSet<string> NonTenantPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "health", "pricing", "register", "super-admin", "login", "login-redirect", "login-modal", "about", "contact", "legal", "sitemap.xml", "robots.txt"
    };

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(3);

    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, CoreDbContext coreDb, IMemoryCache cache)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var firstSegment = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

        if (NonTenantPrefixes.Contains(firstSegment))
        {
            if (tenantContext is TenantContext tc)
            {
                tc.IsTenantRequest = false;
            }

            await _next(context);
            return;
        }

        // Cached tenant lookup — avoids DB query on every tenant request
        var cacheKey = $"tenant-resolution-{firstSegment}";
        var resolved = await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;

            var tenant = await coreDb.Tenants
                .Include(t => t.Plan)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Slug == firstSegment);

            if (tenant is null) return null;

            return new ResolvedTenant
            {
                Id = tenant.Id,
                Slug = tenant.Slug,
                Name = tenant.Name,
                Status = tenant.Status,
                PlanSlug = tenant.Plan.Slug
            };
        });

        if (resolved is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync("""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="utf-8" />
                    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                    <title>Page Not Found</title>
                    <style>
                        body { font-family: system-ui, -apple-system, sans-serif; display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; background: #f5f5f5; color: #333; }
                        .container { text-align: center; padding: 2rem; }
                        h1 { font-size: 4rem; margin: 0; color: #888; }
                        p { font-size: 1.125rem; margin: 1rem 0; }
                        a { color: #6366f1; text-decoration: none; }
                        a:hover { text-decoration: underline; }
                    </style>
                </head>
                <body>
                    <div class="container">
                        <h1>404</h1>
                        <p>The page you're looking for doesn't exist.</p>
                        <a href="/">← Back to home</a>
                    </div>
                </body>
                </html>
                """);
            return;
        }

        // Block suspended tenants — only allow access to login/logout pages
        if (resolved.Status == TenantStatus.Suspended)
        {
            var action = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1).FirstOrDefault() ?? string.Empty;

            if (!action.Equals("login", StringComparison.OrdinalIgnoreCase) &&
                !action.Equals("logout", StringComparison.OrdinalIgnoreCase) &&
                !action.Equals("verify", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync($$"""
                    <!DOCTYPE html>
                    <html lang="en">
                    <head>
                        <meta charset="utf-8" />
                        <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                        <title>Account Suspended</title>
                        <style>
                            body { font-family: system-ui, -apple-system, sans-serif; display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; background: #fef2f2; color: #333; }
                            .container { text-align: center; padding: 2rem; max-width: 480px; }
                            h1 { font-size: 2.5rem; margin: 0; color: #dc2626; }
                            p { font-size: 1.125rem; margin: 1rem 0; color: #666; }
                            a { color: #6366f1; text-decoration: none; }
                            a:hover { text-decoration: underline; }
                        </style>
                    </head>
                    <body>
                        <div class="container">
                            <h1>Account Suspended</h1>
                            <p>This workspace has been suspended. Please contact support for assistance.</p>
                            <a href="/">← Back to home</a>
                        </div>
                    </body>
                    </html>
                    """);
                return;
            }
        }

        if (tenantContext is TenantContext tenantCtx)
        {
            tenantCtx.IsTenantRequest = true;
            tenantCtx.Slug = resolved.Slug;
            tenantCtx.TenantId = resolved.Id;
            tenantCtx.TenantName = resolved.Name;
            tenantCtx.PlanSlug = resolved.PlanSlug;
        }

        await _next(context);
    }

    /// <summary>
    /// Invalidates the cached tenant resolution for a specific slug.
    /// Call this after suspend/activate operations.
    /// </summary>
    public static void InvalidateCache(IMemoryCache cache, string slug)
    {
        cache.Remove($"tenant-resolution-{slug}");
    }

    private sealed class ResolvedTenant
    {
        public Guid Id { get; init; }
        public string Slug { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public TenantStatus Status { get; init; }
        public string PlanSlug { get; init; } = string.Empty;
    }
}
