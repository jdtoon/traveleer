using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using saas.Infrastructure.Middleware;
using Xunit;

namespace saas.Tests.Infrastructure;

public class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_SetsTenantCsp_WithHttpsImagesAllowed()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/demo/branding";

        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask, new ConfigurationBuilder().Build());

        await middleware.InvokeAsync(context);

        var csp = context.Response.Headers["Content-Security-Policy"].ToString();
        Assert.Contains("img-src 'self' data: https:", csp);
        Assert.Contains("frame-src https://challenges.cloudflare.com", csp);
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
    }

    [Fact]
    public async Task InvokeAsync_SetsSuperAdminCsp_WithConfiguredFrameSources()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/super-admin/observability";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Infrastructure:SeqUrl"] = "https://seq.example.com",
                ["Infrastructure:RabbitMqManagementUrl"] = "https://rabbit.example.com"
            })
            .Build();

        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask, configuration);

        await middleware.InvokeAsync(context);

        var csp = context.Response.Headers["Content-Security-Policy"].ToString();
        Assert.Contains("img-src 'self' data: https:", csp);
        Assert.Contains("frame-src 'self' https://challenges.cloudflare.com https://seq.example.com https://rabbit.example.com", csp);
        Assert.Equal("SAMEORIGIN", context.Response.Headers["X-Frame-Options"].ToString());
    }
}