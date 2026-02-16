using saas.Data;

namespace saas.Modules.Auth.Entities;

/// <summary>
/// Tracks active user sessions for "Active Sessions" management in profile.
/// Stored in the tenant database.
/// </summary>
public class UserSession
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? DeviceInfo { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }

    public AppUser User { get; set; } = null!;
}
