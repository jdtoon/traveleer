using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using saas.Data.Core;
using saas.Data.Tenant;
using saas.Infrastructure.Provisioning;
using saas.Infrastructure.Services;
using saas.Shared;
using Xunit;

namespace saas.Tests;

[Trait("Category", "Integration")]
public class TenantProvisionerTests : IAsyncLifetime
{
    // TODO: Fix InMemory provider version compatibility issue
    // Currently getting: System.MissingMethodException: Method not found: 
    // 'System.String Microsoft.EntityFrameworkCore.Diagnostics.AbstractionsStrings.ArgumentIsEmpty(System.Object)'.
    private CoreDbContext _coreDb = null!;
    private ServiceProvider _serviceProvider = null!;
    private string _testDbPath = null!;

    public async Task InitializeAsync()
    {
        // Create in-memory Core database for testing
        var coreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase($"CoreDb_TenantProvisionerTests_{Guid.NewGuid()}")
            .Options;
        _coreDb = new CoreDbContext(coreOptions);

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

        // Setup service provider with all dependencies
        var services = new ServiceCollection();
        
        // Add logging
        services.AddLogging(builder => builder.AddConsole());
        
        // Add Core database
        services.AddSingleton(_coreDb);
        
        // Add Tenant database context (with factory pattern)
        services.AddDbContext<TenantDbContext>((sp, opts) =>
        {
            opts.UseSqlite($"Data Source={_testDbPath}");
        });
        
        // Add Identity
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
        
        // Add mock services
        services.AddSingleton<IEmailService, ConsoleEmailService>();
        services.AddSingleton<IBotProtection, MockBotProtection>();
        
        // Add provisioner service
        services.AddScoped<ITenantProvisioner, TenantProvisionerService>();
        
        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _coreDb.DisposeAsync();
        await _serviceProvider.DisposeAsync();
        
        // Clean up test database files
        if (!string.IsNullOrEmpty(_testDbPath) && File.Exists(_testDbPath))
        {
            try
            {
                File.Delete(_testDbPath);
            }
            catch { /* Ignore cleanup errors */ }
        }
    }

    [Fact(Skip = "InMemory provider version compatibility issue")]
    public async Task ValidateSlugAsync_ValidSlug_ReturnsValid()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioner>();

        // Act
        var result = await provisioner.ValidateSlugAsync("valid-slug-123");

        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Theory(Skip = "InMemory provider version compatibility issue")]
    [InlineData("ab", "Slug must be at least 3 characters")]
    [InlineData("", "Slug is required")]
    [InlineData("slug-", "Slug must contain only lowercase letters, numbers, and hyphens, and must start and end with a letter or number")]
    [InlineData("-slug", "Slug must contain only lowercase letters, numbers, and hyphens, and must start and end with a letter or number")]
    [InlineData("UPPERCASE", "Slug must contain only lowercase letters, numbers, and hyphens, and must start and end with a letter or number")]
    [InlineData("slug_with_underscore", "Slug must contain only lowercase letters, numbers, and hyphens, and must start and end with a letter or number")]
    [InlineData("super-admin", "This slug is reserved and cannot be used")]
    [InlineData("admin", "This slug is reserved and cannot be used")]
    [InlineData("api", "This slug is reserved and cannot be used")]
    public async Task ValidateSlugAsync_InvalidSlug_ReturnsError(string slug, string expectedError)
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioner>();

        // Act
        var result = await provisioner.ValidateSlugAsync(slug);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(expectedError, result.ErrorMessage);
    }

    [Fact(Skip = "InMemory provider version compatibility issue")]
    public async Task ValidateSlugAsync_DuplicateSlug_ReturnsError()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioner>();

        // Create existing tenant
        var existingTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Slug = "existing-slug",
            Name = "Existing",
            PlanId = _coreDb.Plans.First().Id,
            Status = TenantStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _coreDb.Tenants.Add(existingTenant);
        await _coreDb.SaveChangesAsync();

        // Act
        var result = await provisioner.ValidateSlugAsync("existing-slug");

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("This slug is already taken", result.ErrorMessage);
    }

    [Fact(Skip = "InMemory provider version compatibility issue")]
    public async Task ProvisionTenantAsync_ValidData_CreatesTenantAndDatabase()
    {
        // Arrange
        var slug = $"test-tenant-{Guid.NewGuid().ToString()[..8]}";
        var email = "admin@test.com";
        var planId = _coreDb.Plans.First().Id;
        
        _testDbPath = Path.Combine("db", "tenants", $"{slug}.db");
        
        using var scope = _serviceProvider.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioner>();

        // Act
        var result = await provisioner.ProvisionTenantAsync(slug, email, planId);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.TenantId);
        Assert.Null(result.ErrorMessage);

        // Verify tenant in Core database
        var tenant = await _coreDb.Tenants.FindAsync(result.TenantId);
        Assert.NotNull(tenant);
        Assert.Equal(slug, tenant!.Slug);
        Assert.Equal(TenantStatus.Active, tenant.Status);

        // Verify subscription created
        var subscription = await _coreDb.Subscriptions
            .FirstOrDefaultAsync(s => s.TenantId == result.TenantId);
        Assert.NotNull(subscription);
        Assert.Equal(SubscriptionStatus.Trialing, subscription!.Status);
        Assert.NotNull(subscription.NextBillingDate);

        // Verify tenant database file created
        Assert.True(File.Exists(_testDbPath));

        // Verify admin user created in tenant database
        var tenantDbOptions = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_testDbPath}")
            .Options;
        using var tenantDb = new TenantDbContext(tenantDbOptions);
        
        var adminUser = await tenantDb.Users.FirstOrDefaultAsync(u => u.Email == email);
        Assert.NotNull(adminUser);
        Assert.Equal(email, adminUser!.UserName);
        Assert.True(adminUser.EmailConfirmed);

        // Verify admin role assigned
        var userRoles = await tenantDb.UserRoles
            .Where(ur => ur.UserId == adminUser.Id)
            .ToListAsync();
        Assert.NotEmpty(userRoles);
    }

    [Fact(Skip = "InMemory provider version compatibility issue")]
    public async Task ProvisionTenantAsync_InvalidSlug_ReturnsError()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioner>();

        // Act
        var result = await provisioner.ProvisionTenantAsync("invalid_slug", "admin@test.com", _coreDb.Plans.First().Id);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.TenantId);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact(Skip = "InMemory provider version compatibility issue")]
    public async Task ProvisionTenantAsync_InvalidEmail_ReturnsError()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioner>();

        // Act
        var result = await provisioner.ProvisionTenantAsync("valid-slug", "not-an-email", _coreDb.Plans.First().Id);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Invalid email address", result.ErrorMessage);
    }

    [Fact(Skip = "InMemory provider version compatibility issue")]
    public async Task ProvisionTenantAsync_InvalidPlan_ReturnsError()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioner>();

        // Act
        var result = await provisioner.ProvisionTenantAsync("valid-slug", "admin@test.com", Guid.NewGuid());

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Invalid plan selected", result.ErrorMessage);
    }
}
