using saas.Shared;

namespace saas.Infrastructure.Middleware;

public class TenantContext : ITenantContext
{
    public string? Slug { get; internal set; }
    public Guid? TenantId { get; internal set; }
    public string? PlanSlug { get; internal set; }
    public string? TenantName { get; internal set; }
    public bool IsTenantRequest { get; internal set; }
}
