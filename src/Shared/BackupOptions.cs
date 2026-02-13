namespace saas.Shared;

public class BackupOptions
{
    public const string SectionName = "Backup";

    public string LitestreamConfigPath { get; set; } = "/app/db/litestream.yml";
    public string SentinelPath { get; set; } = "/app/db/.litestream-reload";
    public string R2Bucket { get; set; } = "saas-backups";
    public string R2Endpoint { get; set; } = string.Empty;
    public string SyncInterval { get; set; } = "30s";
    public string MonitorInterval { get; set; } = "5s";
    public string CheckpointInterval { get; set; } = "5m";
    public string SnapshotInterval { get; set; } = "24h";
    public string SnapshotRetention { get; set; } = "168h";
    public bool AutoRestoreEnabled { get; set; } = true;
    public bool KeyBackupEnabled { get; set; } = true;
    public string KeyBackupInterval { get; set; } = "1h";
    public string KeyBackupPath { get; set; } = "system/keys/dataprotection-keys.zip";
    public string KeyBackupMarkerPath { get; set; } = "/app/db/.keys-last-backup";
}
