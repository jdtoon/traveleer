using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Modules.Auth.Services;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Auth.Controllers;

[Route("super-admin")]
public class SuperAdminAuthController : SwapController
{
    private readonly CoreDbContext _coreDb;
    private readonly MagicLinkService _magicLinks;
    private readonly IEmailService _email;

    public SuperAdminAuthController(CoreDbContext coreDb, MagicLinkService magicLinks, IEmailService email)
    {
        _coreDb = coreDb;
        _magicLinks = magicLinks;
        _email = email;
    }

    [HttpGet("login")]
    public IActionResult Login()
    {
        return SwapView("SuperAdminLogin");
    }

    [HttpPost("login")]
    public async Task<IActionResult> LoginPost([FromForm] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return SwapView("SuperAdminLogin", model: "Email is required.");

        var admin = await _coreDb.SuperAdmins.FirstOrDefaultAsync(a => a.Email == email && a.IsActive);
        if (admin is null)
            return SwapView("SuperAdminLogin", model: "Email not found.");

        var token = await _magicLinks.GenerateTokenAsync(email);
        var callbackUrl = Url.Action("Verify", "SuperAdminAuth", new { token = token.Token }, Request.Scheme) ?? "/";
        await _email.SendMagicLinkAsync(email, callbackUrl);

        return SwapView("MagicLinkSent");
    }

    [HttpGet("verify")]
    public async Task<IActionResult> Verify([FromQuery] string token)
    {
        var result = await _magicLinks.VerifyTokenAsync(token);
        if (!result.Success || result.Email is null)
            return SwapView("MagicLinkError", result.Error ?? "Invalid token");

        var admin = await _coreDb.SuperAdmins.FirstOrDefaultAsync(a => a.Email == result.Email);
        if (admin is null)
            return SwapView("MagicLinkError", "Admin account not found");

        admin.LastLoginAt = DateTime.UtcNow;
        await _coreDb.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new(ClaimTypes.Email, admin.Email),
            new(AuthClaims.IsSuperAdmin, "true")
        };

        var identity = new ClaimsIdentity(claims, AuthSchemes.SuperAdmin);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(AuthSchemes.SuperAdmin, principal);
        return Redirect("/super-admin");
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthSchemes.SuperAdmin);
        return Redirect("/super-admin/login");
    }
}
