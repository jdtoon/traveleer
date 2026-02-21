namespace saas.Modules.Audit.Entities;

public class AuditEntry
{
    public long Id { get; set; }
    /// <summary>
    /// "Tenant" for EF interceptor-generated entries, "SuperAdmin" for admin action audit entries.
    /// </summary>
    public string Source { get; set; } = "Tenant";
    public string? TenantSlug { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? UserEmail { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? AffectedColumns { get; set; }
    public DateTime Timestamp { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
