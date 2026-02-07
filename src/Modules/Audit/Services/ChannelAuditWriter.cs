using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using saas.Data.Audit;
using saas.Shared;

namespace saas.Modules.Audit.Services;

/// <summary>
/// Fire-and-forget audit writer backed by a <see cref="Channel{T}"/>.
/// Entries are enqueued immediately (non-blocking) and consumed by a background
/// hosted service that writes them to <see cref="AuditDbContext"/>.
/// </summary>
public sealed class ChannelAuditWriter : BackgroundService, IAuditWriter
{
    private readonly Channel<AuditEntry> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChannelAuditWriter> _logger;

    public ChannelAuditWriter(
        IServiceScopeFactory scopeFactory,
        ILogger<ChannelAuditWriter> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        // Unbounded channel — audit writes should never block the request pipeline.
        // Backpressure is not needed because audit entries are small and flush quickly.
        _channel = Channel.CreateUnbounded<AuditEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>
    /// Enqueue an audit entry for background persistence.
    /// Returns immediately — never blocks the calling request.
    /// </summary>
    public ValueTask WriteAsync(AuditEntry entry)
    {
        if (!_channel.Writer.TryWrite(entry))
        {
            _logger.LogWarning("Failed to enqueue audit entry for {EntityType} {EntityId}",
                entry.EntityType, entry.EntityId);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Background consumer that reads entries from the channel and writes to the audit DB.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Audit writer background consumer started");

        try
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

                    db.AuditEntries.Add(entry);
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to persist audit entry for {EntityType} {EntityId}",
                        entry.EntityType, entry.EntityId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown — host signalled cancellation, not an error
        }

        _logger.LogInformation("Audit writer background consumer stopped");
    }

    /// <summary>
    /// Flush remaining entries before shutdown.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }
}
