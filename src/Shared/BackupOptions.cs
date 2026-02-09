namespace saas.Shared;

public class BackupOptions
{
    public const string SectionName = "Backup";

    public string LitestreamConfigPath { get; set; } = "/app/db/litestream.yml";
    public string SentinelPath { get; set; } = "/app/db/.litestream-reload";
    public string R2Bucket { get; set; } = "saas-backups";
    public string R2Endpoint { get; set; } = string.Empty;
}
