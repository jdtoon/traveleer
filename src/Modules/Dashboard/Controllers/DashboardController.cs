using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Onboarding.Services;
using Swap.Htmx;

namespace saas.Modules.Dashboard.Controllers;

[Authorize(Policy = "TenantUser")]
public class DashboardController : SwapController
{
    private readonly IOnboardingService _onboardingService;

    public DashboardController(IOnboardingService onboardingService)
    {
        _onboardingService = onboardingService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (await _onboardingService.ShouldRedirectToOnboardingAsync())
        {
            return RedirectToAction("Index", "Onboarding", new { slug = RouteData.Values["slug"] });
        }

        return SwapView();
    }
}
