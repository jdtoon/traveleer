using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth;
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

    public async Task<IActionResult> Index()
    {
        // Tenant-scoped request: show the tenant dashboard (requires auth)
        if (_tenantContext.IsTenantRequest)
        {
            // Explicitly authenticate against the Tenant cookie scheme —
            // there is no default scheme, so User.Identity is unpopulated
            // unless an [Authorize] policy triggers it.
            var authResult = await HttpContext.AuthenticateAsync(AuthSchemes.Tenant);

            // Must be authenticated AND the cookie's slug claim must match this tenant
            if (!authResult.Succeeded)
                return Redirect($"/{_tenantContext.Slug}/login");

            var slugClaim = authResult.Principal?.FindFirst(AuthClaims.TenantSlug)?.Value;
            if (!string.Equals(slugClaim, _tenantContext.Slug, StringComparison.OrdinalIgnoreCase))
                return Redirect($"/{_tenantContext.Slug}/login");

            return SwapView("Dashboard");
        }

        // Public landing page
        return SwapView();
    }
}
