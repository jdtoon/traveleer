using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using saas.Data.Audit;
using saas.Modules.Audit.Services;
using Xunit;

namespace saas.Tests.Modules.Audit;

/// <summary>
/// Tests that the ChannelAuditWriter background consumer persists entries to AuditDbContext.
/// </summary>
public class AuditWriterTests : IAsyncLifetime
{
    private string _dbPath = null!;
    private ServiceProvider _serviceProvider = null!;
    private ChannelAuditWriter _writer = null!;

    public async Task InitializeAsync()
    {
        // Use a temp file-based database to avoid SQLite in-memory connection sharing
        // issues with the background consumer (active statement conflicts).
        _dbPath = Path.Combine(Path.GetTempPath(), $"audit_test_{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<AuditDbContext>(opts => opts.UseSqlite($"Data Source={_dbPath}"));
        _serviceProvider = services.BuildServiceProvider();

        // Ensure schema is created
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        _writer = new ChannelAuditWriter(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _serviceProvider.GetRequiredService<ILogger<ChannelAuditWriter>>());

        // Start the background consumer
        await _writer.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _writer.StopAsync(CancellationToken.None);
        _writer.Dispose();
        await _serviceProvider.DisposeAsync();

        // Clear SQLite connection pool so Windows releases the file lock
        SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public async Task WriteAsync_PersistsEntryToDatabase()
    {
        var entry = new AuditEntry
        {
            TenantSlug = "test",
            EntityType = "Widget",
            EntityId = "123",
            Action = "Created",
            UserId = "user-1",
            UserEmail = "test@test.com",
            Timestamp = DateTime.UtcNow
        };

        await _writer.WriteAsync(entry);
        await Task.Delay(300); // Allow background consumer to process

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var entries = await db.AuditEntries.ToListAsync();

        Assert.Single(entries);
        Assert.Equal("Widget", entries[0].EntityType);
        Assert.Equal("Created", entries[0].Action);
        Assert.Equal("test", entries[0].TenantSlug);
    }

    [Fact]
    public async Task WriteAsync_MultipleEntries_AllPersisted()
    {
        for (int i = 0; i < 5; i++)
        {
            await _writer.WriteAsync(new AuditEntry
            {
                TenantSlug = "bulk",
                EntityType = "Item",
                EntityId = i.ToString(),
                Action = "Created",
                Timestamp = DateTime.UtcNow
            });
        }

        await Task.Delay(500);

        // Retry with back-off — the background channel consumer may need more time
        int count = 0;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            count = await db.AuditEntries.CountAsync(e => e.TenantSlug == "bulk");
            if (count >= 5) break;
            await Task.Delay(200);
        }

        Assert.Equal(5, count);
    }

    [Fact]
    public async Task WriteAsync_PreservesOldAndNewValues()
    {
        var entry = new AuditEntry
        {
            TenantSlug = "test",
            EntityType = "Note",
            EntityId = "abc",
            Action = "Updated",
            OldValues = "{\"Title\":\"Before\"}",
            NewValues = "{\"Title\":\"After\"}",
            AffectedColumns = "Title",
            Timestamp = DateTime.UtcNow
        };

        await _writer.WriteAsync(entry);
        await Task.Delay(300);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var persisted = await db.AuditEntries.FirstAsync();

        Assert.Equal("{\"Title\":\"Before\"}", persisted.OldValues);
        Assert.Equal("{\"Title\":\"After\"}", persisted.NewValues);
        Assert.Equal("Title", persisted.AffectedColumns);
    }
}
