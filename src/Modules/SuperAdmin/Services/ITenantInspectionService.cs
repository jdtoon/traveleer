namespace saas.Modules.SuperAdmin.Services;

public interface ITenantInspectionService
{
    Task<TenantDatabaseInfoModel?> GetDatabaseInfoAsync(string slug);
    Task<List<TenantUserInfo>> GetUsersAsync(string slug);
    Task<List<TenantSessionInfo>> GetActiveSessionsAsync(string slug);
    Task<List<TenantRoleInfo>> GetRolesAsync(string slug);
    Task<TenantDataCountsModel> GetDataCountsAsync(string slug);
    Task<List<TenantInvitationInfo>> GetPendingInvitationsAsync(string slug);
    Task<QueryResult> ExecuteReadOnlyQueryAsync(string slug, string sql);
}

// ── Models ───────────────────────────────────────────────────────────────────

public class TenantDatabaseInfoModel
{
    public string Slug { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public long WalSizeBytes { get; set; }
    public DateTime? LastModifiedUtc { get; set; }
    public List<TableInfo> Tables { get; set; } = [];
    public string SizeFormatted => FormatBytes(SizeBytes);
    public string WalSizeFormatted => FormatBytes(WalSizeBytes);
    public string TotalSizeFormatted => FormatBytes(SizeBytes + WalSizeBytes);

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1) { order++; size /= 1024; }
        return $"{size:0.##} {sizes[order]}";
    }
}

public class TableInfo
{
    public string Name { get; set; } = string.Empty;
    public long RowCount { get; set; }
}

public class TenantUserInfo
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public List<string> Roles { get; set; } = [];
}

public class TenantSessionInfo
{
    public string SessionId { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
}

public class TenantRoleInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsSystemRole { get; set; }
    public int UserCount { get; set; }
    public List<string> Permissions { get; set; } = [];
}

public class TenantDataCountsModel
{
    public string Slug { get; set; } = string.Empty;
    public int Users { get; set; }
    public int Roles { get; set; }
    public int NotificationsTotal { get; set; }
    public int NotificationsUnread { get; set; }
    public int ActiveSessions { get; set; }
    public int PendingInvitations { get; set; }
}

public class TenantInvitationInfo
{
    public string Email { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? InvitedByEmail { get; set; }
    public DateTime SentAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
}

public class QueryResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<string> Columns { get; set; } = [];
    public List<List<object?>> Rows { get; set; } = [];
    public int RowCount { get; set; }
    public bool Truncated { get; set; }
    public double ElapsedMs { get; set; }
}
