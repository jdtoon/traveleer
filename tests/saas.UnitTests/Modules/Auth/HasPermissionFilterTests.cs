using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using saas.Modules.Auth.Filters;
using saas.Shared;
using Xunit;

namespace saas.Tests.Modules.Auth;

public class HasPermissionFilterTests
{
    private class FakeCurrentUser : ICurrentUser
    {
        public string? UserId => "1";
        public string? Email => "test@test.com";
        public string? DisplayName => "Test";
        public bool IsAuthenticated => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => Array.Empty<string>();
        public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
        public bool HasPermission(string permission) => Permissions.Contains(permission);
        public bool HasAnyPermission(params string[] permissions) => permissions.Any(p => HasPermission(p));
    }

    [Fact]
    public async Task Denies_WhenPermissionMissing()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser());
        var provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var actionContext = new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        var context = new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());

        var attr = new HasPermissionAttribute("notes.read");
        await attr.OnAuthorizationAsync(context);

        Assert.IsType<ForbidResult>(context.Result);
    }

    [Fact]
    public async Task Allows_WhenPermissionPresent()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser { Permissions = new[] { "notes.read" } });
        var provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var actionContext = new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        var context = new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());

        var attr = new HasPermissionAttribute("notes.read");
        await attr.OnAuthorizationAsync(context);

        Assert.Null(context.Result);
    }
}
