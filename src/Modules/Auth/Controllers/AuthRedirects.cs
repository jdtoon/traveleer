using Microsoft.AspNetCore.Mvc;

namespace saas.Modules.Auth.Controllers;

public static class AuthRedirects
{
    public static IActionResult Redirect(Controller controller, string url)
    {
        if (controller.Request.Headers.ContainsKey("HX-Request"))
        {
            controller.Response.Headers["HX-Redirect"] = url;
            return new StatusCodeResult(StatusCodes.Status200OK);
        }

        return new RedirectResult(url);
    }
}