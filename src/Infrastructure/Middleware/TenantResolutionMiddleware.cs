using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Shared;

namespace saas.Infrastructure.Middleware;

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly HashSet<string> NonTenantPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "health", "pricing", "register", "super-admin", "login", "about", "contact", "legal", "sitemap.xml", "robots.txt"
    };

    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, CoreDbContext coreDb)
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

        var tenant = await coreDb.Tenants
            .Include(t => t.Plan)
            .FirstOrDefaultAsync(t => t.Slug == firstSegment);
        if (tenant is null)
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

        if (tenantContext is TenantContext tenantCtx)
        {
            tenantCtx.IsTenantRequest = true;
            tenantCtx.Slug = tenant.Slug;
            tenantCtx.TenantId = tenant.Id;
            tenantCtx.TenantName = tenant.Name;
            tenantCtx.PlanSlug = tenant.Plan.Slug;
        }

        await _next(context);
    }
}
