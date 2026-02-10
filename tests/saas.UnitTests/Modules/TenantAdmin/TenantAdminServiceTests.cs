using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using saas.Data.Tenant;
using saas.Modules.TenantAdmin.Services;
using saas.Shared;
using Xunit;

namespace saas.Tests.Modules.TenantAdmin;

public class TenantAdminServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _serviceProvider = null!;
    private TenantAdminService _service = null!;
    private TenantDbContext _db = null!;
    private UserManager<AppUser> _userManager = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

        services.AddDbContext<TenantDbContext>(opts =>
            opts.UseSqlite(_connection));

        services.AddIdentityCore<AppUser>(opts =>
        {
            opts.User.RequireUniqueEmail = true;
        })
        .AddRoles<AppRole>()
        .AddEntityFrameworkStores<TenantDbContext>();

        services.AddSingleton<ITenantContext>(new FakeTenantContext("testcorp"));
        services.AddSingleton<IEmailService>(new FakeEmailService());

        _serviceProvider = services.BuildServiceProvider();

        // Create schema
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        // Resolve services from a scope
        var mainScope = _serviceProvider.CreateScope();
        _db = mainScope.ServiceProvider.GetRequiredService<TenantDbContext>();
        _userManager = mainScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var emailService = mainScope.ServiceProvider.GetRequiredService<IEmailService>();
        var tenantContext = mainScope.ServiceProvider.GetRequiredService<ITenantContext>();

        _service = new TenantAdminService(_db, _userManager, emailService, tenantContext);

        // Seed an admin user
        var admin = new AppUser
        {
            UserName = "admin@testcorp.com",
            Email = "admin@testcorp.com",
            EmailConfirmed = true,
            IsActive = true,
            DisplayName = "Admin"
        };
        await _userManager.CreateAsync(admin);

        // Seed a role
        var adminRole = new AppRole
        {
            Name = "Admin",
            NormalizedName = "ADMIN",
            Description = "Administrator",
            IsSystemRole = true
        };
        _db.Roles.Add(adminRole);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
        SqliteConnection.ClearAllPools();
    }

    // ── Users ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUsersAsync_ReturnsSeededUser()
    {
        var users = await _service.GetUsersAsync();

        Assert.Single(users.Items);
        Assert.Equal("admin@testcorp.com", users.Items[0].Email);
        Assert.Equal("Admin", users.Items[0].DisplayName);
        Assert.True(users.Items[0].IsActive);
    }

    [Fact]
    public async Task InviteUserAsync_CreatesNewUser()
    {
        var success = await _service.InviteUserAsync("new@testcorp.com");

        Assert.True(success);
        var users = await _service.GetUsersAsync();
        Assert.Equal(2, users.Items.Count);
        Assert.Contains(users.Items, u => u.Email == "new@testcorp.com");
    }

    [Fact]
    public async Task InviteUserAsync_SendsEmail()
    {
        var emailService = _serviceProvider.GetRequiredService<IEmailService>() as FakeEmailService;

        await _service.InviteUserAsync("invited@testcorp.com");

        Assert.Single(emailService!.SentLinks);
        Assert.Contains("invited@testcorp.com", emailService.SentLinks[0].to);
    }

    [Fact]
    public async Task InviteUserAsync_DuplicateEmail_ReturnsFalse()
    {
        var success = await _service.InviteUserAsync("admin@testcorp.com");

        Assert.False(success);
    }

    [Fact]
    public async Task DeactivateUserAsync_SetsIsActiveFalse()
    {
        var user = await _userManager.FindByEmailAsync("admin@testcorp.com");

        var success = await _service.DeactivateUserAsync(user!.Id);

        Assert.True(success);
        // Reload
        await _db.Entry(user).ReloadAsync();
        Assert.False(user.IsActive);
    }

    [Fact]
    public async Task ActivateUserAsync_SetsIsActiveTrue()
    {
        var user = await _userManager.FindByEmailAsync("admin@testcorp.com");
        await _service.DeactivateUserAsync(user!.Id);

        var success = await _service.ActivateUserAsync(user.Id);

        Assert.True(success);
        await _db.Entry(user).ReloadAsync();
        Assert.True(user.IsActive);
    }

    // ── Roles ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRolesAsync_ReturnsSeededRole()
    {
        var roles = await _service.GetRolesAsync();

        Assert.Single(roles);
        Assert.Equal("Admin", roles[0].Name);
        Assert.True(roles[0].IsSystemRole);
    }

    [Fact]
    public async Task AssignRoleAsync_AddsRoleToUser()
    {
        var user = await _userManager.FindByEmailAsync("admin@testcorp.com");
        var role = await _db.Roles.FirstAsync();

        var success = await _service.AssignRoleAsync(user!.Id, role.Id);

        Assert.True(success);
        var users = await _service.GetUsersAsync();
        Assert.Contains("Admin", users.Items[0].Roles);
    }

    [Fact]
    public async Task RemoveRoleAsync_RemovesRoleFromUser()
    {
        var user = await _userManager.FindByEmailAsync("admin@testcorp.com");
        var role = await _db.Roles.FirstAsync();

        // Assign first
        await _service.AssignRoleAsync(user!.Id, role.Id);
        // Then remove
        var success = await _service.RemoveRoleAsync(user.Id, role.Id);

        Assert.True(success);
        var users = await _service.GetUsersAsync();
        Assert.DoesNotContain("Admin", users.Items[0].Roles);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    private class FakeTenantContext : ITenantContext
    {
        public FakeTenantContext(string slug) => Slug = slug;
        public string? Slug { get; }
        public Guid? TenantId => Guid.NewGuid();
        public string? PlanSlug => "test";
        public string? TenantName => "Test Tenant";
        public bool IsTenantRequest => true;
    }

    private class FakeEmailService : IEmailService
    {
        public List<(string to, string url)> SentLinks { get; } = [];

        public Task SendAsync(EmailMessage message)
        {
            return Task.CompletedTask;
        }

        public Task SendMagicLinkAsync(string to, string magicLinkUrl)
        {
            SentLinks.Add((to, magicLinkUrl));
            return Task.CompletedTask;
        }
    }
}
