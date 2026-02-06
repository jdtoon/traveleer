namespace saas.Shared;

/// <summary>
/// Current request's tenant info. Set by TenantResolutionMiddleware.
/// </summary>
public interface ITenantContext
{
    string? Slug { get; }
    Guid? TenantId { get; }
    string? PlanSlug { get; }
    string? TenantName { get; }
    bool IsTenantRequest { get; }
}
