using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using saas.Data.Core;
using saas.Modules.Tenancy.Entities;
using saas.Shared;

namespace saas.Infrastructure.Middleware;

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _nonTenantPrefixes;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(3);

    public TenantResolutionMiddleware(RequestDelegate next, HashSet<string> publicRoutePrefixes)
    {
        _next = next;
        _nonTenantPrefixes = publicRoutePrefixes;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, CoreDbContext coreDb, IMemoryCache cache)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var firstSegment = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

        if (_nonTenantPrefixes.Contains(firstSegment))
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
                .FirstOrDefaultAsync(t => t.Slug == firstSegment && !t.IsDeleted);

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
            await ErrorPages.Write404Async(context.Response);
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
                await ErrorPages.Write403SuspendedAsync(context.Response);
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

    private static void SetTenantContext(ITenantContext tenantContext, ResolvedTenant resolved)
    {
        if (tenantContext is TenantContext tenantCtx)
        {
            tenantCtx.IsTenantRequest = true;
            tenantCtx.Slug = resolved.Slug;
            tenantCtx.TenantId = resolved.Id;
            tenantCtx.TenantName = resolved.Name;
            tenantCtx.PlanSlug = resolved.PlanSlug;
        }
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
