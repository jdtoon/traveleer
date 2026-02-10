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
    public IActionResult Error()
    {
        return View();
    }
}
