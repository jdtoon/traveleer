using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using saas.Data;
using saas.Data.Audit;
using saas.Data.Tenant;
using saas.Modules.Audit.Services;
using saas.Shared;
using Xunit;

namespace saas.Tests.Modules.Audit;

/// <summary>
/// Tests that [AuditIgnore] correctly excludes entities/properties from automatic audit trail.
/// Uses the AuditSaveChangesInterceptor (EF Core interceptor) wired into a test DbContext.
/// </summary>
public class AuditIgnoreTests : IAsyncLifetime
{
    private SqliteConnection _tenantConnection = null!;
    private AuditIgnoreTestDbContext _tenantDb = null!;
    private string _auditDbPath = null!;
    private AuditDbContext _auditDb = null!;
    private ChannelAuditWriter _auditWriter = null!;
    private ServiceProvider _serviceProvider = null!;

    public async Task InitializeAsync()
    {
        // Audit DB — temp file
        _auditDbPath = Path.Combine(Path.GetTempPath(), $"audit_test_{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<AuditDbContext>(opts => opts.UseSqlite($"Data Source={_auditDbPath}"));
        _serviceProvider = services.BuildServiceProvider();

        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        _auditWriter = new ChannelAuditWriter(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _serviceProvider.GetRequiredService<ILogger<ChannelAuditWriter>>());
        await _auditWriter.StartAsync(CancellationToken.None);

        // Interceptor with fake HTTP context
        var httpContextAccessor = new FakeHttpContextAccessor(
            new FakeTenantContext("test"),
            new FakeCurrentUser("u1", "test@test.com"));
        var interceptor = new AuditSaveChangesInterceptor(
            _auditWriter,
            httpContextAccessor,
            NullLogger<AuditSaveChangesInterceptor>.Instance);

        // Tenant DB with test entities — interceptor on options
        _tenantConnection = new SqliteConnection("Data Source=:memory:");
        await _tenantConnection.OpenAsync();
        var tenantOptions = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_tenantConnection)
            .AddInterceptors(interceptor)
            .Options;

        _tenantDb = new AuditIgnoreTestDbContext(tenantOptions);
        await _tenantDb.Database.EnsureCreatedAsync();

        // Separate AuditDbContext for assertions
        var auditOptions = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite($"Data Source={_auditDbPath}")
            .Options;
        _auditDb = new AuditDbContext(auditOptions);
    }

    public async Task DisposeAsync()
    {
        await _auditWriter.StopAsync(CancellationToken.None);
        _auditWriter.Dispose();
        await _serviceProvider.DisposeAsync();
        await _tenantDb.DisposeAsync();
        await _tenantConnection.DisposeAsync();
        await _auditDb.DisposeAsync();

        SqliteConnection.ClearAllPools();
        if (File.Exists(_auditDbPath))
            File.Delete(_auditDbPath);
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

    public class AuditIgnoreTestDbContext : TenantDbContext
    {
        public AuditIgnoreTestDbContext(DbContextOptions<TenantDbContext> options)
            : base(options)
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

    private class FakeHttpContextAccessor : IHttpContextAccessor
    {
        private readonly HttpContext _context;

        public FakeHttpContextAccessor(ITenantContext tenantContext, ICurrentUser currentUser)
        {
            var services = new ServiceCollection();
            services.AddSingleton(tenantContext);
            services.AddSingleton(currentUser);

            _context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        }

        public HttpContext? HttpContext
        {
            get => _context;
            set { }
        }
    }
}
