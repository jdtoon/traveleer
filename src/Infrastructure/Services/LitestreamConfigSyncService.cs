using System.Text;
using Microsoft.Extensions.Options;
using saas.Shared;

namespace saas.Infrastructure.Services;

public class LitestreamConfigSyncService : BackgroundService, ILitestreamConfigSync
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LitestreamConfigSyncService> _logger;
    private readonly LitestreamOptions _options;
    private readonly string _coreDbPath;
    private readonly string _auditDbPath;
    private readonly string _tenantDbPath;
    private readonly string _hangfireDbPath;
    private readonly TimeSpan _syncInterval = TimeSpan.FromMinutes(5);

    public LitestreamConfigSyncService(
        IConfiguration configuration,
        IOptions<LitestreamOptions> options,
        ILogger<LitestreamConfigSyncService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _options = options.Value;

        // Extract file paths from connection strings (strip "Data Source=" prefix)
        var coreCs = configuration.GetConnectionString("CoreDatabase") ?? "Data Source=/app/db/core.db";
        var auditCs = configuration.GetConnectionString("AuditDatabase") ?? "Data Source=/app/db/audit.db";
        _coreDbPath = coreCs.Replace("Data Source=", "", StringComparison.OrdinalIgnoreCase).Trim();
        _auditDbPath = auditCs.Replace("Data Source=", "", StringComparison.OrdinalIgnoreCase).Trim();
        _tenantDbPath = configuration["Tenancy:DatabasePath"] ?? "/app/db/tenants";
        _hangfireDbPath = configuration["Hangfire:SQLitePath"] ?? "/app/db/hangfire.db";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once at startup, then on interval
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncConfigAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync Litestream config");
            }

            await Task.Delay(_syncInterval, stoppingToken);
        }
    }

    public async Task SyncConfigAsync()
    {
        var bucket = _options.R2Bucket;
        var endpoint = _options.R2Endpoint;

        // Discover all tenant .db files
        var tenantDbs = Directory.Exists(_tenantDbPath)
            ? Directory.GetFiles(_tenantDbPath, "*.db")
            : Array.Empty<string>();

        // Include hangfire.db if Hangfire is using SQLite storage
        var hangfireStorage = _configuration.GetValue("Hangfire:Storage", "InMemory");
        var hangfireDbPath = string.Equals(hangfireStorage, "SQLite", StringComparison.OrdinalIgnoreCase)
            ? _hangfireDbPath
            : null;

        var yaml = GenerateYaml(
            _coreDbPath,
            _auditDbPath,
            tenantDbs,
            bucket,
            endpoint,
            _options.SyncInterval,
            _options.MonitorInterval,
            _options.CheckpointInterval,
            _options.SnapshotInterval,
            _options.SnapshotRetention,
            hangfireDbPath);

        // Only write if changed
        var existingConfig = File.Exists(_options.LitestreamConfigPath)
            ? await File.ReadAllTextAsync(_options.LitestreamConfigPath)
            : string.Empty;

        if (yaml != existingConfig)
        {
            // Ensure directory exists
            var configDir = Path.GetDirectoryName(_options.LitestreamConfigPath);
            if (!string.IsNullOrEmpty(configDir))
                Directory.CreateDirectory(configDir);

            await File.WriteAllTextAsync(_options.LitestreamConfigPath, yaml);

            _logger.LogInformation(
                "Litestream config updated with {Count} tenant databases",
                tenantDbs.Length);

            // Write sentinel file to signal the litestream wrapper to reload
            await File.WriteAllTextAsync(
                _options.SentinelPath,
                DateTime.UtcNow.ToString("O"));
        }
    }

    public static string GenerateYaml(
        string coreDbPath,
        string auditDbPath,
        string[] tenantDbs,
        string bucket,
        string endpoint,
        string syncInterval = "30s",
        string monitorInterval = "5s",
        string checkpointInterval = "5m",
        string snapshotInterval = "24h",
        string snapshotRetention = "168h",
        string? hangfireDbPath = null)
    {
        var yaml = new StringBuilder();
        yaml.AppendLine($"sync-interval: {syncInterval}");
        yaml.AppendLine("snapshot:");
        yaml.AppendLine($"  interval: {snapshotInterval}");
        yaml.AppendLine($"  retention: {snapshotRetention}");
        yaml.AppendLine("dbs:");

        // Core database
        AppendDbEntry(yaml, coreDbPath, "core.db", bucket, endpoint, monitorInterval, checkpointInterval);

        // Audit database
        AppendDbEntry(yaml, auditDbPath, "audit.db", bucket, endpoint, monitorInterval, checkpointInterval);

        // Hangfire database (when using SQLite storage)
        if (!string.IsNullOrEmpty(hangfireDbPath))
        {
            AppendDbEntry(yaml, hangfireDbPath, "hangfire.db", bucket, endpoint, monitorInterval, checkpointInterval);
        }

        // Tenant databases
        foreach (var dbFile in tenantDbs.OrderBy(f => f))
        {
            var fileName = Path.GetFileName(dbFile);
            var r2Path = $"tenants/{fileName}";
            AppendDbEntry(yaml, dbFile, r2Path, bucket, endpoint, monitorInterval, checkpointInterval);
        }

        return yaml.ToString();
    }

    private static void AppendDbEntry(
        StringBuilder yaml,
        string path,
        string r2Path,
        string bucket,
        string endpoint,
        string monitorInterval,
        string checkpointInterval)
    {
        // Normalize to forward slashes for cross-platform compatibility (litestream runs on Linux)
        var normalizedPath = path.Replace('\\', '/');
        yaml.AppendLine($"  - path: {normalizedPath}");
        yaml.AppendLine($"    monitor-interval: {monitorInterval}");
        yaml.AppendLine($"    checkpoint-interval: {checkpointInterval}");
        yaml.AppendLine($"    replica:");
        yaml.AppendLine($"      type: s3");
        yaml.AppendLine($"      bucket: {bucket}");
        yaml.AppendLine($"      path: {r2Path}");
        yaml.AppendLine($"      endpoint: {endpoint}");
        yaml.AppendLine($"      force-path-style: true");
    }
}
