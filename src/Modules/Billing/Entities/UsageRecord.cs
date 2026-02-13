using saas.Data;

namespace saas.Modules.Billing.Entities;

public class UsageRecord : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Metric { get; set; } = string.Empty;  // e.g., "api_calls", "storage_bytes", "users"
    public long Quantity { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
