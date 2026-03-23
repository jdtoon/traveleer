using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Controllers;
using Xunit;

namespace saas.Tests.Modules.Auth;

public class AuthRedirectsTests
{
    [Fact]
    public void Redirect_WithHtmxRequest_ReturnsStatus200AndHxRedirectHeader()
    {
        var controller = new TestController();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.Request.Headers["HX-Request"] = "true";

        var result = AuthRedirects.Redirect(controller, "/demo");

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status200OK, status.StatusCode);
        Assert.Equal("/demo", controller.Response.Headers["HX-Redirect"].ToString());
    }

    [Fact]
    public void Redirect_WithoutHtmxRequest_ReturnsStandardRedirectResult()
    {
        var controller = new TestController();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = AuthRedirects.Redirect(controller, "/demo");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/demo", redirect.Url);
        Assert.False(controller.Response.Headers.ContainsKey("HX-Redirect"));
    }

    private sealed class TestController : Controller
    {
    }
}
