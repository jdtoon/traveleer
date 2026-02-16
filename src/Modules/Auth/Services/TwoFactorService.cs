using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using OtpNet;
using saas.Modules.Auth.Entities;

namespace saas.Modules.Auth.Services;

public class TwoFactorService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly ILogger<TwoFactorService> _logger;
    private const int RecoveryCodeCount = 8;

    public TwoFactorService(UserManager<AppUser> userManager, ILogger<TwoFactorService> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Generate a new TOTP secret for setup. Does not enable 2FA yet.
    /// </summary>
    public async Task<TwoFactorSetupResult> GenerateSetupAsync(AppUser user, string issuer)
    {
        var secret = KeyGeneration.GenerateRandomKey(20);
        var base32Secret = Base32Encoding.ToString(secret);

        // Store the secret (not yet enabled until verified)
        user.TwoFactorSecret = base32Secret;
        await _userManager.UpdateAsync(user);

        var otpUri = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(user.Email!)}?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}&digits=6&period=30";

        return new TwoFactorSetupResult(base32Secret, otpUri);
    }

    /// <summary>
    /// Verify a TOTP code and enable 2FA if valid.
    /// </summary>
    public async Task<TwoFactorEnableResult> VerifyAndEnableAsync(AppUser user, string code)
    {
        if (string.IsNullOrWhiteSpace(user.TwoFactorSecret))
            return new TwoFactorEnableResult(false, "No 2FA setup in progress.", null);

        if (!ValidateCode(user.TwoFactorSecret, code))
            return new TwoFactorEnableResult(false, "Invalid code. Please try again.", null);

        // Generate recovery codes
        var recoveryCodes = GenerateRecoveryCodes();
        user.TwoFactorRecoveryCodes = string.Join(";", recoveryCodes);
        user.IsTwoFactorEnabled = true;
        await _userManager.UpdateAsync(user);

        _logger.LogInformation("2FA enabled for user {UserId}", user.Id);

        return new TwoFactorEnableResult(true, null, recoveryCodes);
    }

    /// <summary>
    /// Validate a TOTP code during login.
    /// </summary>
    public bool ValidateCode(string base32Secret, string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        var secret = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(secret, step: 30, totpSize: 6);

        // Allow 1 step tolerance (30 seconds before/after)
        return totp.VerifyTotp(code.Trim(), out _, new VerificationWindow(previous: 1, future: 1));
    }

    /// <summary>
    /// Validate and consume a recovery code.
    /// </summary>
    public async Task<bool> ValidateRecoveryCodeAsync(AppUser user, string code)
    {
        if (string.IsNullOrWhiteSpace(user.TwoFactorRecoveryCodes)) return false;

        var codes = user.TwoFactorRecoveryCodes.Split(';').ToList();
        var normalizedCode = code.Trim().ToUpperInvariant();

        if (!codes.Contains(normalizedCode)) return false;

        // Consume the code
        codes.Remove(normalizedCode);
        user.TwoFactorRecoveryCodes = string.Join(";", codes);
        await _userManager.UpdateAsync(user);

        _logger.LogInformation("Recovery code used for user {UserId}. {Remaining} codes remaining.", user.Id, codes.Count);
        return true;
    }

    /// <summary>
    /// Disable 2FA for a user.
    /// </summary>
    public async Task DisableAsync(AppUser user)
    {
        user.IsTwoFactorEnabled = false;
        user.TwoFactorSecret = null;
        user.TwoFactorRecoveryCodes = null;
        await _userManager.UpdateAsync(user);

        _logger.LogInformation("2FA disabled for user {UserId}", user.Id);
    }

    private static List<string> GenerateRecoveryCodes()
    {
        var codes = new List<string>();
        for (int i = 0; i < RecoveryCodeCount; i++)
        {
            var bytes = RandomNumberGenerator.GetBytes(5);
            codes.Add(Convert.ToHexString(bytes).ToUpperInvariant());
        }
        return codes;
    }
}

public record TwoFactorSetupResult(string Secret, string OtpUri);
public record TwoFactorEnableResult(bool Success, string? Error, List<string>? RecoveryCodes);
