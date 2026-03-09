using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using saas.Data.Tenant;
using saas.Infrastructure.Middleware;
using saas.Modules.Auth;
using saas.Modules.Auth.Entities;
using saas.Modules.Auth.Services;
using saas.Shared;
using Xunit;

namespace saas.Tests.Infrastructure;

public class CurrentUserMiddlewareTests
{
    [Fact]
    public async Task RefreshesTenantClaims_WhenRolePermissionsChangedAfterLogin()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var tenantDb = new TenantDbContext(options);
        await tenantDb.Database.EnsureCreatedAsync();

        var role = new AppRole
        {
            Id = "role-member",
            Name = "Member",
            NormalizedName = "MEMBER",
            IsSystemRole = true
        };

        var inventoryPermission = new Permission
        {
            Id = Guid.NewGuid(),
            Key = "inventory.read",
            Name = "View Inventory",
            Group = "Inventory",
            SortOrder = 0
        };

        var user = new AppUser
        {
            Id = "user-1",
            UserName = "member@example.test",
            NormalizedUserName = "MEMBER@EXAMPLE.TEST",
            Email = "member@example.test",
            NormalizedEmail = "MEMBER@EXAMPLE.TEST",
            EmailConfirmed = true,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString("N")
        };

        var sessionId = Guid.NewGuid();

        tenantDb.Roles.Add(role);
        tenantDb.Users.Add(user);
        tenantDb.Permissions.Add(inventoryPermission);
        tenantDb.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<string>
        {
            UserId = user.Id,
            RoleId = role.Id
        });
        tenantDb.RolePermissions.Add(new RolePermission
        {
            RoleId = role.Id,
            PermissionId = inventoryPermission.Id
        });
        tenantDb.UserSessions.Add(new UserSession
        {
            Id = sessionId,
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        });
        await tenantDb.SaveChangesAsync();

        var currentUser = new CurrentUser();
        var tenantContext = new FakeTenantContext
        {
            IsTenantRequest = true,
            Slug = "demo"
        };
        var authService = new FakeAuthenticationService();

        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(currentUser);
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(tenantDb);
        services.AddSingleton<IAuthenticationService>(authService);
        var provider = services.BuildServiceProvider();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Role, "Member"),
            new(AuthClaims.TenantSlug, "demo"),
            new(AuthClaims.SessionId, sessionId.ToString())
        };

        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSchemes.Tenant))
        };

        var called = false;
        var middleware = new CurrentUserMiddleware(
            _ =>
            {
                called = true;
                return Task.CompletedTask;
            },
            NullLogger<CurrentUserMiddleware>.Instance);

        await middleware.InvokeAsync(httpContext, currentUser, tenantContext);

        Assert.True(called);
        Assert.True(currentUser.HasPermission("inventory.read"));
        Assert.NotNull(authService.SignedInPrincipal);
        Assert.Contains(
            authService.SignedInPrincipal!.Claims,
            claim => claim.Type == AuthClaims.Permission && claim.Value == "inventory.read");
    }

    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        public ClaimsPrincipal? SignedInPrincipal { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
            => Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
        {
            SignedInPrincipal = principal;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public string? Slug { get; set; }
        public Guid? TenantId { get; set; }
        public string? PlanSlug { get; set; }
        public string? TenantName { get; set; }
        public bool IsTenantRequest { get; set; }
    }
}
