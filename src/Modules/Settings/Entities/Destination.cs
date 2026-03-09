using saas.Data;

namespace saas.Modules.Settings.Entities;

public class Destination : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
    public string? CountryName { get; set; }
    public string? Region { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
