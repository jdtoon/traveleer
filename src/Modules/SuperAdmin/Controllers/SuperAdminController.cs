using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swap.Htmx;

namespace saas.Modules.SuperAdmin.Controllers;

[Authorize(Policy = "SuperAdmin")]
public class SuperAdminController : SwapController
{
    [HttpGet("/super-admin")]
    public IActionResult Index()
    {
        return SwapView();
    }
}
