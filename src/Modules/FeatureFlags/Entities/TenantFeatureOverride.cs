using saas.Data;
using saas.Modules.Tenancy.Entities;

namespace saas.Modules.FeatureFlags.Entities;

public class TenantFeatureOverride : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid FeatureId { get; set; }
    public Feature Feature { get; set; } = null!;
    public bool IsEnabled { get; set; }
    public string? Reason { get; set; }
    public DateTime? ExpiresAt { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
