using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.Onboarding.DTOs;
using saas.Modules.Onboarding.Services;
using Swap.Htmx;

namespace saas.Modules.Onboarding.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(OnboardingFeatures.Onboarding)]
[Route("{slug}/onboarding")]
public class OnboardingController : SwapController
{
    private readonly IOnboardingService _service;

    public OnboardingController(IOnboardingService service)
    {
        _service = service;
    }

    [HttpGet("")]
    [HasPermission(OnboardingPermissions.OnboardingRead)]
    public async Task<IActionResult> Index([FromQuery] int? step = null)
        => SwapView(await _service.GetPageAsync(step));

    [HttpGet("step/{step:int}")]
    [HasPermission(OnboardingPermissions.OnboardingRead)]
    public async Task<IActionResult> Step(int step)
    {
        return step switch
        {
            1 => PartialView("_IdentityStep", await _service.GetIdentityStepAsync()),
            2 => PartialView("_DefaultsStep", await _service.GetDefaultsStepAsync()),
            _ => PartialView("_CompletionStep", await _service.GetCompletionStepAsync())
        };
    }

    [HttpPost("step/identity")]
    [ValidateAntiForgeryToken]
    [HasPermission(OnboardingPermissions.OnboardingEdit)]
    public async Task<IActionResult> SaveIdentity([FromForm] OnboardingIdentityStepDto dto)
    {
        if (!ModelState.IsValid)
        {
            return SwapResponse()
                .WithErrorToast("Please fix the errors below.")
                .WithView("Index", await _service.RehydrateIdentityPageAsync(dto))
                .Build();
        }

        await _service.SaveIdentityAsync(dto);
        return SwapResponse()
            .WithSuccessToast("Identity saved.")
            .WithView("Index", await _service.GetPageAsync(2))
            .Build();
    }

    [HttpPost("step/defaults")]
    [ValidateAntiForgeryToken]
    [HasPermission(OnboardingPermissions.OnboardingEdit)]
    public async Task<IActionResult> SaveDefaults([FromForm] OnboardingDefaultsStepDto dto)
    {
        if (!ModelState.IsValid)
        {
            return SwapResponse()
                .WithErrorToast("Please fix the errors below.")
                .WithView("Index", await _service.RehydrateDefaultsPageAsync(dto))
                .Build();
        }

        await _service.SaveDefaultsAsync(dto);
        return SwapResponse()
            .WithSuccessToast("Defaults saved.")
            .WithView("Index", await _service.GetPageAsync(3))
            .Build();
    }

    [HttpPost("complete")]
    [ValidateAntiForgeryToken]
    [HasPermission(OnboardingPermissions.OnboardingEdit)]
    public async Task<IActionResult> Complete()
    {
        try
        {
            await _service.CompleteAsync();
        }
        catch (InvalidOperationException ex)
        {
            return SwapResponse()
                .WithErrorToast(ex.Message)
                .WithView("Index", await _service.GetPageAsync())
                .Build();
        }

        return Redirect($"/{RouteData.Values["slug"]}");
    }

    [HttpPost("skip")]
    [ValidateAntiForgeryToken]
    [HasPermission(OnboardingPermissions.OnboardingEdit)]
    public async Task<IActionResult> Skip()
    {
        await _service.SkipAsync();
        return Redirect($"/{RouteData.Values["slug"]}");
    }
}
