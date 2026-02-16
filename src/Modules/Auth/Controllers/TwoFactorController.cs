using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using saas.Modules.Auth.Entities;
using saas.Modules.Auth.Services;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Auth.Controllers;

[Route("{slug}/profile/two-factor")]
[Authorize(Policy = "TenantUser")]
public class TwoFactorController : SwapController
{
    private readonly UserManager<AppUser> _userManager;
    private readonly ICurrentUser _currentUser;
    private readonly TwoFactorService _twoFactorService;
    private readonly IOptions<SiteSettings> _siteSettings;

    public TwoFactorController(
        UserManager<AppUser> userManager,
        ICurrentUser currentUser,
        TwoFactorService twoFactorService,
        IOptions<SiteSettings> siteSettings)
    {
        _userManager = userManager;
        _currentUser = currentUser;
        _twoFactorService = twoFactorService;
        _siteSettings = siteSettings;
    }

    [HttpGet("")]
    public async Task<IActionResult> Setup([FromRoute] string slug)
    {
        var user = await _userManager.FindByIdAsync(_currentUser.UserId!);
        if (user is null) return NotFound();

        if (user.IsTwoFactorEnabled)
        {
            ViewData["Title"] = "Two-Factor Authentication";
            return SwapView("TwoFactorManage", user);
        }

        var issuer = _siteSettings.Value.Name;
        var result = await _twoFactorService.GenerateSetupAsync(user, issuer);

        ViewData["Title"] = "Set Up Two-Factor Authentication";
        ViewData["Secret"] = result.Secret;
        ViewData["OtpUri"] = result.OtpUri;
        return SwapView("TwoFactorSetup", user);
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromRoute] string slug, [FromForm] string code)
    {
        var user = await _userManager.FindByIdAsync(_currentUser.UserId!);
        if (user is null) return NotFound();

        var result = await _twoFactorService.VerifyAndEnableAsync(user, code);

        if (!result.Success)
        {
            ViewData["Error"] = result.Error;
            ViewData["Secret"] = user.TwoFactorSecret;
            ViewData["OtpUri"] = $"otpauth://totp/{Uri.EscapeDataString(_siteSettings.Value.Name)}:{Uri.EscapeDataString(user.Email!)}?secret={user.TwoFactorSecret}&issuer={Uri.EscapeDataString(_siteSettings.Value.Name)}&digits=6&period=30";
            return SwapView("TwoFactorSetup", user);
        }

        ViewData["RecoveryCodes"] = result.RecoveryCodes;
        return SwapView("TwoFactorRecoveryCodes", user);
    }

    [HttpPost("disable")]
    public async Task<IActionResult> Disable([FromRoute] string slug)
    {
        var user = await _userManager.FindByIdAsync(_currentUser.UserId!);
        if (user is null) return NotFound();

        await _twoFactorService.DisableAsync(user);

        ViewData["Success"] = "Two-factor authentication has been disabled.";
        return SwapView("Profile", user);
    }
}
