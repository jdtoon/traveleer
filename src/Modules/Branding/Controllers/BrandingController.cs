using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.Branding.DTOs;
using saas.Modules.Branding.Events;
using saas.Modules.Branding.Services;
using Swap.Htmx;

namespace saas.Modules.Branding.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(BrandingFeatures.Branding)]
[Route("{slug}/branding")]
public class BrandingController : SwapController
{
    private readonly IBrandingService _service;

    public BrandingController(IBrandingService service)
    {
        _service = service;
    }

    [HttpGet("")]
    [HasPermission(BrandingPermissions.BrandingRead)]
    public async Task<IActionResult> Index()
        => SwapView(await _service.GetSettingsAsync());

    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    [HasPermission(BrandingPermissions.BrandingEdit)]
    public async Task<IActionResult> Save([FromForm] BrandingSettingsDto dto)
    {
        if (!ModelState.IsValid)
        {
            dto = await RehydrateAsync(dto);
            return SwapResponse()
                .WithErrorToast("Please fix the errors below.")
                .WithView("Index", dto)
                .Build();
        }

        await _service.UpdateAsync(dto);
        var model = await _service.GetSettingsAsync();
        return SwapResponse()
            .WithSuccessToast("Branding updated.")
            .WithTrigger(BrandingEvents.Refresh)
            .WithView("Index", model)
            .Build();
    }

    [HttpGet("shell")]
    [HasPermission(BrandingPermissions.BrandingRead)]
    public async Task<IActionResult> Shell()
        => PartialView("_BrandShell", await _service.GetShellAsync());

    [HttpGet("theme-vars")]
    [HasPermission(BrandingPermissions.BrandingRead)]
    public async Task<IActionResult> ThemeVars()
        => PartialView("_ThemeVars", await _service.GetThemeAsync());

    private async Task<BrandingSettingsDto> RehydrateAsync(BrandingSettingsDto dto)
    {
        var current = await _service.GetSettingsAsync();
        dto.PreviewReferenceNumber = current.PreviewReferenceNumber;
        dto.EffectiveAgencyName = string.IsNullOrWhiteSpace(dto.AgencyName) ? current.EffectiveAgencyName : dto.AgencyName.Trim();
        dto.EffectiveContactEmail = string.IsNullOrWhiteSpace(dto.PublicContactEmail) ? current.EffectiveContactEmail : dto.PublicContactEmail.Trim();
        dto.PrimaryTextColor = current.PrimaryTextColor;
        dto.SecondaryTextColor = current.SecondaryTextColor;
        return dto;
    }
}
