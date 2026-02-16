using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using saas.Shared;

namespace saas.Infrastructure.Services;

public class LitestreamRestoreService : ILitestreamRestoreService
{
    private readonly IConfiguration _configuration;
    private readonly LitestreamOptions _options;
    private readonly ILogger<LitestreamRestoreService> _logger;
    private readonly string _coreDbPath;
    private readonly string _auditDbPath;
    private readonly string _tenantDbPath;
    private readonly string _hangfireDbPath;
    private readonly IStorageService _storageService;

    public LitestreamRestoreService(
        IConfiguration configuration,
        IOptions<LitestreamOptions> options,
        ILogger<LitestreamRestoreService> logger,
        IStorageService storageService)
    {
        _configuration = configuration;
        _options = options.Value;
        _logger = logger;
        _storageService = storageService;

        var coreCs = configuration.GetConnectionString("CoreDatabase") ?? "Data Source=/app/db/core.db";
        var auditCs = configuration.GetConnectionString("AuditDatabase") ?? "Data Source=/app/db/audit.db";
        _coreDbPath = coreCs.Replace("Data Source=", "", StringComparison.OrdinalIgnoreCase).Trim();
        _auditDbPath = auditCs.Replace("Data Source=", "", StringComparison.OrdinalIgnoreCase).Trim();
        _tenantDbPath = configuration["Tenancy:DatabasePath"] ?? "/app/db/tenants";
        _hangfireDbPath = configuration["Hangfire:SQLitePath"] ?? "/app/db/hangfire.db";
    }

    public async Task RestoreIfNeededAsync(CancellationToken ct = default)
    {
        if (!_options.AutoRestoreEnabled)
        {
            _logger.LogInformation("Litestream restore gate disabled by configuration");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.R2Bucket) || string.IsNullOrWhiteSpace(_options.R2Endpoint))
        {
            _logger.LogInformation("Skipping Litestream restore: Litestream:R2Bucket or Litestream:R2Endpoint is not configured");
            return;
        }

        if (!File.Exists("/usr/local/bin/litestream"))
        {
            _logger.LogWarning("Skipping Litestream restore: binary not found at /usr/local/bin/litestream");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_coreDbPath) ?? "/app/db");
        Directory.CreateDirectory(Path.GetDirectoryName(_auditDbPath) ?? "/app/db");
        Directory.CreateDirectory(_tenantDbPath);

        // Restore DataProtection keys from storage before DB restore (needed for token decryption)
        if (_options.KeyBackupEnabled)
        {
            await RestoreKeysIfNeededAsync(ct);
        }

        await RestoreDatabaseIfMissingAsync(_coreDbPath, "core.db", ct);
        await RestoreDatabaseIfMissingAsync(_auditDbPath, "audit.db", ct);

        // Restore hangfire.db if using SQLite storage
        var hangfireStorage = _configuration.GetValue("Hangfire:Storage", "InMemory");
        if (string.Equals(hangfireStorage, "SQLite", StringComparison.OrdinalIgnoreCase))
        {
            var hangfireDir = Path.GetDirectoryName(_hangfireDbPath);
            if (!string.IsNullOrEmpty(hangfireDir)) Directory.CreateDirectory(hangfireDir);
            await RestoreDatabaseIfMissingAsync(_hangfireDbPath, "hangfire.db", ct);
        }

        if (File.Exists(_coreDbPath))
        {
            var tenantSlugs = await ReadTenantSlugsAsync(_coreDbPath, ct);
            foreach (var slug in tenantSlugs)
            {
                var tenantDbFile = Path.Combine(_tenantDbPath, $"{slug}.db");
                var replicaPath = $"tenants/{slug}.db";
                await RestoreDatabaseIfMissingAsync(tenantDbFile, replicaPath, ct);
            }
        }
    }

    private async Task RestoreKeysIfNeededAsync(CancellationToken ct)
    {
        var keysDir = Path.Combine(Path.GetDirectoryName(_coreDbPath) ?? "/app/db", "keys");
        if (Directory.Exists(keysDir) && Directory.GetFiles(keysDir).Length > 0)
        {
            _logger.LogInformation("DataProtection keys already exist at {Path}, skipping restore", keysDir);
            return;
        }

        try
        {
            var zipStream = await _storageService.DownloadAsync(_options.KeyBackupPath, ct);
            if (zipStream is null)
            {
                _logger.LogInformation("No key backup found at {Path}, starting with fresh keys", _options.KeyBackupPath);
                return;
            }

            Directory.CreateDirectory(keysDir);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                var destPath = Path.Combine(keysDir, entry.Name);
                entry.ExtractToFile(destPath, overwrite: true);
            }
            _logger.LogInformation("Restored {Count} DataProtection keys from backup", archive.Entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore DataProtection keys from backup — continuing with fresh keys");
        }
    }

    private async Task RestoreDatabaseIfMissingAsync(string localPath, string replicaPath, CancellationToken ct)
    {
        if (File.Exists(localPath))
            return;

        _logger.LogInformation("Attempting Litestream restore for {LocalPath} from replica {ReplicaPath}", localPath, replicaPath);

        var replicaUrl = BuildReplicaUrl(replicaPath);
        var process = new Process
        {
            StartInfo = BuildProcessStartInfo(localPath, replicaUrl)
        };

        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stdErrTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);
        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        if (!string.IsNullOrWhiteSpace(stdOut))
            _logger.LogInformation("litestream restore output: {Output}", stdOut.Trim());

        if (process.ExitCode != 0)
        {
            _logger.LogError("litestream restore failed for {LocalPath} (exit {ExitCode}): {Error}", localPath, process.ExitCode, stdErr.Trim());
            throw new InvalidOperationException($"Litestream restore failed for {localPath} with exit code {process.ExitCode}");
        }

        if (File.Exists(localPath))
            _logger.LogInformation("Restored database {LocalPath}", localPath);
        else
            _logger.LogInformation("No replica found for {LocalPath}; continuing with fresh database creation", localPath);
    }

    private ProcessStartInfo BuildProcessStartInfo(string outputPath, string replicaUrl)
    {
        var accessKey =
            _configuration["R2_ACCESS_KEY_ID"] ??
            _configuration["Litestream:R2_ACCESS_KEY_ID"] ??
            _configuration["Storage:R2AccessKey"];

        var secretKey =
            _configuration["R2_SECRET_ACCESS_KEY"] ??
            _configuration["Litestream:R2_SECRET_ACCESS_KEY"] ??
            _configuration["Storage:R2SecretKey"];

        var psi = new ProcessStartInfo
        {
            FileName = "/usr/local/bin/litestream",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        psi.ArgumentList.Add("restore");
        psi.ArgumentList.Add("-if-db-not-exists");
        psi.ArgumentList.Add("-if-replica-exists");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(outputPath);
        psi.ArgumentList.Add(replicaUrl);

        if (!string.IsNullOrWhiteSpace(accessKey))
            psi.Environment["LITESTREAM_ACCESS_KEY_ID"] = accessKey;
        if (!string.IsNullOrWhiteSpace(secretKey))
            psi.Environment["LITESTREAM_SECRET_ACCESS_KEY"] = secretKey;

        return psi;
    }

    private string BuildReplicaUrl(string replicaPath)
    {
        var endpoint = Uri.EscapeDataString(_options.R2Endpoint.Trim());
        return $"s3://{_options.R2Bucket}/{replicaPath}?endpoint={endpoint}";
    }

    private async Task<List<string>> ReadTenantSlugsAsync(string coreDbPath, CancellationToken ct)
    {
        var slugs = new List<string>();

        try
        {
            await using var connection = new SqliteConnection($"Data Source={coreDbPath}");
            await connection.OpenAsync(ct);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Slug FROM Tenants";

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (!reader.IsDBNull(0))
                {
                    var slug = reader.GetString(0);
                    if (!string.IsNullOrWhiteSpace(slug))
                        slugs.Add(slug);
                }
            }
        }
        catch (SqliteException ex)
        {
            _logger.LogInformation(ex, "Tenant slug discovery skipped during restore bootstrap");
        }

        return slugs;
    }
}
