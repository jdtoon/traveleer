using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
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
    private readonly IBotProtection _botProtection;
    private readonly SuperAdminTwoFactorService _twoFactorService;
    private readonly IDataProtector _twoFactorProtector;

    private const string TwoFactorPurpose = "SA-2FA-Challenge";

    public SuperAdminAuthController(
        CoreDbContext coreDb,
        MagicLinkService magicLinks,
        IEmailService email,
        IBotProtection botProtection,
        SuperAdminTwoFactorService twoFactorService,
        IDataProtectionProvider dataProtection)
    {
        _coreDb = coreDb;
        _magicLinks = magicLinks;
        _email = email;
        _botProtection = botProtection;
        _twoFactorService = twoFactorService;
        _twoFactorProtector = dataProtection.CreateProtector(TwoFactorPurpose);
    }

    [HttpGet("login")]
    public IActionResult Login()
    {
        return SwapView(SwapViews.SuperAdminAuth.SuperAdminLogin);
    }

    [HttpPost("login")]
    public async Task<IActionResult> LoginPost([FromForm] string email, [FromForm] string? captchaToken)
    {
        if (!await _botProtection.ValidateAsync(captchaToken))
            return SwapView(SwapViews.SuperAdminAuth.SuperAdminLogin, model: "Bot verification failed. Please try again.");

        if (string.IsNullOrWhiteSpace(email))
            return SwapView(SwapViews.SuperAdminAuth.SuperAdminLogin, model: "Email is required.");

        var admin = await _coreDb.SuperAdmins.FirstOrDefaultAsync(a => a.Email == email && a.IsActive);
        if (admin is null)
            return SwapView(SwapViews.SuperAdminAuth.SuperAdminLogin, model: "Invalid credentials.");

        var token = await _magicLinks.GenerateTokenAsync(email);
        var callbackUrl = Url.Action("Verify", "SuperAdminAuth", new { token = token.Token }, Request.Scheme) ?? "/";
        await _email.SendMagicLinkAsync(email, callbackUrl);

        return SwapView(SwapViews.Shared.MagicLinkSent);
    }

    [HttpGet("verify")]
    public async Task<IActionResult> Verify([FromQuery] string token)
    {
        var result = await _magicLinks.VerifyTokenAsync(token);
        if (!result.Success || result.Email is null)
            return SwapView(SwapViews.Shared.MagicLinkError, result.Error ?? "Invalid token");

        var admin = await _coreDb.SuperAdmins.FirstOrDefaultAsync(a => a.Email == result.Email && a.IsActive);
        if (admin is null)
            return SwapView(SwapViews.Shared.MagicLinkError, "Admin account not found");

        admin.LastLoginAt = DateTime.UtcNow;
        await _coreDb.SaveChangesAsync();

        // If 2FA is enabled, redirect to challenge instead of completing login
        if (admin.IsTwoFactorEnabled)
        {
            var payload = $"{admin.Id}|{DateTime.UtcNow.AddMinutes(5):O}";
            var challengeToken = _twoFactorProtector.Protect(payload);
            return Redirect($"/super-admin/two-factor-challenge?t={Uri.EscapeDataString(challengeToken)}");
        }

        await SignInAdminAsync(admin);
        return Redirect("/super-admin");
    }

    // ── Two-Factor Challenge ────────────────────────────────────────────────

    private (string AdminId, DateTime Expiry)? ValidateChallengeToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            var payload = _twoFactorProtector.Unprotect(token);
            var parts = payload.Split('|', 2);
            if (parts.Length != 2) return null;
            if (!DateTime.TryParse(parts[1], null, System.Globalization.DateTimeStyles.RoundtripKind, out var expiry)) return null;
            if (expiry < DateTime.UtcNow) return null;
            return (parts[0], expiry);
        }
        catch
        {
            return null;
        }
    }

    [HttpGet("two-factor-challenge")]
    public IActionResult TwoFactorChallenge([FromQuery] string? t)
    {
        var parsed = ValidateChallengeToken(t);
        if (parsed is null)
            return Redirect("/super-admin/login");

        ViewData["ChallengeToken"] = t;
        return SwapView(SwapViews.SuperAdminAuth.SuperAdminTwoFactorChallenge);
    }

    [HttpPost("two-factor-challenge")]
    public async Task<IActionResult> TwoFactorChallengePost([FromForm] string? code, [FromForm] string? recoveryCode, [FromForm] string? challengeToken)
    {
        var parsed = ValidateChallengeToken(challengeToken);
        if (parsed is null)
            return Redirect("/super-admin/login");

        var admin = await _coreDb.SuperAdmins.FindAsync(Guid.Parse(parsed.Value.AdminId));
        if (admin is null || !admin.IsActive)
            return SwapView(SwapViews.Shared.MagicLinkError, "Session expired. Please request a new magic link.");

        bool isValid = false;
        if (!string.IsNullOrWhiteSpace(code))
        {
            isValid = _twoFactorService.ValidateCode(admin.TwoFactorSecret!, code);
        }
        else if (!string.IsNullOrWhiteSpace(recoveryCode))
        {
            isValid = await _twoFactorService.ValidateRecoveryCodeAsync(admin, recoveryCode);
        }

        if (!isValid)
        {
            ViewData["ChallengeToken"] = challengeToken;
            ViewData["Error"] = "Invalid code. Please try again.";
            return SwapView(SwapViews.SuperAdminAuth.SuperAdminTwoFactorChallenge);
        }

        await SignInAdminAsync(admin);
        return Redirect("/super-admin");
    }

    private async Task SignInAdminAsync(Modules.SuperAdmin.Entities.SuperAdmin admin)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new(ClaimTypes.Email, admin.Email),
            new(AuthClaims.IsSuperAdmin, "true")
        };

        var identity = new ClaimsIdentity(claims, AuthSchemes.SuperAdmin);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(AuthSchemes.SuperAdmin, principal);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthSchemes.SuperAdmin);
        return Redirect("/super-admin/login");
    }

    [HttpGet("logout")]
    public async Task<IActionResult> LogoutGet()
    {
        await HttpContext.SignOutAsync(AuthSchemes.SuperAdmin);
        return Redirect("/super-admin/login");
    }
}
