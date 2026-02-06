using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace saas.Controllers;

[Authorize(Policy = "SuperAdmin")]
public class SuperAdminController : Controller
{
    [HttpGet("/super-admin")]
    public IActionResult Index()
    {
        return View();
    }
}
