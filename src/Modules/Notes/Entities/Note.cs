using saas.Data;

namespace saas.Modules.Notes.Entities;

public class Note : IAuditableEntity
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public string? Content { get; set; }
    public string Color { get; set; } = "gray";
    public bool IsPinned { get; set; }

    // Audit fields
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
