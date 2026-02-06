using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Auth.Services;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Auth.Controllers;

[Route("{slug}")]
public class TenantAuthController : SwapController
{
    private readonly MagicLinkService _magicLinks;
    private readonly IEmailService _email;
    private readonly UserManager<AppUser> _userManager;

    public TenantAuthController(MagicLinkService magicLinks, IEmailService email, UserManager<AppUser> userManager)
    {
        _magicLinks = magicLinks;
        _email = email;
        _userManager = userManager;
    }

    [HttpGet("login")]
    public IActionResult Login([FromRoute] string slug)
    {
        ViewData["TenantSlug"] = slug;
        return SwapView("TenantLogin");
    }

    [HttpPost("login")]
    public async Task<IActionResult> LoginPost([FromRoute] string slug, [FromForm] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            ViewData["TenantSlug"] = slug;
            return SwapView("TenantLogin", model: "Email is required.");
        }

        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            ViewData["TenantSlug"] = slug;
            return SwapView("TenantLogin", model: "Email not found.");
        }

        var token = await _magicLinks.GenerateTokenAsync(email, slug);
        var callbackUrl = Url.Action("Verify", "TenantAuth", new { slug, token = token.Token }, Request.Scheme) ?? "/";
        await _email.SendMagicLinkAsync(email, callbackUrl);

        return SwapView("MagicLinkSent");
    }

    [HttpGet("verify")]
    public async Task<IActionResult> Verify([FromRoute] string slug, [FromQuery] string token)
    {
        var result = await _magicLinks.VerifyTokenAsync(token);
        if (!result.Success || result.Email is null || !string.Equals(result.TenantSlug, slug, StringComparison.OrdinalIgnoreCase))
            return SwapView("MagicLinkError", result.Error ?? "Invalid token");

        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Email == result.Email);
        if (user is null)
            return SwapView("MagicLinkError", "User not found");

        var roles = await _userManager.GetRolesAsync(user);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(AuthClaims.TenantSlug, slug)
        };

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var identity = new ClaimsIdentity(claims, AuthSchemes.Tenant);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(AuthSchemes.Tenant, principal);
        return Redirect($"/{slug}");
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromRoute] string slug)
    {
        await HttpContext.SignOutAsync(AuthSchemes.Tenant);
        return Redirect($"/{slug}/login");
    }
}
