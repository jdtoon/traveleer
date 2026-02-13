using Microsoft.Extensions.Options;
using saas.Shared;

namespace saas.Infrastructure.Services;

public class BackupStatusService : IBackupStatusService
{
    private readonly IConfiguration _configuration;
    private readonly BackupOptions _options;

    public BackupStatusService(IConfiguration configuration, IOptions<BackupOptions> options)
    {
        _configuration = configuration;
        _options = options.Value;
    }

    public Task<BackupStatusModel> GetStatusAsync(CancellationToken ct = default)
    {
        var corePath = GetDatabasePath("CoreDatabase", "/app/db/core.db");
        var auditPath = GetDatabasePath("AuditDatabase", "/app/db/audit.db");
        var tenantPath = _configuration["Tenancy:DatabasePath"] ?? "/app/db/tenants";

        var tenantCount = Directory.Exists(tenantPath)
            ? Directory.GetFiles(tenantPath, "*.db").Length
            : 0;

        var model = new BackupStatusModel
        {
            AutoRestoreEnabled = _options.AutoRestoreEnabled,
            LitestreamConfigured = !string.IsNullOrWhiteSpace(_options.R2Bucket)
                                  && !string.IsNullOrWhiteSpace(_options.R2Endpoint),
            LitestreamBinaryAvailable = File.Exists("/usr/local/bin/litestream"),
            LitestreamConfigExists = File.Exists(_options.LitestreamConfigPath),
            CoreDatabaseExists = File.Exists(corePath),
            AuditDatabaseExists = File.Exists(auditPath),
            TenantDatabaseCount = tenantCount,
            KeyBackupEnabled = _options.KeyBackupEnabled,
            KeyBackupPath = _options.KeyBackupPath,
            LastKeyBackupUtc = ReadTimestamp(_options.KeyBackupMarkerPath),
            LitestreamConfigUpdatedUtc = ReadLastWrite(_options.LitestreamConfigPath),
            LitestreamReloadSignalUtc = ReadTimestamp(_options.SentinelPath)
        };

        return Task.FromResult(model);
    }

    private string GetDatabasePath(string name, string fallback)
    {
        var cs = _configuration.GetConnectionString(name) ?? $"Data Source={fallback}";
        return cs.Replace("Data Source=", "", StringComparison.OrdinalIgnoreCase).Trim();
    }

    private static DateTime? ReadTimestamp(string path)
    {
        if (!File.Exists(path))
            return null;

        var text = File.ReadAllText(path).Trim();
        return DateTime.TryParse(text, out var dt) ? dt : null;
    }

    private static DateTime? ReadLastWrite(string path)
    {
        return File.Exists(path)
            ? File.GetLastWriteTimeUtc(path)
            : null;
    }
}
