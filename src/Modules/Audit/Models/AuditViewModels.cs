using saas.Data;

namespace saas.Modules.Audit.Models;

public class AuditLogItem
{
    public long Id { get; set; }
    public string Source { get; set; } = "Tenant";
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string TenantSlug { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool HasChanges { get; set; }
}

public class AuditLogViewModel
{
    public PaginatedList<AuditLogItem> Entries { get; set; } = null!;
    public string? FilterEntity { get; set; }
    public string? FilterAction { get; set; }
    public string? FilterSlug { get; set; }
    public string? FilterSource { get; set; }
}
