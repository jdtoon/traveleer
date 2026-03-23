using Microsoft.AspNetCore.Identity;
using Xunit;
using SuperAdminEntity = saas.Modules.SuperAdmin.Entities.SuperAdmin;

namespace saas.Tests.Modules.Auth;

public class SuperAdminPasswordTests
{
    private readonly IPasswordHasher<SuperAdminEntity> _hasher = new PasswordHasher<SuperAdminEntity>();

    [Fact]
    public void HashPassword_ProducesVerifiableHash()
    {
        var admin = new SuperAdminEntity { Email = "admin@test.com" };
        var hash = _hasher.HashPassword(admin, "MyPassword1!");

        Assert.NotNull(hash);
        Assert.NotEmpty(hash);

        var result = _hasher.VerifyHashedPassword(admin, hash, "MyPassword1!");
        Assert.Equal(PasswordVerificationResult.Success, result);
    }

    [Fact]
    public void VerifyHashedPassword_Fails_WithWrongPassword()
    {
        var admin = new SuperAdminEntity { Email = "admin@test.com" };
        var hash = _hasher.HashPassword(admin, "CorrectPassword");

        var result = _hasher.VerifyHashedPassword(admin, hash, "WrongPassword");
        Assert.Equal(PasswordVerificationResult.Failed, result);
    }

    [Fact]
    public void SuperAdmin_PasswordHash_IsNullByDefault()
    {
        var admin = new SuperAdminEntity();
        Assert.Null(admin.PasswordHash);
        Assert.Null(admin.PasswordResetToken);
        Assert.Null(admin.PasswordResetTokenExpiry);
    }

    [Fact]
    public void SuperAdmin_CanSetAndClearPasswordResetToken()
    {
        var admin = new SuperAdminEntity
        {
            PasswordResetToken = "some-hash",
            PasswordResetTokenExpiry = DateTime.UtcNow.AddHours(1)
        };

        Assert.NotNull(admin.PasswordResetToken);
        Assert.NotNull(admin.PasswordResetTokenExpiry);

        admin.PasswordResetToken = null;
        admin.PasswordResetTokenExpiry = null;

        Assert.Null(admin.PasswordResetToken);
        Assert.Null(admin.PasswordResetTokenExpiry);
    }

    [Fact]
    public void SuperAdmin_PasswordResetToken_Expires()
    {
        var admin = new SuperAdminEntity
        {
            PasswordResetToken = "some-hash",
            PasswordResetTokenExpiry = DateTime.UtcNow.AddHours(-1) // already expired
        };

        // Simulates what the controller checks before accepting a reset
        var isExpired = admin.PasswordResetTokenExpiry < DateTime.UtcNow;
        Assert.True(isExpired);
    }

    [Fact]
    public void PasswordResetTokenHash_IsConsistentForSameInput()
    {
        var rawToken = "my-raw-reset-token";
        var hash1 = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(rawToken)));
        var hash2 = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(rawToken)));

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void PasswordResetTokenHash_IsDifferentForDifferentInputs()
    {
        var hash1 = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("token-a")));
        var hash2 = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("token-b")));

        Assert.NotEqual(hash1, hash2);
    }
}
