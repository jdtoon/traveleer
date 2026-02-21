using System.Net.Sockets;
using System.Text.Json;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace saas.Modules.SuperAdmin.Services;

public class InfrastructureService : IInfrastructureService
{
    private readonly HealthCheckService _healthCheckService;
    private readonly IConnectionMultiplexer? _redis;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<InfrastructureService> _logger;

    public InfrastructureService(
        HealthCheckService healthCheckService,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<InfrastructureService> logger,
        IConnectionMultiplexer? redis = null)
    {
        _healthCheckService = healthCheckService;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _redis = redis;
    }

    // ── System Health ────────────────────────────────────────────────────────

    public async Task<SystemHealthModel> GetSystemHealthAsync()
    {
        var report = await _healthCheckService.CheckHealthAsync();

        return new SystemHealthModel
        {
            OverallStatus = report.Status.ToString(),
            TotalDuration = report.TotalDuration,
            CheckedAtUtc = DateTime.UtcNow,
            Checks = report.Entries.Select(e => new SystemHealthModel.HealthCheckResult
            {
                Name = e.Key,
                Status = e.Value.Status.ToString(),
                Description = e.Value.Description,
                Duration = e.Value.Duration,
                Exception = e.Value.Exception?.Message,
                Data = e.Value.Data.ToDictionary(kv => kv.Key, kv => kv.Value)
            }).ToList()
        };
    }

    // ── Redis ────────────────────────────────────────────────────────────────

    public async Task<RedisInfoModel?> GetRedisInfoAsync()
    {
        if (_redis is null)
            return null;

        var model = new RedisInfoModel
        {
            InstanceName = _configuration.GetValue<string>("Caching:Redis:InstanceName") ?? "saas:",
            IsConnected = _redis.IsConnected
        };

        if (!_redis.IsConnected)
            return model;

        try
        {
            var endpoints = _redis.GetEndPoints();
            if (endpoints.Length == 0) return model;

            model.Endpoint = endpoints[0].ToString();
            var server = _redis.GetServer(endpoints[0]);
            var info = await server.InfoAsync();

            foreach (var group in info)
            {
                foreach (var pair in group)
                {
                    switch (pair.Key)
                    {
                        case "redis_version": model.RedisVersion = pair.Value; break;
                        case "uptime_in_seconds":
                            if (long.TryParse(pair.Value, out var uptimeSec))
                                model.Uptime = TimeSpan.FromSeconds(uptimeSec);
                            break;
                        case "connected_clients":
                            if (long.TryParse(pair.Value, out var clients))
                                model.ConnectedClients = clients;
                            break;
                        case "used_memory":
                            if (long.TryParse(pair.Value, out var usedMem))
                                model.UsedMemoryBytes = usedMem;
                            break;
                        case "used_memory_human": model.UsedMemory = pair.Value; break;
                        case "used_memory_peak_human": model.PeakMemory = pair.Value; break;
                        case "mem_fragmentation_ratio":
                            if (double.TryParse(pair.Value, System.Globalization.CultureInfo.InvariantCulture, out var frag))
                                model.MemoryFragmentationRatio = frag;
                            break;
                        case "keyspace_hits":
                            if (long.TryParse(pair.Value, out var hits))
                                model.KeyspaceHits = hits;
                            break;
                        case "keyspace_misses":
                            if (long.TryParse(pair.Value, out var misses))
                                model.KeyspaceMisses = misses;
                            break;
                        case "evicted_keys":
                            if (long.TryParse(pair.Value, out var evicted))
                                model.EvictedKeys = evicted;
                            break;
                        case "total_commands_processed":
                            if (long.TryParse(pair.Value, out var cmds))
                                model.TotalCommandsProcessed = cmds;
                            break;
                    }
                }
            }

            // Compute hit rate
            var totalRequests = model.KeyspaceHits + model.KeyspaceMisses;
            model.HitRate = totalRequests > 0
                ? Math.Round((double)model.KeyspaceHits / totalRequests * 100, 2)
                : 0;

            // Get total key count
            try
            {
                model.TotalKeys = server.DatabaseSize();
            }
            catch
            {
                // DatabaseSize may not be available on all configurations
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve Redis server info");
        }

        return model;
    }

    // ── RabbitMQ ─────────────────────────────────────────────────────────────

    public async Task<RabbitMqStatusModel?> GetRabbitMqStatusAsync()
    {
        var provider = _configuration.GetValue<string>("Messaging:Provider") ?? "InMemory";
        var model = new RabbitMqStatusModel
        {
            Provider = provider,
            IsConfigured = provider.Equals("RabbitMQ", StringComparison.OrdinalIgnoreCase)
        };

        if (!model.IsConfigured)
            return model;

        model.Host = _configuration.GetValue<string>("Messaging:RabbitMQ:Host") ?? "rabbitmq";
        model.Port = _configuration.GetValue<int?>("Messaging:RabbitMQ:Port") ?? 5672;
        model.VirtualHost = _configuration.GetValue<string>("Messaging:RabbitMQ:VirtualHost") ?? "/";
        model.ManagementUrl = $"http://{model.Host}:15672";

        // Check TCP reachability
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(model.Host, model.Port);
            model.IsReachable = await Task.WhenAny(connectTask, Task.Delay(3000)) == connectTask
                                && client.Connected;
        }
        catch
        {
            model.IsReachable = false;
        }

        // Try to fetch management API overview
        if (model.IsReachable)
        {
            try
            {
                var username = _configuration.GetValue<string>("Messaging:RabbitMQ:Username") ?? "guest";
                var password = _configuration.GetValue<string>("Messaging:RabbitMQ:Password") ?? "guest";

                var httpClient = _httpClientFactory.CreateClient("RabbitMQ");
                httpClient.BaseAddress = new Uri(model.ManagementUrl);
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{username}:{password}"))
                );
                httpClient.Timeout = TimeSpan.FromSeconds(5);

                var response = await httpClient.GetAsync("/api/overview");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    model.Overview = new RabbitMqOverview
                    {
                        ConnectionCount = GetJsonInt(root, "object_totals.connections"),
                        ChannelCount = GetJsonInt(root, "object_totals.channels"),
                        ConsumerCount = GetJsonInt(root, "object_totals.consumers"),
                        QueueCount = GetJsonInt(root, "object_totals.queues"),
                        MessagesReady = GetJsonLong(root, "queue_totals.messages_ready"),
                        MessagesUnacknowledged = GetJsonLong(root, "queue_totals.messages_unacknowledged"),
                        PublishRate = GetJsonDouble(root, "message_stats.publish_details.rate"),
                        DeliverRate = GetJsonDouble(root, "message_stats.deliver_get_details.rate")
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch RabbitMQ management API overview");
            }
        }

        return model;
    }

    // ── Disk Usage ───────────────────────────────────────────────────────────

    public async Task<DiskUsageModel> GetDiskUsageAsync()
    {
        var dbPath = _configuration["Tenancy:DatabasePath"] ?? "db/tenants";
        var basePath = Path.GetDirectoryName(dbPath) ?? "db";

        var model = new DiskUsageModel { DatabasePath = basePath };

        try
        {
            var driveInfo = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(basePath)) ?? "C");
            model.TotalDiskBytes = driveInfo.TotalSize;
            model.FreeDiskBytes = driveInfo.AvailableFreeSpace;
            model.DiskUsagePercent = Math.Round((1.0 - (double)model.FreeDiskBytes / model.TotalDiskBytes) * 100, 2);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get drive info");
        }

        // Calculate total DB usage
        var databases = await GetDatabaseSizesAsync();
        model.UsedByDatabasesBytes = databases.Sum(d => d.SizeBytes + d.WalSizeBytes);

        return model;
    }

    // ── Database Sizes ───────────────────────────────────────────────────────

    public Task<List<DatabaseSizeInfo>> GetDatabaseSizesAsync()
    {
        var results = new List<DatabaseSizeInfo>();

        // Core databases
        AddDatabaseInfo(results, GetDbPathFromConnectionString("CoreDatabase"), "Core");
        AddDatabaseInfo(results, GetDbPathFromConnectionString("AuditDatabase"), "Audit");

        // Hangfire
        var hangfirePath = _configuration["Hangfire:SQLitePath"];
        if (!string.IsNullOrEmpty(hangfirePath))
            AddDatabaseInfo(results, hangfirePath, "Hangfire");

        // Tenant databases
        var tenantDir = _configuration["Tenancy:DatabasePath"] ?? "db/tenants";
        if (Directory.Exists(tenantDir))
        {
            foreach (var dbFile in Directory.GetFiles(tenantDir, "*.db"))
            {
                AddDatabaseInfo(results, dbFile, "Tenant");
            }
        }

        return Task.FromResult(results);
    }

    // ── Hangfire ─────────────────────────────────────────────────────────────

    public Task<HangfireStatusModel> GetHangfireStatusAsync()
    {
        var storageType = _configuration.GetValue<string>("Hangfire:Storage") ?? "InMemory";
        var model = new HangfireStatusModel { StorageType = storageType };

        try
        {
            var monitor = JobStorage.Current.GetMonitoringApi();
            model.IsAvailable = true;

            var stats = monitor.GetStatistics();
            model.SucceededJobs = stats.Succeeded;
            model.FailedJobs = stats.Failed;
            model.ProcessingJobs = stats.Processing;
            model.EnqueuedJobs = stats.Enqueued;
            model.ScheduledJobs = stats.Scheduled;
            model.ServerCount = (int)stats.Servers;
            model.RecurringJobCount = stats.Recurring;

            // Recent failures
            var failedJobs = monitor.FailedJobs(0, 10);
            model.RecentFailures = failedJobs.Select(j => new FailedJobInfo
            {
                JobId = j.Key,
                JobName = j.Value.Job?.Type?.Name ?? "Unknown",
                ExceptionMessage = j.Value.ExceptionMessage,
                FailedAt = j.Value.FailedAt ?? DateTime.MinValue
            }).ToList();

            // Recurring jobs
            using var connection = JobStorage.Current.GetConnection();
            var recurringJobs = connection.GetRecurringJobs();
            model.RecurringJobs = recurringJobs.Select(j => new RecurringJobInfo
            {
                JobId = j.Id,
                Cron = j.Cron,
                NextExecution = j.NextExecution,
                LastExecution = j.LastExecution,
                LastJobState = j.LastJobState
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query Hangfire status");
            model.IsAvailable = false;
        }

        return Task.FromResult(model);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string GetDbPathFromConnectionString(string name)
    {
        var cs = _configuration.GetConnectionString(name) ?? "";
        return cs.Replace("Data Source=", "", StringComparison.OrdinalIgnoreCase).Trim();
    }

    private static void AddDatabaseInfo(List<DatabaseSizeInfo> results, string path, string category)
    {
        if (string.IsNullOrEmpty(path)) return;

        var info = new FileInfo(path);
        var walInfo = new FileInfo(path + "-wal");

        results.Add(new DatabaseSizeInfo
        {
            FileName = Path.GetFileName(path),
            FilePath = path,
            Category = category,
            SizeBytes = info.Exists ? info.Length : 0,
            WalSizeBytes = walInfo.Exists ? walInfo.Length : 0,
            LastModifiedUtc = info.Exists ? info.LastWriteTimeUtc : null
        });
    }

    private static int GetJsonInt(JsonElement el, string path)
    {
        try
        {
            var parts = path.Split('.');
            var current = el;
            foreach (var part in parts)
            {
                if (!current.TryGetProperty(part, out current))
                    return 0;
            }
            return current.GetInt32();
        }
        catch { return 0; }
    }

    private static long GetJsonLong(JsonElement el, string path)
    {
        try
        {
            var parts = path.Split('.');
            var current = el;
            foreach (var part in parts)
            {
                if (!current.TryGetProperty(part, out current))
                    return 0;
            }
            return current.GetInt64();
        }
        catch { return 0; }
    }

    private static double GetJsonDouble(JsonElement el, string path)
    {
        try
        {
            var parts = path.Split('.');
            var current = el;
            foreach (var part in parts)
            {
                if (!current.TryGetProperty(part, out current))
                    return 0;
            }
            return current.GetDouble();
        }
        catch { return 0; }
    }
}
