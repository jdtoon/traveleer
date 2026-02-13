using saas.Data;

namespace saas.Modules.TenantAdmin.Entities;

public class TeamInvitation : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? RoleId { get; set; }
    public string? RoleName { get; set; }
    public string Token { get; set; } = string.Empty;
    public string InvitedByUserId { get; set; } = string.Empty;
    public string? InvitedByEmail { get; set; }
    public InvitationStatus Status { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public enum InvitationStatus
{
    Pending,
    Accepted,
    Expired,
    Revoked
}
