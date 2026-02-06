using System.Security.Claims;
using saas.Modules.Auth;
using saas.Modules.Auth.Services;
using Xunit;

namespace saas.Tests;

public class CurrentUserTests
{
    [Fact]
    public void ReadsClaimsCorrectly()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "user-1"),
            new(ClaimTypes.Email, "user@test.com"),
            new(ClaimTypes.Name, "User"),
            new(ClaimTypes.Role, "Admin"),
            new(AuthClaims.Permission, "notes.read"),
            new(AuthClaims.IsSuperAdmin, "true")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var current = new CurrentUser();
        current.SetFromClaims(principal, true);

        Assert.True(current.IsAuthenticated);
        Assert.True(current.IsSuperAdmin);
        Assert.Equal("user-1", current.UserId);
        Assert.Equal("user@test.com", current.Email);
        Assert.Contains("Admin", current.Roles);
        Assert.Contains("notes.read", current.Permissions);
        Assert.True(current.HasPermission("notes.read"));
    }
}
