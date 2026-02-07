using Microsoft.AspNetCore.Mvc;
using saas.Shared;
using Swap.Htmx;

namespace saas.Controllers;

public class HomeController : SwapController
{
    private readonly ITenantContext _tenantContext;

    public HomeController(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public IActionResult Index()
    {
        // Tenant-scoped request: show the tenant dashboard (requires auth)
        if (_tenantContext.IsTenantRequest)
        {
            if (User.Identity?.IsAuthenticated != true)
                return Redirect($"/{_tenantContext.Slug}/login");

            return SwapView("Dashboard");
        }

        // Public landing page
        return SwapView();
    }
}
