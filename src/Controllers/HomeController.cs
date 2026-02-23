using Microsoft.AspNetCore.Mvc;
using Swap.Htmx;

namespace saas.Controllers;

public class HomeController : SwapController
{
    public IActionResult Index()
    {
        return Redirect("/");
    }

    [Route("/Home/Error")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error([FromQuery] int? statusCode)
    {
        if (statusCode == 404)
        {
            Response.StatusCode = 404;
            return View("NotFound");
        }

        return View();
    }
}
