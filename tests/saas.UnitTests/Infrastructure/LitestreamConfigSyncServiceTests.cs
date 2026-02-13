using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using saas.Infrastructure.Services;
using saas.Shared;
using Xunit;

namespace saas.Tests.Infrastructure;

public class LitestreamConfigSyncServiceTests : IDisposable
{
    private readonly string _tempDir;

    public LitestreamConfigSyncServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"litestream-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void GenerateYaml_IncludesCoreAndAuditDatabases()
    {
        var yaml = LitestreamConfigSyncService.GenerateYaml(
            coreDbPath: "/app/db/core.db",
            auditDbPath: "/app/db/audit.db",
            tenantDbs: Array.Empty<string>(),
            bucket: "my-bucket",
            endpoint: "https://r2.example.com");

        Assert.Contains("path: /app/db/core.db", yaml);
        Assert.Contains("path: core.db", yaml);
        Assert.Contains("path: /app/db/audit.db", yaml);
        Assert.Contains("path: audit.db", yaml);
        Assert.Contains("bucket: my-bucket", yaml);
        Assert.Contains("endpoint: https://r2.example.com", yaml);
        Assert.Contains("force-path-style: true", yaml);
        Assert.Contains("sync-interval: 30s", yaml);
        Assert.Contains("monitor-interval: 5s", yaml);
        Assert.Contains("checkpoint-interval: 5m", yaml);
        Assert.Contains("snapshot:", yaml);
    }

    [Fact]
    public void GenerateYaml_IncludesTenantDatabases()
    {
        var tenantDbs = new[]
        {
            "/app/db/tenants/acme.db",
            "/app/db/tenants/globex.db"
        };

        var yaml = LitestreamConfigSyncService.GenerateYaml(
            coreDbPath: "/app/db/core.db",
            auditDbPath: "/app/db/audit.db",
            tenantDbs: tenantDbs,
            bucket: "backups",
            endpoint: "https://r2.example.com");

        Assert.Contains("path: /app/db/tenants/acme.db", yaml);
        Assert.Contains("path: tenants/acme.db", yaml);
        Assert.Contains("path: /app/db/tenants/globex.db", yaml);
        Assert.Contains("path: tenants/globex.db", yaml);
    }

    [Fact]
    public void GenerateYaml_SortsTenantDatabases()
    {
        var tenantDbs = new[]
        {
            "/app/db/tenants/zebra.db",
            "/app/db/tenants/alpha.db"
        };

        var yaml = LitestreamConfigSyncService.GenerateYaml(
            coreDbPath: "/app/db/core.db",
            auditDbPath: "/app/db/audit.db",
            tenantDbs: tenantDbs,
            bucket: "backups",
            endpoint: "https://r2.example.com");

        var alphaIndex = yaml.IndexOf("alpha.db");
        var zebraIndex = yaml.IndexOf("zebra.db");
        Assert.True(alphaIndex < zebraIndex, "Tenant databases should be sorted alphabetically");
    }

    [Fact]
    public void GenerateYaml_StartsWithDbsKey()
    {
        var yaml = LitestreamConfigSyncService.GenerateYaml(
            coreDbPath: "/app/db/core.db",
            auditDbPath: "/app/db/audit.db",
            tenantDbs: Array.Empty<string>(),
            bucket: "b",
            endpoint: "e");

        Assert.Contains("dbs:", yaml);
    }

    [Fact]
    public void GenerateYaml_AllowsCustomTuningValues()
    {
        var yaml = LitestreamConfigSyncService.GenerateYaml(
            coreDbPath: "/app/db/core.db",
            auditDbPath: "/app/db/audit.db",
            tenantDbs: Array.Empty<string>(),
            bucket: "b",
            endpoint: "e",
            syncInterval: "60s",
            monitorInterval: "10s",
            checkpointInterval: "10m",
            snapshotInterval: "48h",
            snapshotRetention: "336h");

        Assert.Contains("sync-interval: 60s", yaml);
        Assert.Contains("monitor-interval: 10s", yaml);
        Assert.Contains("checkpoint-interval: 10m", yaml);
        Assert.Contains("interval: 48h", yaml);
        Assert.Contains("retention: 336h", yaml);
    }

    [Fact]
    public async Task SyncConfigAsync_WritesConfigAndSentinelFile()
    {
        var configPath = Path.Combine(_tempDir, "litestream.yml");
        var sentinelPath = Path.Combine(_tempDir, ".litestream-reload");
        var tenantDir = Path.Combine(_tempDir, "tenants");
        Directory.CreateDirectory(tenantDir);

        // Create a fake tenant db
        await File.WriteAllBytesAsync(Path.Combine(tenantDir, "test.db"), Array.Empty<byte>());

        var coreDbPath = Path.Combine(_tempDir, "core.db");
        var auditDbPath = Path.Combine(_tempDir, "audit.db");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CoreDatabase"] = $"Data Source={coreDbPath}",
                ["ConnectionStrings:AuditDatabase"] = $"Data Source={auditDbPath}",
                ["Tenancy:DatabasePath"] = tenantDir
            })
            .Build();

        var options = Options.Create(new BackupOptions
        {
            LitestreamConfigPath = configPath,
            SentinelPath = sentinelPath,
            R2Bucket = "test-bucket",
            R2Endpoint = "https://test.r2.example.com"
        });

        var service = new LitestreamConfigSyncService(
            config,
            options,
            NullLogger<LitestreamConfigSyncService>.Instance);

        await service.SyncConfigAsync();

        Assert.True(File.Exists(configPath), "Config file should be written");
        Assert.True(File.Exists(sentinelPath), "Sentinel file should be written");

        var yaml = await File.ReadAllTextAsync(configPath);
        Assert.Contains("test-bucket", yaml);
        Assert.Contains("tenants/test.db", yaml);
        Assert.Contains(coreDbPath.Replace('\\', '/'), yaml);
        Assert.Contains(auditDbPath.Replace('\\', '/'), yaml);
    }

    [Fact]
    public async Task SyncConfigAsync_DoesNotRewriteIfUnchanged()
    {
        var configPath = Path.Combine(_tempDir, "litestream.yml");
        var sentinelPath = Path.Combine(_tempDir, ".litestream-reload");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CoreDatabase"] = "Data Source=/app/db/core.db",
                ["ConnectionStrings:AuditDatabase"] = "Data Source=/app/db/audit.db",
                ["Tenancy:DatabasePath"] = Path.Combine(_tempDir, "nonexistent")
            })
            .Build();

        var options = Options.Create(new BackupOptions
        {
            LitestreamConfigPath = configPath,
            SentinelPath = sentinelPath,
            R2Bucket = "b",
            R2Endpoint = "e"
        });

        var service = new LitestreamConfigSyncService(
            config,
            options,
            NullLogger<LitestreamConfigSyncService>.Instance);

        // First run — writes config
        await service.SyncConfigAsync();
        var firstWriteTime = File.GetLastWriteTimeUtc(sentinelPath);

        // Small delay to detect timestamp change
        await Task.Delay(50);

        // Second run — no change, sentinel should NOT be updated
        await service.SyncConfigAsync();
        var secondWriteTime = File.GetLastWriteTimeUtc(sentinelPath);

        Assert.Equal(firstWriteTime, secondWriteTime);
    }
}
