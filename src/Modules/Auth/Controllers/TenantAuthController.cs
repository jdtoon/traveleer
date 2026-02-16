using System.Security.Claims;
using System.Threading.RateLimiting;
using MassTransit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
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

    public TenantAuthController(
        MagicLinkService magicLinks, 
        IEmailService email,
        IBotProtection botProtection,
        UserManager<AppUser> userManager,
        TenantDbContext tenantDb,
        INotificationService notifications,
        IPublishEndpoint publishEndpoint,
        ITenantContext tenantContext)
    {
        _magicLinks = magicLinks;
        _email = email;
        _botProtection = botProtection;
        _userManager = userManager;
        _tenantDb = tenantDb;
        _notifications = notifications;
        _publishEndpoint = publishEndpoint;
        _tenantContext = tenantContext;
    }

    [HttpGet("login")]
    public IActionResult Login([FromRoute] string slug)
    {
        ViewData["TenantSlug"] = slug;
        return SwapView("TenantLogin");
    }

    [HttpPost("login")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> LoginPost([FromRoute] string slug, [FromForm] string email, [FromForm] string? captchaToken)
    {
        if (!await _botProtection.ValidateAsync(captchaToken))
        {
            ViewData["TenantSlug"] = slug;
            return SwapView("TenantLogin", model: "Bot verification failed. Please try again.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            ViewData["TenantSlug"] = slug;
            return SwapView("TenantLogin", model: "Email is required.");
        }

        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || !user.IsActive)
        {
            // Always show the same message to prevent email enumeration
            return SwapView("MagicLinkSent");
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

        if (!user.IsActive)
            return SwapView("MagicLinkError", "This account has been deactivated. Please contact your administrator.");

        var roles = await _userManager.GetRolesAsync(user);

        // Load role IDs for the user's roles
        var roleIds = await _tenantDb.Roles
            .Where(r => roles.Contains(r.Name!))
            .Select(r => r.Id)
            .ToListAsync();

        // Load permissions for those roles
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

        // Check if 2FA is enabled — redirect to 2FA challenge instead of completing login
        if (user.IsTwoFactorEnabled)
        {
            // Store pending login details in TempData for 2FA verification
            TempData["Pending2FA_UserId"] = user.Id;
            TempData["Pending2FA_Slug"] = slug;
            return Redirect($"/{slug}/profile/two-factor");
        }

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

        // Add session ID to claims so middleware can validate it
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

        return Redirect($"/{slug}");
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
        return Redirect($"/{slug}/login");
    }

    [HttpGet("logout")]
    public async Task<IActionResult> LogoutGet([FromRoute] string slug)
    {
        await HttpContext.SignOutAsync(AuthSchemes.Tenant);
        return Redirect($"/{slug}/login");
    }
}
