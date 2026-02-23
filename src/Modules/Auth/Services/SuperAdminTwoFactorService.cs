using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using saas.Data.Core;

namespace saas.Modules.Auth.Services;

/// <summary>
/// TOTP-based 2FA for SuperAdmin accounts. Works directly with CoreDbContext
/// (SuperAdmin entities are not Identity-based).
/// </summary>
public class SuperAdminTwoFactorService
{
    private readonly CoreDbContext _coreDb;
    private readonly ILogger<SuperAdminTwoFactorService> _logger;
    private const int RecoveryCodeCount = 8;

    public SuperAdminTwoFactorService(CoreDbContext coreDb, ILogger<SuperAdminTwoFactorService> logger)
    {
        _coreDb = coreDb;
        _logger = logger;
    }

    /// <summary>
    /// Generate a new TOTP secret for setup. Does not enable 2FA yet.
    /// </summary>
    public async Task<TwoFactorSetupResult> GenerateSetupAsync(Modules.SuperAdmin.Entities.SuperAdmin admin, string issuer)
    {
        var secret = KeyGeneration.GenerateRandomKey(20);
        var base32Secret = Base32Encoding.ToString(secret);

        admin.TwoFactorSecret = base32Secret;
        await _coreDb.SaveChangesAsync();

        var otpUri = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(admin.Email)}?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}&digits=6&period=30";

        return new TwoFactorSetupResult(base32Secret, otpUri);
    }

    /// <summary>
    /// Verify a TOTP code and enable 2FA if valid.
    /// </summary>
    public async Task<TwoFactorEnableResult> VerifyAndEnableAsync(Modules.SuperAdmin.Entities.SuperAdmin admin, string code)
    {
        if (string.IsNullOrWhiteSpace(admin.TwoFactorSecret))
            return new TwoFactorEnableResult(false, "No 2FA setup in progress.", null);

        if (!ValidateCode(admin.TwoFactorSecret, code))
            return new TwoFactorEnableResult(false, "Invalid code. Please try again.", null);

        var recoveryCodes = GenerateRecoveryCodes();
        admin.TwoFactorRecoveryCodes = string.Join(";", recoveryCodes);
        admin.IsTwoFactorEnabled = true;
        await _coreDb.SaveChangesAsync();

        _logger.LogInformation("2FA enabled for super admin {AdminId}", admin.Id);

        return new TwoFactorEnableResult(true, null, recoveryCodes);
    }

    /// <summary>
    /// Validate a TOTP code.
    /// </summary>
    public bool ValidateCode(string base32Secret, string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        var secret = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(secret, step: 30, totpSize: 6);

        return totp.VerifyTotp(code.Trim(), out _, new VerificationWindow(previous: 1, future: 1));
    }

    /// <summary>
    /// Validate and consume a recovery code.
    /// </summary>
    public async Task<bool> ValidateRecoveryCodeAsync(Modules.SuperAdmin.Entities.SuperAdmin admin, string code)
    {
        if (string.IsNullOrWhiteSpace(admin.TwoFactorRecoveryCodes)) return false;

        var codes = admin.TwoFactorRecoveryCodes.Split(';').ToList();
        var normalizedCode = code.Trim().ToUpperInvariant();

        if (!codes.Contains(normalizedCode)) return false;

        codes.Remove(normalizedCode);
        admin.TwoFactorRecoveryCodes = string.Join(";", codes);
        await _coreDb.SaveChangesAsync();

        _logger.LogInformation("Recovery code used for super admin {AdminId}. {Remaining} codes remaining.", admin.Id, codes.Count);
        return true;
    }

    /// <summary>
    /// Disable 2FA for a super admin.
    /// </summary>
    public async Task DisableAsync(Modules.SuperAdmin.Entities.SuperAdmin admin)
    {
        admin.IsTwoFactorEnabled = false;
        admin.TwoFactorSecret = null;
        admin.TwoFactorRecoveryCodes = null;
        await _coreDb.SaveChangesAsync();

        _logger.LogInformation("2FA disabled for super admin {AdminId}", admin.Id);
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
