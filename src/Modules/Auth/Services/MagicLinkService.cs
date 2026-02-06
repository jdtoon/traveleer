using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;

namespace saas.Modules.Auth.Services;

public class MagicLinkService
{
    private readonly CoreDbContext _db;
    private readonly IConfiguration _config;

    public MagicLinkService(CoreDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<MagicLinkToken> GenerateTokenAsync(string email, string? tenantSlug = null)
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        var hash = HashToken(rawToken);
        var expiresAt = DateTime.UtcNow.AddMinutes(_config.GetValue<int?>("MagicLinks:ExpiryMinutes") ?? 15);

        var token = new MagicLinkToken
        {
            Id = Guid.NewGuid(),
            Token = hash,
            Email = email,
            TenantSlug = tenantSlug,
            ExpiresAt = expiresAt,
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.MagicLinkTokens.Add(token);
        await _db.SaveChangesAsync();

        return new MagicLinkToken
        {
            Id = token.Id,
            Token = rawToken,
            Email = email,
            TenantSlug = tenantSlug,
            ExpiresAt = expiresAt,
            IsUsed = false,
            CreatedAt = token.CreatedAt
        };
    }

    public async Task<MagicLinkVerifyResult> VerifyTokenAsync(string rawToken)
    {
        var hash = HashToken(rawToken);
        var token = await _db.MagicLinkTokens.FirstOrDefaultAsync(t => t.Token == hash);

        if (token is null)
            return MagicLinkVerifyResult.Fail("Invalid token");

        if (token.IsUsed)
            return MagicLinkVerifyResult.Fail("Token already used");

        if (token.ExpiresAt < DateTime.UtcNow)
            return MagicLinkVerifyResult.Fail("Token expired");

        token.IsUsed = true;
        token.UsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return MagicLinkVerifyResult.Ok(token.Email, token.TenantSlug);
    }

    public async Task<int> CleanupExpiredAsync()
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var tokens = await _db.MagicLinkTokens.Where(t => t.CreatedAt < cutoff).ToListAsync();
        _db.MagicLinkTokens.RemoveRange(tokens);
        return await _db.SaveChangesAsync();
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}

public record MagicLinkVerifyResult(bool Success, string? Email = null, string? TenantSlug = null, string? Error = null)
{
    public static MagicLinkVerifyResult Fail(string error) => new(false, null, null, error);
    public static MagicLinkVerifyResult Ok(string email, string? tenantSlug) => new(true, email, tenantSlug, null);
}
