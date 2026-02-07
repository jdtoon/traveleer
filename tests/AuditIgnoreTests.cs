using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using saas.Data;
using saas.Data.Audit;
using saas.Data.Tenant;
using saas.Modules.Audit.Services;
using saas.Shared;
using Xunit;

namespace saas.Tests;

/// <summary>
/// Tests that [AuditIgnore] correctly excludes entities/properties from automatic audit trail.
/// Creates test entities directly in TenantDbContext via its existing DbSet mechanism
/// and validates audit behaviour via the real ChangeTracker pipeline.
/// </summary>
public class AuditIgnoreTests : IAsyncLifetime
{
    private SqliteConnection _tenantConnection = null!;
    private AuditIgnoreTestDbContext _tenantDb = null!;
    private SqliteConnection _auditConnection = null!;
    private AuditDbContext _auditDb = null!;
    private ChannelAuditWriter _auditWriter = null!;
    private ServiceProvider _serviceProvider = null!;

    public async Task InitializeAsync()
    {
        // Audit DB
        _auditConnection = new SqliteConnection("Data Source=:memory:");
        await _auditConnection.OpenAsync();
        var auditOptions = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_auditConnection).Options;
        _auditDb = new AuditDbContext(auditOptions);
        await _auditDb.Database.EnsureCreatedAsync();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<AuditDbContext>(opts => opts.UseSqlite(_auditConnection));
        _serviceProvider = services.BuildServiceProvider();

        _auditWriter = new ChannelAuditWriter(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _serviceProvider.GetRequiredService<ILogger<ChannelAuditWriter>>());
        await _auditWriter.StartAsync(CancellationToken.None);

        // Tenant DB with test entities
        _tenantConnection = new SqliteConnection("Data Source=:memory:");
        await _tenantConnection.OpenAsync();
        var tenantOptions = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_tenantConnection).Options;
        var tenantContext = new FakeTenantContext("test");
        var currentUser = new FakeCurrentUser("u1", "test@test.com");

        _tenantDb = new AuditIgnoreTestDbContext(tenantOptions, tenantContext, _auditWriter, currentUser);
        await _tenantDb.Database.EnsureCreatedAsync();
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
    public async Task ClassLevel_AuditIgnore_SkipsEntireEntity()
    {
        _tenantDb.IgnoredEntities.Add(new IgnoredEntity { Name = "Secret" });
        await _tenantDb.SaveChangesAsync();

        await Task.Delay(300);

        var entries = await _auditDb.AuditEntries.ToListAsync();
        Assert.Empty(entries);
    }

    [Fact]
    public async Task PropertyLevel_AuditIgnore_ExcludesFieldFromValues()
    {
        var entity = new PartialAuditEntity { Name = "Visible", Secret = "Hidden" };
        _tenantDb.PartialEntities.Add(entity);
        await _tenantDb.SaveChangesAsync();

        await Task.Delay(300);

        var entries = await _auditDb.AuditEntries.ToListAsync();
        Assert.Single(entries);
        Assert.Contains("Visible", entries[0].NewValues!);
        Assert.DoesNotContain("Hidden", entries[0].NewValues!);
        Assert.DoesNotContain("Secret", entries[0].NewValues!);
    }

    [Fact]
    public async Task PropertyLevel_AuditIgnore_SkipsEntryIfAllChangedPropsIgnored()
    {
        // Create the entity first
        var entity = new PartialAuditEntity { Name = "Test", Secret = "Original" };
        _tenantDb.PartialEntities.Add(entity);
        await _tenantDb.SaveChangesAsync();
        await Task.Delay(300);

        // Clear audit entries from the create
        var oldEntries = await _auditDb.AuditEntries.ToListAsync();
        _auditDb.AuditEntries.RemoveRange(oldEntries);
        await _auditDb.SaveChangesAsync();

        // Now update ONLY the ignored property
        entity.Secret = "Changed";
        await _tenantDb.SaveChangesAsync();
        await Task.Delay(300);

        var entries = await _auditDb.AuditEntries.ToListAsync();
        Assert.Empty(entries); // Should be skipped — only [AuditIgnore] property changed
    }

    // ── Test Entities ─────────────────────────────────────────────────────

    [AuditIgnore]
    public class IgnoredEntity : IAuditableEntity
    {
        public Guid Id { get; set; }
        public required string Name { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class PartialAuditEntity : IAuditableEntity
    {
        public Guid Id { get; set; }
        public required string Name { get; set; }

        [AuditIgnore]
        public string? Secret { get; set; }

        public string? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // ── Test DbContext (extends TenantDbContext with extra entity sets) ───

    /// <summary>
    /// Subclass of TenantDbContext that adds test-only entity types.
    /// Passes TenantDbContext-typed options to the base constructor.
    /// </summary>
    public class AuditIgnoreTestDbContext : TenantDbContext
    {
        public AuditIgnoreTestDbContext(
            DbContextOptions<TenantDbContext> options,
            ITenantContext tenantContext,
            IAuditWriter? auditWriter = null,
            ICurrentUser? currentUser = null)
            : base(options, tenantContext, auditWriter, currentUser)
        {
        }

        public DbSet<IgnoredEntity> IgnoredEntities => Set<IgnoredEntity>();
        public DbSet<PartialAuditEntity> PartialEntities => Set<PartialAuditEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<IgnoredEntity>();
            modelBuilder.Entity<PartialAuditEntity>();
        }
    }

    // ── Fakes ─────────────────────────────────────────────────────────────

    private class FakeTenantContext : ITenantContext
    {
        public FakeTenantContext(string slug) => Slug = slug;
        public string? Slug { get; }
        public Guid? TenantId => Guid.NewGuid();
        public string? PlanSlug => "test";
        public string? TenantName => "Test";
        public bool IsTenantRequest => true;
    }

    private class FakeCurrentUser : ICurrentUser
    {
        public FakeCurrentUser(string userId, string email) { UserId = userId; Email = email; }
        public string? UserId { get; }
        public string? Email { get; }
        public string? DisplayName => Email;
        public bool IsAuthenticated => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions => [];
        public bool HasPermission(string permission) => false;
        public bool HasAnyPermission(params string[] permissions) => false;
    }
}
