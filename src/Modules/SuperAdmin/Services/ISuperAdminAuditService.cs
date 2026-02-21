namespace saas.Modules.SuperAdmin.Services;

/// <summary>
/// Writes audit entries for super admin actions (suspend, plan change, feature toggle, etc.)
/// to the shared AuditDbContext with Source = "SuperAdmin".
/// </summary>
public interface ISuperAdminAuditService
{
    Task LogAsync(SuperAdminAuditEntry entry);
    Task LogAsync(string action, string entityType, string entityId, string? details = null);
}

public class SuperAdminAuditEntry
{
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? Details { get; set; }
}
