using saas.Data;

namespace saas.Modules.Audit.DTOs;

public class AuditDashboardItemDto
{
    public long Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool HasChanges { get; set; }

    public string ActionBadgeClass => Action switch
    {
        "Created" => "badge-success",
        "Updated" => "badge-warning",
        "Deleted" => "badge-error",
        _ => "badge-ghost"
    };
}

public class AuditDashboardViewModel
{
    public PaginatedList<AuditDashboardItemDto> Entries { get; set; } = null!;
    public string? FilterEntity { get; set; }
    public string? FilterAction { get; set; }
    public string? FilterUser { get; set; }
    public string? FilterFrom { get; set; }
    public string? FilterTo { get; set; }
    public List<string> DistinctEntityTypes { get; set; } = [];
    public List<string> DistinctActions { get; set; } = [];
    public List<string> DistinctUsers { get; set; } = [];
}

public class AuditDetailDto
{
    public long Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? IpAddress { get; set; }
    public List<string> AffectedColumns { get; set; } = [];
    public List<AuditFieldChange> Changes { get; set; } = [];

    public string ActionBadgeClass => Action switch
    {
        "Created" => "badge-success",
        "Updated" => "badge-warning",
        "Deleted" => "badge-error",
        _ => "badge-ghost"
    };
}

public class AuditFieldChange
{
    public string Field { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public bool IsChanged { get; set; }
}
