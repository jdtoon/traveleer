namespace saas.Modules.SuperAdmin.Entities;

public class Announcement
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public AnnouncementType Type { get; set; } = AnnouncementType.Info;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public string? CreatedByEmail { get; set; }
}

public enum AnnouncementType
{
    Info,
    Warning,
    Critical
}
