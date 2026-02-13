using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using saas.Modules.Auth.Entities;

namespace saas.Modules.Auth.Services;

public class EmailVerificationService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly Shared.IEmailService _emailService;
    private readonly ILogger<EmailVerificationService> _logger;

    public EmailVerificationService(
        UserManager<AppUser> userManager,
        Shared.IEmailService emailService,
        ILogger<EmailVerificationService> logger)
    {
        _userManager = userManager;
        _emailService = emailService;
        _logger = logger;
    }

    /// <summary>
    /// Generate a verification token and send it via email.
    /// </summary>
    public async Task SendVerificationEmailAsync(AppUser user, string slug, string baseUrl)
    {
        var token = GenerateToken();
        user.EmailVerificationToken = token;
        await _userManager.UpdateAsync(user);

        var verifyUrl = $"{baseUrl.TrimEnd('/')}/{slug}/profile/verify-email?token={token}&userId={user.Id}";

        await _emailService.SendEmailAsync(
            user.Email!,
            "Verify your email address",
            $"<p>Click the link below to verify your email address:</p>" +
            $"<p><a href=\"{verifyUrl}\">Verify Email</a></p>" +
            $"<p>If you didn't request this, you can ignore this email.</p>");

        _logger.LogInformation("Verification email sent to {Email} for tenant {Slug}", user.Email, slug);
    }

    /// <summary>
    /// Verify the token and mark the user's email as verified.
    /// </summary>
    public async Task<bool> VerifyAsync(string userId, string token)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return false;

        if (user.EmailVerificationToken != token)
        {
            _logger.LogWarning("Invalid email verification token for user {UserId}", userId);
            return false;
        }

        user.IsEmailVerified = true;
        user.EmailVerifiedAt = DateTime.UtcNow;
        user.EmailVerificationToken = null;
        user.EmailConfirmed = true; // ASP.NET Identity's built-in flag
        await _userManager.UpdateAsync(user);

        _logger.LogInformation("Email verified for user {UserId} ({Email})", userId, user.Email);
        return true;
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
}
