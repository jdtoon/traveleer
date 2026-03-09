using saas.Data;

namespace saas.Modules.Settings.Entities;

public class RateCategory : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public InventoryType ForType { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? Capacity { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
