using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
    private readonly IPasswordHasher<Modules.SuperAdmin.Entities.SuperAdmin> _passwordHasher;
    private readonly AuthOptions _authOptions;

    private const string TwoFactorPurpose = "SA-2FA-Challenge";
    private const int PasswordResetTokenExpiryMinutes = 60;

    public SuperAdminAuthController(
        CoreDbContext coreDb,
        MagicLinkService magicLinks,
        IEmailService email,
        IBotProtection botProtection,
        SuperAdminTwoFactorService twoFactorService,
        IDataProtectionProvider dataProtection,
        IPasswordHasher<Modules.SuperAdmin.Entities.SuperAdmin> passwordHasher,
        IOptions<AuthOptions> authOptions)
    {
        _coreDb = coreDb;
        _magicLinks = magicLinks;
        _email = email;
        _botProtection = botProtection;
        _twoFactorService = twoFactorService;
        _twoFactorProtector = dataProtection.CreateProtector(TwoFactorPurpose);
        _passwordHasher = passwordHasher;
        _authOptions = authOptions.Value;
    }

    [HttpGet("login")]
    public IActionResult Login()
    {
        return _authOptions.IsPasswordLogin
            ? SwapView(SwapViews.SuperAdminAuth.SuperAdminPasswordLogin)
            : SwapView(SwapViews.SuperAdminAuth.SuperAdminLogin);
    }

    [HttpPost("login")]
    public async Task<IActionResult> LoginPost([FromForm] string email, [FromForm] string? captchaToken)
    {
        if (!_authOptions.IsMagicLinkLogin)
            return NotFound();

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

    // ── Password Login ─────────────────────────────────────────────────────

    [HttpPost("login/password")]
    public async Task<IActionResult> PasswordLoginPost([FromForm] string email, [FromForm] string password, [FromForm] string? captchaToken)
    {
        if (!_authOptions.IsPasswordLogin)
            return NotFound();

        if (!await _botProtection.ValidateAsync(captchaToken))
            return SwapView(SwapViews.SuperAdminAuth.SuperAdminPasswordLogin, model: "Bot verification failed. Please try again.");

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return SwapView(SwapViews.SuperAdminAuth.SuperAdminPasswordLogin, model: "Email and password are required.");

        var admin = await _coreDb.SuperAdmins.FirstOrDefaultAsync(a => a.Email == email && a.IsActive);
        if (admin is null || string.IsNullOrEmpty(admin.PasswordHash))
        {
            return SwapView(SwapViews.SuperAdminAuth.SuperAdminPasswordLogin, model: "Invalid email or password.");
        }

        var result = _passwordHasher.VerifyHashedPassword(admin, admin.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
            return SwapView(SwapViews.SuperAdminAuth.SuperAdminPasswordLogin, model: "Invalid email or password.");

        admin.LastLoginAt = DateTime.UtcNow;
        await _coreDb.SaveChangesAsync();

        if (admin.IsTwoFactorEnabled)
        {
            var payload = $"{admin.Id}|{DateTime.UtcNow.AddMinutes(5):O}";
            var challengeToken = _twoFactorProtector.Protect(payload);
            return Redirect($"/super-admin/two-factor-challenge?t={Uri.EscapeDataString(challengeToken)}");
        }

        await SignInAdminAsync(admin);
        return Redirect("/super-admin");
    }

    // ── Forgot Password ────────────────────────────────────────────────────

    [HttpGet("forgot-password")]
    public IActionResult ForgotPassword()
    {
        if (!_authOptions.IsPasswordLogin)
            return NotFound();

        return SwapView(SwapViews.SuperAdminAuth.SuperAdminForgotPassword);
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPasswordPost([FromForm] string email)
    {
        if (!_authOptions.IsPasswordLogin)
            return NotFound();

        var admin = await _coreDb.SuperAdmins.FirstOrDefaultAsync(a => a.Email == email && a.IsActive);
        if (admin is not null)
        {
            var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace("+", "-").Replace("/", "_").TrimEnd('=');
            var tokenHash = Convert.ToBase64String(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(rawToken)));

            admin.PasswordResetToken = tokenHash;
            admin.PasswordResetTokenExpiry = DateTime.UtcNow.AddMinutes(PasswordResetTokenExpiryMinutes);
            await _coreDb.SaveChangesAsync();

            var resetUrl = Url.Action("SuperAdminResetPassword", "SuperAdminAuth",
                new { email = admin.Email, token = rawToken },
                Request.Scheme) ?? "/super-admin/login";

            try { await _email.SendPasswordResetAsync(admin.Email, resetUrl); }
            catch { /* Don't block response if email fails */ }
        }

        return SwapView(SwapViews.Shared.PasswordResetSent);
    }

    // ── Reset Password ─────────────────────────────────────────────────────

    [HttpGet("reset-password")]
    public IActionResult SuperAdminResetPassword([FromQuery] string email, [FromQuery] string token)
    {
        if (!_authOptions.IsPasswordLogin)
            return NotFound();

        ViewData["Email"] = email;
        ViewData["Token"] = token;
        return SwapView(SwapViews.SuperAdminAuth.SuperAdminResetPassword);
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> SuperAdminResetPasswordPost(
        [FromForm] string email, [FromForm] string token,
        [FromForm] string newPassword, [FromForm] string confirmPassword)
    {
        if (!_authOptions.IsPasswordLogin)
            return NotFound();

        ViewData["Email"] = email;
        ViewData["Token"] = token;

        if (newPassword != confirmPassword)
            return SwapView(SwapViews.SuperAdminAuth.SuperAdminResetPassword, model: "Passwords do not match.");

        if (newPassword.Length < 8)
            return SwapView(SwapViews.SuperAdminAuth.SuperAdminResetPassword, model: "Password must be at least 8 characters.");

        var admin = await _coreDb.SuperAdmins.FirstOrDefaultAsync(a => a.Email == email && a.IsActive);
        if (admin is null || string.IsNullOrEmpty(admin.PasswordResetToken) || admin.PasswordResetTokenExpiry < DateTime.UtcNow)
            return SwapView(SwapViews.SuperAdminAuth.SuperAdminResetPassword, model: "This reset link has expired or is invalid. Please request a new one.");

        var tokenHash = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(token)));

        if (!string.Equals(tokenHash, admin.PasswordResetToken, StringComparison.Ordinal))
            return SwapView(SwapViews.SuperAdminAuth.SuperAdminResetPassword, model: "This reset link has expired or is invalid. Please request a new one.");

        admin.PasswordHash = _passwordHasher.HashPassword(admin, newPassword);
        admin.PasswordResetToken = null;
        admin.PasswordResetTokenExpiry = null;
        await _coreDb.SaveChangesAsync();

        return Redirect("/super-admin/login?passwordReset=true");
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
