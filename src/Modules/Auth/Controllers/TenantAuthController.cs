using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using MassTransit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using saas.Data.Tenant;
using saas.Modules.Auth.Entities;
using saas.Modules.Auth.Services;
using saas.Modules.Notifications.Services;
using saas.Shared;
using saas.Shared.Messages;
using Swap.Htmx;

namespace saas.Modules.Auth.Controllers;

[Route("{slug}")]
public class TenantAuthController : SwapController
{
    private readonly MagicLinkService _magicLinks;
    private readonly IEmailService _email;
    private readonly IBotProtection _botProtection;
    private readonly UserManager<AppUser> _userManager;
    private readonly TenantDbContext _tenantDb;
    private readonly INotificationService _notifications;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ITenantContext _tenantContext;
    private readonly TwoFactorService _twoFactorService;
    private readonly IDataProtector _twoFactorProtector;
    private readonly AuthOptions _authOptions;

    private const string TwoFactorPurpose = "2FA-Challenge";

    public TenantAuthController(
        MagicLinkService magicLinks, 
        IEmailService email,
        IBotProtection botProtection,
        UserManager<AppUser> userManager,
        TenantDbContext tenantDb,
        INotificationService notifications,
        IPublishEndpoint publishEndpoint,
        ITenantContext tenantContext,
        TwoFactorService twoFactorService,
        IDataProtectionProvider dataProtection,
        IOptions<AuthOptions> authOptions)
    {
        _magicLinks = magicLinks;
        _email = email;
        _botProtection = botProtection;
        _userManager = userManager;
        _tenantDb = tenantDb;
        _notifications = notifications;
        _publishEndpoint = publishEndpoint;
        _tenantContext = tenantContext;
        _twoFactorService = twoFactorService;
        _twoFactorProtector = dataProtection.CreateProtector(TwoFactorPurpose);
        _authOptions = authOptions.Value;
    }

    [HttpGet("login")]
    public IActionResult Login([FromRoute] string slug)
    {
        ViewData["TenantSlug"] = slug;
        return _authOptions.IsPasswordLogin
            ? SwapView(SwapViews.TenantAuth.TenantPasswordLogin)
            : SwapView(SwapViews.TenantAuth.TenantLogin);
    }

    [HttpPost("login")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> LoginPost([FromRoute] string slug, [FromForm] string email, [FromForm] string? captchaToken)
    {
        if (!_authOptions.IsMagicLinkLogin)
            return NotFound();

        if (!await _botProtection.ValidateAsync(captchaToken))
        {
            ViewData["TenantSlug"] = slug;
            return SwapView(SwapViews.TenantAuth.TenantLogin, model: "Bot verification failed. Please try again.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            ViewData["TenantSlug"] = slug;
            return SwapView(SwapViews.TenantAuth.TenantLogin, model: "Email is required.");
        }

        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || !user.IsActive)
        {
            // Always show the same message to prevent email enumeration
            return SwapView(SwapViews.Shared.MagicLinkSent);
        }

        var token = await _magicLinks.GenerateTokenAsync(email, slug);
        var callbackUrl = Url.Action("Verify", "TenantAuth", new { slug, token = token.Token }, Request.Scheme) ?? "/";
        await _email.SendMagicLinkAsync(email, callbackUrl);

        return SwapView(SwapViews.Shared.MagicLinkSent);
    }

    [HttpGet("verify")]
    public async Task<IActionResult> Verify([FromRoute] string slug, [FromQuery] string token)
    {
        var result = await _magicLinks.VerifyTokenAsync(token);
        if (!result.Success || result.Email is null || !string.Equals(result.TenantSlug, slug, StringComparison.OrdinalIgnoreCase))
            return SwapView(SwapViews.Shared.MagicLinkError, result.Error ?? "Invalid token");

        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Email == result.Email);
        if (user is null)
            return SwapView(SwapViews.Shared.MagicLinkError, "User not found");

        if (!user.IsActive)
            return SwapView(SwapViews.Shared.MagicLinkError, "This account has been deactivated. Please contact your administrator.");

        return await SignInOrChallengeTenantUserAsync(user, slug);
    }

    // ── Password Login ─────────────────────────────────────────────────────

    [HttpPost("login/password")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> PasswordLoginPost([FromRoute] string slug, [FromForm] string email, [FromForm] string password, [FromForm] string? captchaToken)
    {
        if (!_authOptions.IsPasswordLogin)
            return NotFound();

        if (!await _botProtection.ValidateAsync(captchaToken))
        {
            ViewData["TenantSlug"] = slug;
            return SwapView(SwapViews.TenantAuth.TenantPasswordLogin, model: "Bot verification failed. Please try again.");
        }

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            ViewData["TenantSlug"] = slug;
            return SwapView(SwapViews.TenantAuth.TenantPasswordLogin, model: "Email and password are required.");
        }

        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || !user.IsActive || !await _userManager.CheckPasswordAsync(user, password))
        {
            ViewData["TenantSlug"] = slug;
            return SwapView(SwapViews.TenantAuth.TenantPasswordLogin, model: "Invalid email or password.");
        }

        return await SignInOrChallengeTenantUserAsync(user, slug);
    }

    // ── Forgot Password ────────────────────────────────────────────────────

    [HttpGet("forgot-password")]
    public IActionResult ForgotPassword([FromRoute] string slug)
    {
        if (!_authOptions.IsPasswordLogin)
            return NotFound();

        ViewData["TenantSlug"] = slug;
        return SwapView(SwapViews.TenantAuth.ForgotPassword);
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> ForgotPasswordPost([FromRoute] string slug, [FromForm] string email)
    {
        if (!_authOptions.IsPasswordLogin)
            return NotFound();

        ViewData["TenantSlug"] = slug;

        // Always show the same view to prevent email enumeration
        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is not null && user.IsActive)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetUrl = Url.Action("ResetPassword", "TenantAuth",
                new { slug, email = user.Email, token },
                Request.Scheme) ?? "/";

            try { await _email.SendPasswordResetAsync(user.Email!, resetUrl); }
            catch { /* Don't block response if email fails */ }
        }

        return SwapView(SwapViews.Shared.PasswordResetSent);
    }

    // ── Reset Password ─────────────────────────────────────────────────────

    [HttpGet("reset-password")]
    public IActionResult ResetPassword([FromRoute] string slug, [FromQuery] string email, [FromQuery] string token)
    {
        if (!_authOptions.IsPasswordLogin)
            return NotFound();

        ViewData["TenantSlug"] = slug;
        ViewData["Email"] = email;
        ViewData["Token"] = token;
        return SwapView(SwapViews.TenantAuth.ResetPassword);
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPasswordPost([FromRoute] string slug,
        [FromForm] string email, [FromForm] string token,
        [FromForm] string newPassword, [FromForm] string confirmPassword)
    {
        if (!_authOptions.IsPasswordLogin)
            return NotFound();

        ViewData["TenantSlug"] = slug;
        ViewData["Email"] = email;
        ViewData["Token"] = token;

        if (newPassword != confirmPassword)
            return SwapView(SwapViews.TenantAuth.ResetPassword, model: "Passwords do not match.");

        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
            return SwapView(SwapViews.TenantAuth.ResetPassword, model: "Invalid request.");

        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            var error = result.Errors.FirstOrDefault()?.Description ?? "Failed to reset password.";
            return SwapView(SwapViews.TenantAuth.ResetPassword, model: error);
        }

        return AuthRedirects.Redirect(this, $"/{slug}/login?passwordReset=true");
    }

    // ── Two-Factor Challenge (unauthenticated) ─────────────────────────────

    /// <summary>
    /// Validates the signed 2FA challenge token from the query string.
    /// Returns (userId, slug) on success, or null on failure.
    /// </summary>
    private (string UserId, string Slug)? ValidateChallengeToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            var payload = _twoFactorProtector.Unprotect(token);
            var parts = payload.Split('|', 3);
            if (parts.Length != 3) return null;
            if (!DateTime.TryParse(parts[2], null, System.Globalization.DateTimeStyles.RoundtripKind, out var expiry)) return null;
            if (expiry < DateTime.UtcNow) return null;
            return (parts[0], parts[1]);
        }
        catch
        {
            return null;
        }
    }

    [HttpGet("two-factor-challenge")]
    public IActionResult TwoFactorChallenge([FromRoute] string slug, [FromQuery] string? t)
    {
        var parsed = ValidateChallengeToken(t);
        if (parsed is null || !string.Equals(parsed.Value.Slug, slug, StringComparison.OrdinalIgnoreCase))
            return AuthRedirects.Redirect(this, $"/{slug}/login");

        ViewData["TenantSlug"] = slug;
        ViewData["ChallengeToken"] = t;
        return SwapView(SwapViews.TenantAuth.TwoFactorChallenge);
    }

    [HttpPost("two-factor-challenge")]
    public async Task<IActionResult> TwoFactorChallengePost([FromRoute] string slug, [FromForm] string? code, [FromForm] string? recoveryCode, [FromForm] string? challengeToken)
    {
        var parsed = ValidateChallengeToken(challengeToken);
        if (parsed is null || !string.Equals(parsed.Value.Slug, slug, StringComparison.OrdinalIgnoreCase))
            return AuthRedirects.Redirect(this, $"/{slug}/login");

        var userId = parsed.Value.UserId;
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null || !user.IsActive)
            return SwapView(SwapViews.Shared.MagicLinkError, "Session expired. Please request a new magic link.");

        // Try TOTP code first, then recovery code
        bool isValid = false;
        if (!string.IsNullOrWhiteSpace(code))
        {
            isValid = _twoFactorService.ValidateCode(user.TwoFactorSecret!, code);
        }
        else if (!string.IsNullOrWhiteSpace(recoveryCode))
        {
            isValid = await _twoFactorService.ValidateRecoveryCodeAsync(user, recoveryCode);
        }

        if (!isValid)
        {
            ViewData["TenantSlug"] = slug;
            ViewData["ChallengeToken"] = challengeToken;
            ViewData["Error"] = "Invalid code. Please try again.";
            return SwapView(SwapViews.TenantAuth.TwoFactorChallenge);
        }

        // 2FA passed — complete the sign-in flow
        return await CompleteSignInAsync(user, slug);
    }

    // ── Shared Sign-In Helpers ─────────────────────────────────────────────

    /// <summary>
    /// Checks 2FA and either redirects to the challenge page or completes sign-in.
    /// </summary>
    private async Task<IActionResult> SignInOrChallengeTenantUserAsync(AppUser user, string slug)
    {
        if (user.IsTwoFactorEnabled)
        {
            var payload = $"{user.Id}|{slug}|{DateTime.UtcNow.AddMinutes(5):O}";
            var challengeToken = _twoFactorProtector.Protect(payload);
            return AuthRedirects.Redirect(this, $"/{slug}/two-factor-challenge?t={Uri.EscapeDataString(challengeToken)}");
        }

        return await CompleteSignInAsync(user, slug);
    }

    /// <summary>
    /// Builds claims, tracks session, signs in, sends notification and publishes event.
    /// Called after both magic-link verify and 2FA challenge completion.
    /// </summary>
    private async Task<IActionResult> CompleteSignInAsync(AppUser user, string slug)
    {
        var roles = await _userManager.GetRolesAsync(user);

        var roleIds = await _tenantDb.Roles
            .Where(r => roles.Contains(r.Name!))
            .Select(r => r.Id)
            .ToListAsync();

        var permissionKeys = await _tenantDb.RolePermissions
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.Permission.Key)
            .Distinct()
            .ToListAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(AuthClaims.TenantSlug, slug)
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(permissionKeys.Select(p => new Claim(AuthClaims.Permission, p)));

        // Track session
        Guid sessionId = Guid.NewGuid();
        try
        {
            var session = new UserSession
            {
                Id = sessionId,
                UserId = user.Id,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                UserAgent = Request.Headers.UserAgent.ToString(),
                DeviceInfo = ParseDeviceInfo(Request.Headers.UserAgent.ToString()),
                CreatedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(12)
            };
            _tenantDb.Set<UserSession>().Add(session);
            await _tenantDb.SaveChangesAsync();
        }
        catch { /* Don't block login if session tracking fails */ }

        claims.Add(new Claim(AuthClaims.SessionId, sessionId.ToString()));

        var identity = new ClaimsIdentity(claims, AuthSchemes.Tenant);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(AuthSchemes.Tenant, principal);

        // Send login notification
        try
        {
            var device = ParseDeviceInfo(Request.Headers.UserAgent.ToString());
            await _notifications.SendAsync(user.Id, "Sign-in detected",
                $"You signed in from {device}",
                $"/{slug}/Session");
        }
        catch { /* Don't block login if notification fails */ }

        // Publish domain event
        try
        {
            await _publishEndpoint.Publish(new UserLoggedInEvent(
                UserId: user.Id,
                Email: user.Email ?? string.Empty,
                TenantId: _tenantContext.TenantId ?? Guid.Empty,
                Slug: slug,
                LoggedInAtUtc: DateTime.UtcNow));
        }
        catch { /* Don't block login if event publish fails */ }

        return AuthRedirects.Redirect(this, $"/{slug}");
    }

    private static string ParseDeviceInfo(string userAgent)
    {
        if (string.IsNullOrEmpty(userAgent)) return "Unknown";
        
        var browser = "Unknown Browser";
        if (userAgent.Contains("Firefox")) browser = "Firefox";
        else if (userAgent.Contains("Edg/")) browser = "Edge";
        else if (userAgent.Contains("Chrome")) browser = "Chrome";
        else if (userAgent.Contains("Safari")) browser = "Safari";

        var os = "Unknown OS";
        if (userAgent.Contains("Windows")) os = "Windows";
        else if (userAgent.Contains("Mac OS")) os = "macOS";
        else if (userAgent.Contains("Linux")) os = "Linux";
        else if (userAgent.Contains("Android")) os = "Android";
        else if (userAgent.Contains("iPhone") || userAgent.Contains("iPad")) os = "iOS";

        return $"{browser} on {os}";
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromRoute] string slug)
    {
        await HttpContext.SignOutAsync(AuthSchemes.Tenant);
        return AuthRedirects.Redirect(this, $"/{slug}/login");
    }

    [HttpGet("logout")]
    public async Task<IActionResult> LogoutGet([FromRoute] string slug)
    {
        await HttpContext.SignOutAsync(AuthSchemes.Tenant);
        return AuthRedirects.Redirect(this, $"/{slug}/login");
    }
}
