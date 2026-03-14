using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using saas.Data.Core;
using saas.Data.Tenant;
using saas.Infrastructure.Provisioning;
using saas.Infrastructure.Services;
using saas.Shared;
using Xunit;

namespace saas.Tests.Infrastructure;

[Trait("Category", "Integration")]
public class TenantProvisionerTests : IAsyncLifetime
{
    private SqliteConnection _coreConnection = null!;
    private CoreDbContext _coreDb = null!;
    private ServiceProvider _serviceProvider = null!;
    private string _testDbPath = null!;
    private Guid _testPlanId;
    private readonly List<string> _provisionedDbPaths = [];
    private string _testTenantDir = null!;

    public async Task InitializeAsync()
    {
        // SQLite in-memory via a shared connection that stays open for the lifetime of the test
        _coreConnection = new SqliteConnection("Data Source=:memory:");
        await _coreConnection.OpenAsync();

        var coreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(_coreConnection)
            .Options;
        _coreDb = new CoreDbContext(coreOptions);
        await _coreDb.Database.EnsureCreatedAsync();

        // Seed test plan
        var testPlan = new Plan
        {
            Id = Guid.NewGuid(),
            Name = "Test Plan",
            Slug = "test",
            Description = "Test plan",
            MonthlyPrice = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _coreDb.Plans.Add(testPlan);
        await _coreDb.SaveChangesAsync();
        _testPlanId = testPlan.Id;

        // Unique temp file for the tenant database
        _testDbPath = Path.Combine(Path.GetTempPath(), $"tenant-test-{Guid.NewGuid()}.db");

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(_coreDb);

        services.AddDbContext<TenantDbContext>((_, opts) =>
            opts.UseSqlite($"Data Source={_testDbPath}"));

        services.AddIdentity<AppUser, AppRole>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            options.Password.RequiredLength = 1;
            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<TenantDbContext>()
        .AddDefaultTokenProviders();

        services.AddSingleton<IEmailService, ConsoleEmailService>();
        services.AddSingleton<IBotProtection, MockBotProtection>();
        services.AddSingleton<IPublishEndpoint>(new NullPublishEndpoint());

        // TenantProvisionerService requires IConfiguration for Tenancy:DatabasePath
        _testTenantDir = Path.Combine(Path.GetTempPath(), $"tenant-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testTenantDir);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tenancy:DatabasePath"] = _testTenantDir
            })
            .Build();
        services.AddSingleton<IConfiguration>(config);

        services.AddSingleton<IReadOnlyList<IModule>>(new IModule[]
        {
            new saas.Modules.Tenancy.TenancyModule(),
            new saas.Modules.Audit.AuditModule(),
            new saas.Modules.TenantAdmin.TenantAdminModule(),
            new saas.Modules.SuperAdmin.SuperAdminModule(),
            new saas.Modules.Registration.RegistrationModule(),
        });
        services.AddScoped<ITenantProvisioner, TenantProvisionerService>();

        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _coreDb.DisposeAsync();
        await _coreConnection.DisposeAsync();

        // Clean up temp tenant database file
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            var path = _testDbPath + suffix;
            if (File.Exists(path))
                try { File.Delete(path); } catch { }
        }

        // Clean up provisioned DB files (from ProvisionTenantAsync tests)
        foreach (var dbPath in _provisionedDbPaths)
        {
            foreach (var suffix in new[] { "", "-shm", "-wal" })
            {
                var path = dbPath + suffix;
                if (File.Exists(path))
                    try { File.Delete(path); } catch { }
            }
        }

        // Clean up the temp tenant directory
        if (Directory.Exists(_testTenantDir))
            try { Directory.Delete(_testTenantDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task ValidateSlugAsync_ValidSlug_ReturnsValid()
    {
        using var scope = _serviceProvider.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioner>();

        var result = await provisioner.ValidateSlugAsync("valid-slug-123");

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Theory]
    [InlineData("ab", "at least 3 characters")]
    [InlineData("", "Slug is required")]
    [InlineData("slug-", "must start and end")]
    [InlineData("-slug", "must start and end")]
    [InlineData("slug_with_underscore", "must contain only lowercase")]
    [InlineData("super-admin", "reserved")]
    [InlineData("admin", "reserved")]
    [InlineData("api", "reserved")]
    public async Task ValidateSlugAsync_InvalidSlug_ReturnsError(string slug, string expectedFragment)
    {
        using var scope = _serviceProvider.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioner>();

        var result = await provisioner.ValidateSlugAsync(slug);

        Assert.False(result.IsValid);
        Assert.Contains(expectedFragment, result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateSlugAsync_DuplicateSlug_ReturnsError()
    {
        _coreDb.Tenants.Add(new Tenant
        {
            Id = Guid.NewGuid(),
            Slug = "existing-slug",
            Name = "Existing",
            PlanId = _testPlanId,
            Status = TenantStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _coreDb.SaveChangesAsync();

        using var scope = _serviceProvider.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioner>();

        var result = await provisioner.ValidateSlugAsync("existing-slug");

        Assert.False(result.IsValid);
        Assert.Equal("This slug is already taken", result.ErrorMessage);
    }

    [Fact]
    public async Task ProvisionTenantAsync_ValidData_CreatesTenantAndDatabase()
    {
        var slug = $"test-prov-{Guid.NewGuid().ToString()[..8]}";
        var email = "admin@test.com";

        using var scope = _serviceProvider.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioner>();

        var result = await provisioner.ProvisionTenantAsync(slug, email, _testPlanId);

        // Core assertions
        Assert.True(result.Success);
        Assert.NotNull(result.TenantId);
        Assert.Null(result.ErrorMessage);

        var tenant = await _coreDb.Tenants.FindAsync(result.TenantId);
        Assert.NotNull(tenant);
        Assert.Equal(slug, tenant!.Slug);
        Assert.Equal(email, tenant.ContactEmail);
        Assert.Equal(TenantStatus.Active, tenant.Status);

        var subscription = await _coreDb.Subscriptions
            .FirstOrDefaultAsync(s => s.TenantId == result.TenantId);
        Assert.NotNull(subscription);
        Assert.Equal(SubscriptionStatus.Trialing, subscription!.Status);

        // Tenant DB assertions — the provisioner creates {Tenancy:DatabasePath}/{slug}.db
        var provisionedDbPath = Path.Combine(_testTenantDir, $"{slug}.db");
        _provisionedDbPaths.Add(provisionedDbPath);
        Assert.True(File.Exists(provisionedDbPath), $"Tenant DB file not found at {provisionedDbPath}");

        var tenantDbOptions = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={provisionedDbPath}")
            .Options;
        using var tenantDb = new TenantDbContext(tenantDbOptions);

        var adminUser = await tenantDb.Users.FirstOrDefaultAsync(u => u.Email == email);
        Assert.NotNull(adminUser);
        Assert.True(adminUser!.EmailConfirmed);

        // Roles
        var roles = await tenantDb.Roles.Select(r => r.Name).ToListAsync();
        Assert.Contains("Admin", roles);
        Assert.Contains("Member", roles);

        // Admin is in Admin role
        var userRoles = await tenantDb.UserRoles
            .Where(ur => ur.UserId == adminUser.Id)
            .ToListAsync();
        Assert.NotEmpty(userRoles);

        // Permissions seeded
        var permissions = await tenantDb.Permissions.ToListAsync();
        Assert.Equal(11, permissions.Count);
        Assert.Contains(permissions, p => p.Key == saas.Modules.TenantAdmin.TenantAdminPermissions.SettingsEdit);

        // RolePermissions: Admin has all 10
        var adminRole = await tenantDb.Roles.FirstAsync(r => r.Name == "Admin");
        var adminRolePerms = await tenantDb.RolePermissions
            .Where(rp => rp.RoleId == adminRole.Id)
            .ToListAsync();
        Assert.Equal(11, adminRolePerms.Count);
    }

    [Fact]
    public async Task ProvisionTenantAsync_InvalidSlug_ReturnsError()
    {
        using var scope = _serviceProvider.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioner>();

        var result = await provisioner.ProvisionTenantAsync("invalid_slug", "admin@test.com", _testPlanId);

        Assert.False(result.Success);
        Assert.Null(result.TenantId);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task ProvisionTenantAsync_InvalidEmail_ReturnsError()
    {
        using var scope = _serviceProvider.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioner>();

        var result = await provisioner.ProvisionTenantAsync("valid-slug", "not-an-email", _testPlanId);

        Assert.False(result.Success);
        Assert.Equal("Invalid email address", result.ErrorMessage);
    }

    [Fact]
    public async Task ProvisionTenantAsync_InvalidPlan_ReturnsError()
    {
        using var scope = _serviceProvider.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioner>();

        var result = await provisioner.ProvisionTenantAsync("valid-slug", "admin@test.com", Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal("Invalid plan selected", result.ErrorMessage);
    }

    /// <summary>No-op IPublishEndpoint for testing without MassTransit container.</summary>
    private class NullPublishEndpoint : IPublishEndpoint
    {
        public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => new NullConnectHandle();
        public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish(object message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        private class NullConnectHandle : ConnectHandle { public void Disconnect() { } public void Dispose() { } }
    }
}
