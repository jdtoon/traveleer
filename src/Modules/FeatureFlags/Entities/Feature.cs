using saas.Data;

namespace saas.Modules.FeatureFlags.Entities;

public class Feature : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Module { get; set; }
    public bool IsGlobal { get; set; }
    public bool IsEnabled { get; set; } = true;

    public ICollection<PlanFeature> PlanFeatures { get; set; } = [];

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
