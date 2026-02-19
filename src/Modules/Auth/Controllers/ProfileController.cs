using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using saas.Modules.Auth.Entities;
using saas.Modules.Auth.Services;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Auth.Controllers;

[Route("{slug}/profile")]
[Authorize(Policy = "TenantUser")]
public class ProfileController : SwapController
{
    private readonly UserManager<AppUser> _userManager;
    private readonly ICurrentUser _currentUser;
    private readonly EmailVerificationService _emailVerification;
    private readonly IOptions<SiteSettings> _siteSettings;

    public ProfileController(
        UserManager<AppUser> userManager,
        ICurrentUser currentUser,
        EmailVerificationService emailVerification,
        IOptions<SiteSettings> siteSettings)
    {
        _userManager = userManager;
        _currentUser = currentUser;
        _emailVerification = emailVerification;
        _siteSettings = siteSettings;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromRoute] string slug)
    {
        var user = await _userManager.FindByIdAsync(_currentUser.UserId!);
        if (user is null) return NotFound();

        ViewData["Title"] = "Profile";
        return SwapView(SwapViews.Profile.Index, user);
    }

    [HttpPost("")]
    public async Task<IActionResult> Update(
        [FromRoute] string slug,
        [FromForm] string? displayName,
        [FromForm] string? timeZone)
    {
        var user = await _userManager.FindByIdAsync(_currentUser.UserId!);
        if (user is null) return NotFound();

        user.DisplayName = displayName?.Trim();
        user.TimeZone = timeZone?.Trim();
        user.UpdatedAt = DateTime.UtcNow;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            ViewData["Error"] = string.Join(", ", result.Errors.Select(e => e.Description));
            return SwapView(SwapViews.Profile.Index, user);
        }

        ViewData["Success"] = "Profile updated successfully.";
        return SwapView(SwapViews.Profile.Index, user);
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(
        [FromRoute] string slug,
        [FromForm] string currentPassword,
        [FromForm] string newPassword,
        [FromForm] string confirmPassword)
    {
        var user = await _userManager.FindByIdAsync(_currentUser.UserId!);
        if (user is null) return NotFound();

        if (newPassword != confirmPassword)
        {
            ViewData["PasswordError"] = "Passwords do not match.";
            return SwapView(SwapViews.Profile.Index, user);
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            ViewData["PasswordError"] = "Password must be at least 8 characters.";
            return SwapView(SwapViews.Profile.Index, user);
        }

        // If user has no password (magic-link only), set one
        if (!await _userManager.HasPasswordAsync(user))
        {
            var addResult = await _userManager.AddPasswordAsync(user, newPassword);
            if (!addResult.Succeeded)
            {
                ViewData["PasswordError"] = string.Join(", ", addResult.Errors.Select(e => e.Description));
                return SwapView(SwapViews.Profile.Index, user);
            }
        }
        else
        {
            var changeResult = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
            if (!changeResult.Succeeded)
            {
                ViewData["PasswordError"] = string.Join(", ", changeResult.Errors.Select(e => e.Description));
                return SwapView(SwapViews.Profile.Index, user);
            }
        }

        ViewData["PasswordSuccess"] = "Password updated successfully.";
        return SwapView(SwapViews.Profile.Index, user);
    }

    [HttpPost("send-verification")]
    public async Task<IActionResult> SendVerification([FromRoute] string slug)
    {
        var user = await _userManager.FindByIdAsync(_currentUser.UserId!);
        if (user is null) return NotFound();

        if (user.IsEmailVerified)
        {
            ViewData["Success"] = "Your email is already verified.";
            return SwapView(SwapViews.Profile.Index, user);
        }

        var baseUrl = _siteSettings.Value.BaseUrl;
        await _emailVerification.SendVerificationEmailAsync(user, slug, baseUrl);

        ViewData["Success"] = "Verification email sent. Check your inbox.";
        return SwapView(SwapViews.Profile.Index, user);
    }

    [HttpGet("verify-email")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyEmail([FromRoute] string slug, [FromQuery] string token, [FromQuery] string userId)
    {
        var success = await _emailVerification.VerifyAsync(userId, token);

        if (success)
        {
            ViewData["Success"] = "Your email has been verified!";
        }
        else
        {
            ViewData["Error"] = "Invalid or expired verification link.";
        }

        var user = await _userManager.FindByIdAsync(userId);
        return SwapView(SwapViews.Profile.Index, user ?? new AppUser());
    }
}
