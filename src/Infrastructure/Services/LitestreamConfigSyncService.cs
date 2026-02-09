using System.Text;
using Microsoft.Extensions.Options;
using saas.Shared;

namespace saas.Infrastructure.Services;

public class LitestreamConfigSyncService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LitestreamConfigSyncService> _logger;
    private readonly BackupOptions _options;
    private readonly string _coreDbPath;
    private readonly string _auditDbPath;
    private readonly string _tenantDbPath;
    private readonly TimeSpan _syncInterval = TimeSpan.FromMinutes(5);

    public LitestreamConfigSyncService(
        IConfiguration configuration,
        IOptions<BackupOptions> options,
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

        var yaml = GenerateYaml(_coreDbPath, _auditDbPath, tenantDbs, bucket, endpoint);

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
        string endpoint)
    {
        var yaml = new StringBuilder();
        yaml.AppendLine("dbs:");

        // Core database
        AppendDbEntry(yaml, coreDbPath, "core.db", bucket, endpoint);

        // Audit database
        AppendDbEntry(yaml, auditDbPath, "audit.db", bucket, endpoint);

        // Tenant databases
        foreach (var dbFile in tenantDbs.OrderBy(f => f))
        {
            var fileName = Path.GetFileName(dbFile);
            var r2Path = $"tenants/{fileName}";
            AppendDbEntry(yaml, dbFile, r2Path, bucket, endpoint);
        }

        return yaml.ToString();
    }

    private static void AppendDbEntry(
        StringBuilder yaml,
        string path,
        string r2Path,
        string bucket,
        string endpoint)
    {
        yaml.AppendLine($"  - path: {path}");
        yaml.AppendLine($"    replicas:");
        yaml.AppendLine($"      - type: s3");
        yaml.AppendLine($"        bucket: {bucket}");
        yaml.AppendLine($"        path: {r2Path}");
        yaml.AppendLine($"        endpoint: {endpoint}");
        yaml.AppendLine($"        force-path-style: true");
    }
}
