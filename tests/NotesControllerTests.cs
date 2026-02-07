using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using saas.Data.Audit;
using saas.Data.Tenant;
using saas.Modules.Audit.Services;
using saas.Modules.Notes.Entities;
using saas.Modules.Notes.Services;
using saas.Shared;
using Xunit;

namespace saas.Tests;

/// <summary>
/// Tests for NotesService CRUD operations against TenantDbContext.
/// Audit trail entries are now generated automatically by TenantDbContext's
/// SaveChangesAsync override — no manual audit code in services.
/// </summary>
public class NotesServiceTests : IAsyncLifetime
{
    private SqliteConnection _tenantConnection = null!;
    private TenantDbContext _tenantDb = null!;
    private SqliteConnection _auditConnection = null!;
    private AuditDbContext _auditDb = null!;
    private NotesService _service = null!;
    private ChannelAuditWriter _auditWriter = null!;
    private ServiceProvider _serviceProvider = null!;

    public async Task InitializeAsync()
    {
        // Audit DB — SQLite in-memory
        _auditConnection = new SqliteConnection("Data Source=:memory:");
        await _auditConnection.OpenAsync();
        var auditOptions = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_auditConnection)
            .Options;
        _auditDb = new AuditDbContext(auditOptions);
        await _auditDb.Database.EnsureCreatedAsync();

        // Build a minimal DI container for ChannelAuditWriter
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<AuditDbContext>(opts => opts.UseSqlite(_auditConnection));
        _serviceProvider = services.BuildServiceProvider();

        _auditWriter = new ChannelAuditWriter(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _serviceProvider.GetRequiredService<ILogger<ChannelAuditWriter>>());

        // Start the background consumer so audit entries get processed
        await _auditWriter.StartAsync(CancellationToken.None);

        // Tenant DB — SQLite in-memory, with audit deps injected into TenantDbContext
        _tenantConnection = new SqliteConnection("Data Source=:memory:");
        await _tenantConnection.OpenAsync();
        var tenantOptions = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_tenantConnection)
            .Options;
        var tenantContext = new FakeTenantContext("test-tenant");
        var currentUser = new FakeCurrentUser("user-1", "test@example.com");

        _tenantDb = new TenantDbContext(tenantOptions, tenantContext, _auditWriter, currentUser);
        await _tenantDb.Database.EnsureCreatedAsync();

        // NotesService only needs TenantDbContext — pure CRUD, no audit deps
        _service = new NotesService(_tenantDb);
    }

    public async Task DisposeAsync()
    {
        await _auditWriter.StopAsync(CancellationToken.None);
        _auditWriter.Dispose();
        await _serviceProvider.DisposeAsync();
        await _tenantDb.DisposeAsync();
        await _tenantConnection.DisposeAsync();
        await _auditDb.DisposeAsync();
        await _auditConnection.DisposeAsync();
    }

    [Fact]
    public async Task GetAllAsync_EmptyDb_ReturnsEmptyList()
    {
        var notes = await _service.GetAllAsync();
        Assert.Empty(notes);
    }

    [Fact]
    public async Task CreateAsync_AddsNoteToDb()
    {
        var note = new Note { Title = "Test Note", Content = "Hello world" };

        var created = await _service.CreateAsync(note);

        Assert.NotEqual(Guid.Empty, created.Id);
        var fromDb = await _tenantDb.Notes.FindAsync(created.Id);
        Assert.NotNull(fromDb);
        Assert.Equal("Test Note", fromDb.Title);
        Assert.Equal("Hello world", fromDb.Content);
    }

    [Fact]
    public async Task CreateAsync_SetsCreatedByFromCurrentUser()
    {
        var note = new Note { Title = "Tracked Note" };
        await _service.CreateAsync(note);

        var fromDb = await _tenantDb.Notes.FindAsync(note.Id);
        Assert.Equal("test@example.com", fromDb!.CreatedBy);
        Assert.True(fromDb.CreatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task CreateAsync_WritesAuditEntryAutomatically()
    {
        var note = new Note { Title = "Audited Note", Content = "Content" };
        await _service.CreateAsync(note);

        await Task.Delay(300);

        var entries = await _auditDb.AuditEntries.ToListAsync();
        Assert.Single(entries);
        Assert.Equal("Note", entries[0].EntityType);
        Assert.Equal("Created", entries[0].Action);
        Assert.Equal("test-tenant", entries[0].TenantSlug);
        Assert.Equal("test@example.com", entries[0].UserEmail);
        Assert.Contains("Audited Note", entries[0].NewValues!);
        Assert.Equal(note.Id.ToString(), entries[0].EntityId);
    }

    [Fact]
    public async Task UpdateAsync_ChangesNoteFields()
    {
        var note = new Note { Title = "Original", Content = "Old content", Color = "gray" };
        await _service.CreateAsync(note);

        var updated = new Note { Title = "Updated", Content = "New content", Color = "blue" };
        await _service.UpdateAsync(note.Id, updated);

        var fromDb = await _tenantDb.Notes.FindAsync(note.Id);
        Assert.NotNull(fromDb);
        Assert.Equal("Updated", fromDb.Title);
        Assert.Equal("New content", fromDb.Content);
        Assert.Equal("blue", fromDb.Color);
    }

    [Fact]
    public async Task UpdateAsync_SetsUpdatedByFromCurrentUser()
    {
        var note = new Note { Title = "Before" };
        await _service.CreateAsync(note);

        var updated = new Note { Title = "After" };
        await _service.UpdateAsync(note.Id, updated);

        var fromDb = await _tenantDb.Notes.FindAsync(note.Id);
        Assert.Equal("test@example.com", fromDb!.UpdatedBy);
        Assert.NotNull(fromDb.UpdatedAt);
    }

    [Fact]
    public async Task UpdateAsync_WritesAuditWithOldAndNewValues()
    {
        var note = new Note { Title = "Before", Content = "Old", Color = "gray" };
        await _service.CreateAsync(note);

        var updated = new Note { Title = "After", Content = "New", Color = "blue" };
        await _service.UpdateAsync(note.Id, updated);

        await Task.Delay(300);

        var entries = await _auditDb.AuditEntries
            .Where(e => e.Action == "Updated")
            .ToListAsync();
        Assert.Single(entries);
        Assert.Contains("Before", entries[0].OldValues!);
        Assert.Contains("After", entries[0].NewValues!);
        Assert.NotNull(entries[0].AffectedColumns);
        Assert.Contains("Title", entries[0].AffectedColumns!);
    }

    [Fact]
    public async Task UpdateAsync_NonExistentNote_Throws()
    {
        var note = new Note { Title = "X", Content = "Y" };
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UpdateAsync(Guid.NewGuid(), note));
    }

    [Fact]
    public async Task DeleteAsync_RemovesNoteFromDb()
    {
        var note = new Note { Title = "To Delete" };
        await _service.CreateAsync(note);

        await _service.DeleteAsync(note.Id);

        var fromDb = await _tenantDb.Notes.FindAsync(note.Id);
        Assert.Null(fromDb);
    }

    [Fact]
    public async Task DeleteAsync_WritesAuditEntry()
    {
        var note = new Note { Title = "Deleted Note", Content = "Bye" };
        await _service.CreateAsync(note);
        await _service.DeleteAsync(note.Id);

        await Task.Delay(300);

        var entries = await _auditDb.AuditEntries
            .Where(e => e.Action == "Deleted")
            .ToListAsync();
        Assert.Single(entries);
        Assert.Contains("Deleted Note", entries[0].OldValues!);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentNote_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task TogglePinAsync_TogglesState()
    {
        var note = new Note { Title = "Pin Me", IsPinned = false };
        await _service.CreateAsync(note);

        await _service.TogglePinAsync(note.Id);
        var fromDb = await _tenantDb.Notes.FindAsync(note.Id);
        Assert.True(fromDb!.IsPinned);

        // Detach to avoid tracking conflict, then re-fetch
        _tenantDb.Entry(fromDb).State = EntityState.Detached;

        await _service.TogglePinAsync(note.Id);
        fromDb = await _tenantDb.Notes.FindAsync(note.Id);
        Assert.False(fromDb!.IsPinned);
    }

    [Fact]
    public async Task TogglePinAsync_WritesAuditEntryAutomatically()
    {
        var note = new Note { Title = "Pin Audit", IsPinned = false };
        await _service.CreateAsync(note);
        await _service.TogglePinAsync(note.Id);

        await Task.Delay(300);

        var entries = await _auditDb.AuditEntries
            .Where(e => e.Action == "Updated")
            .ToListAsync();
        Assert.Single(entries);
        Assert.Contains("IsPinned", entries[0].AffectedColumns!);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsPinnedFirst()
    {
        await _service.CreateAsync(new Note { Title = "Unpinned", IsPinned = false });
        await Task.Delay(10); // ensure different CreatedAt
        var pinned = new Note { Title = "Pinned", IsPinned = true };
        await _service.CreateAsync(pinned);

        var all = await _service.GetAllAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal("Pinned", all[0].Title);
    }

    // ── Fakes ──────────────────────────────────────────────────────────────

    private class FakeTenantContext : ITenantContext
    {
        public FakeTenantContext(string slug) => Slug = slug;
        public string? Slug { get; }
        public Guid? TenantId => Guid.NewGuid();
        public string? PlanSlug => "test";
        public string? TenantName => "Test Tenant";
        public bool IsTenantRequest => true;
    }

    private class FakeCurrentUser : ICurrentUser
    {
        public FakeCurrentUser(string userId, string email)
        {
            UserId = userId;
            Email = email;
        }
        public string? UserId { get; }
        public string? Email { get; }
        public string? DisplayName => Email;
        public bool IsAuthenticated => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => ["Admin"];
        public IReadOnlyList<string> Permissions => ["notes.read", "notes.create", "notes.edit", "notes.delete"];
        public bool HasPermission(string permission) => Permissions.Contains(permission);
        public bool HasAnyPermission(params string[] permissions) => permissions.Any(p => Permissions.Contains(p));
    }
}
