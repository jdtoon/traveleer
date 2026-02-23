using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using saas.Data.Core;
using saas.Modules.Auth.Services;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Auth.Controllers;

[Route("super-admin/two-factor")]
[Authorize(Policy = "SuperAdmin")]
public class SuperAdminTwoFactorController : SwapController
{
    private readonly CoreDbContext _coreDb;
    private readonly SuperAdminTwoFactorService _twoFactorService;
    private readonly IOptions<SiteSettings> _siteSettings;

    public SuperAdminTwoFactorController(
        CoreDbContext coreDb,
        SuperAdminTwoFactorService twoFactorService,
        IOptions<SiteSettings> siteSettings)
    {
        _coreDb = coreDb;
        _twoFactorService = twoFactorService;
        _siteSettings = siteSettings;
    }

    private async Task<Modules.SuperAdmin.Entities.SuperAdmin?> GetCurrentAdminAsync()
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (adminId is null || !Guid.TryParse(adminId, out var id)) return null;
        return await _coreDb.SuperAdmins.FindAsync(id);
    }

    [HttpGet("")]
    public async Task<IActionResult> Setup()
    {
        var admin = await GetCurrentAdminAsync();
        if (admin is null) return NotFound();

        if (admin.IsTwoFactorEnabled)
        {
            ViewData["Title"] = "Two-Factor Authentication";
            ViewData["AdminEmail"] = admin.Email;
            return SwapView("SuperAdminTwoFactorManage");
        }

        var issuer = _siteSettings.Value.Name + " Admin";
        var result = await _twoFactorService.GenerateSetupAsync(admin, issuer);

        ViewData["Title"] = "Set Up Two-Factor Authentication";
        ViewData["Secret"] = result.Secret;
        ViewData["OtpUri"] = result.OtpUri;
        ViewData["AdminEmail"] = admin.Email;
        return SwapView("SuperAdminTwoFactorSetup");
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromForm] string code)
    {
        var admin = await GetCurrentAdminAsync();
        if (admin is null) return NotFound();

        var result = await _twoFactorService.VerifyAndEnableAsync(admin, code);

        if (!result.Success)
        {
            var issuer = _siteSettings.Value.Name + " Admin";
            ViewData["Error"] = result.Error;
            ViewData["Secret"] = admin.TwoFactorSecret;
            ViewData["OtpUri"] = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(admin.Email)}?secret={admin.TwoFactorSecret}&issuer={Uri.EscapeDataString(issuer)}&digits=6&period=30";
            ViewData["AdminEmail"] = admin.Email;
            return SwapView("SuperAdminTwoFactorSetup");
        }

        ViewData["RecoveryCodes"] = result.RecoveryCodes;
        return SwapView("SuperAdminTwoFactorRecoveryCodes");
    }

    [HttpPost("disable")]
    public async Task<IActionResult> Disable()
    {
        var admin = await GetCurrentAdminAsync();
        if (admin is null) return NotFound();

        await _twoFactorService.DisableAsync(admin);

        ViewData["Success"] = "Two-factor authentication has been disabled.";
        ViewData["AdminEmail"] = admin.Email;
        return SwapView("SuperAdminTwoFactorDisabled");
    }
}
