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
            await context.Response.WriteAsync("Tenant not found.");
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
