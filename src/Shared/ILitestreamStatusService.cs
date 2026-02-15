namespace saas.Shared;

public interface ILitestreamStatusService
{
    Task<LitestreamStatusModel> GetStatusAsync(CancellationToken ct = default);
}

public class LitestreamStatusModel
{
    public bool AutoRestoreEnabled { get; set; }
    public bool LitestreamConfigured { get; set; }
    public bool LitestreamBinaryAvailable { get; set; }
    public bool LitestreamConfigExists { get; set; }
    public bool CoreDatabaseExists { get; set; }
    public bool AuditDatabaseExists { get; set; }
    public int TenantDatabaseCount { get; set; }
    public bool KeyBackupEnabled { get; set; }
    public string KeyBackupPath { get; set; } = string.Empty;
    public DateTime? LastKeyBackupUtc { get; set; }
    public DateTime? LitestreamConfigUpdatedUtc { get; set; }
    public DateTime? LitestreamReloadSignalUtc { get; set; }
}
