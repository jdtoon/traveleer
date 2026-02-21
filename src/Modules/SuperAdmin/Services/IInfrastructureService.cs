using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace saas.Modules.SuperAdmin.Services;

public interface IInfrastructureService
{
    Task<SystemHealthModel> GetSystemHealthAsync();
    Task<RedisInfoModel?> GetRedisInfoAsync();
    Task<RabbitMqStatusModel?> GetRabbitMqStatusAsync();
    Task<DiskUsageModel> GetDiskUsageAsync();
    Task<List<DatabaseSizeInfo>> GetDatabaseSizesAsync();
    Task<HangfireStatusModel> GetHangfireStatusAsync();
}

// ── Models ───────────────────────────────────────────────────────────────────

public class SystemHealthModel
{
    public string OverallStatus { get; set; } = "Unknown";
    public TimeSpan TotalDuration { get; set; }
    public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;
    public List<HealthCheckResult> Checks { get; set; } = [];

    public class HealthCheckResult
    {
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = "Unknown";
        public string? Description { get; set; }
        public TimeSpan Duration { get; set; }
        public string? Exception { get; set; }
        public Dictionary<string, object> Data { get; set; } = [];
    }
}

public class RedisInfoModel
{
    public bool IsConnected { get; set; }
    public string? RedisVersion { get; set; }
    public TimeSpan? Uptime { get; set; }
    public long ConnectedClients { get; set; }
    public string? UsedMemory { get; set; }
    public long UsedMemoryBytes { get; set; }
    public string? PeakMemory { get; set; }
    public double MemoryFragmentationRatio { get; set; }
    public long KeyspaceHits { get; set; }
    public long KeyspaceMisses { get; set; }
    public double HitRate { get; set; }
    public long TotalKeys { get; set; }
    public long EvictedKeys { get; set; }
    public long TotalCommandsProcessed { get; set; }
    public string? InstanceName { get; set; }
    public string? Endpoint { get; set; }
}

public class RabbitMqStatusModel
{
    public bool IsConfigured { get; set; }
    public string Provider { get; set; } = "InMemory";
    public string? Host { get; set; }
    public int Port { get; set; }
    public string? VirtualHost { get; set; }
    public bool IsReachable { get; set; }
    public string? ManagementUrl { get; set; }
    public RabbitMqOverview? Overview { get; set; }
}

public class RabbitMqOverview
{
    public int QueueCount { get; set; }
    public int ConnectionCount { get; set; }
    public int ChannelCount { get; set; }
    public int ConsumerCount { get; set; }
    public long MessagesReady { get; set; }
    public long MessagesUnacknowledged { get; set; }
    public double PublishRate { get; set; }
    public double DeliverRate { get; set; }
}

public class DiskUsageModel
{
    public string DatabasePath { get; set; } = string.Empty;
    public long TotalDiskBytes { get; set; }
    public long FreeDiskBytes { get; set; }
    public long UsedByDatabasesBytes { get; set; }
    public double DiskUsagePercent { get; set; }
    public string TotalDiskFormatted => FormatBytes(TotalDiskBytes);
    public string FreeDiskFormatted => FormatBytes(FreeDiskBytes);
    public string UsedByDatabasesFormatted => FormatBytes(UsedByDatabasesBytes);

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}

public class DatabaseSizeInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // Core, Audit, Hangfire, Tenant
    public long SizeBytes { get; set; }
    public long WalSizeBytes { get; set; }
    public DateTime? LastModifiedUtc { get; set; }
    public string SizeFormatted => FormatBytes(SizeBytes);
    public string WalSizeFormatted => FormatBytes(WalSizeBytes);

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}

public class HangfireStatusModel
{
    public bool IsAvailable { get; set; }
    public string StorageType { get; set; } = "Unknown";
    public int ServerCount { get; set; }
    public long SucceededJobs { get; set; }
    public long FailedJobs { get; set; }
    public long ProcessingJobs { get; set; }
    public long EnqueuedJobs { get; set; }
    public long ScheduledJobs { get; set; }
    public long RecurringJobCount { get; set; }
    public List<FailedJobInfo> RecentFailures { get; set; } = [];
    public List<RecurringJobInfo> RecurringJobs { get; set; } = [];
}

public class FailedJobInfo
{
    public string JobId { get; set; } = string.Empty;
    public string JobName { get; set; } = string.Empty;
    public string? ExceptionMessage { get; set; }
    public DateTime FailedAt { get; set; }
}

public class RecurringJobInfo
{
    public string JobId { get; set; } = string.Empty;
    public string? Cron { get; set; }
    public DateTime? NextExecution { get; set; }
    public DateTime? LastExecution { get; set; }
    public string? LastJobState { get; set; }
}
