using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Onboarding.Services;
using Swap.Htmx;

namespace saas.Modules.Dashboard.Controllers;

[Authorize(Policy = "TenantUser")]
public class DashboardController : SwapController
{
    private static readonly HashSet<string> ValidRanges = ["month", "quarter", "year"];

    private readonly IOnboardingService _onboardingService;

    public DashboardController(IOnboardingService onboardingService)
    {
        _onboardingService = onboardingService;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] string? range = null)
    {
        if (await _onboardingService.ShouldRedirectToOnboardingAsync())
        {
            return RedirectToAction("Index", "Onboarding", new { slug = RouteData.Values["slug"] });
        }

        ViewData["DashboardRange"] = !string.IsNullOrWhiteSpace(range) && ValidRanges.Contains(range)
            ? range
            : "month";

        return SwapView();
    }
}
