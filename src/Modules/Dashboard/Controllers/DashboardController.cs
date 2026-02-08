using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swap.Htmx;

namespace saas.Modules.Dashboard.Controllers;

[Authorize(Policy = "TenantUser")]
public class DashboardController : SwapController
{
    [HttpGet]
    public IActionResult Index()
    {
        return SwapView();
    }
}
