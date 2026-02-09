using Microsoft.AspNetCore.Mvc;
using Swap.Htmx;

namespace saas.Controllers;

public class HomeController : SwapController
{
    public IActionResult Index()
    {
        return Redirect("/");
    }
}
