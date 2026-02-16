using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using saas.Data.Tenant;
using saas.Modules.Auth.Entities;
using saas.Modules.Auth.Services;
using OtpNet;
using Xunit;

namespace saas.Tests.Modules.Auth;

public class TwoFactorServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _serviceProvider = null!;
    private TwoFactorService _service = null!;
    private UserManager<AppUser> _userManager = null!;
    private AppUser _testUser = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

        services.AddDbContext<TenantDbContext>(opts =>
            opts.UseSqlite(_connection));

        services.AddIdentityCore<AppUser>(opts =>
        {
            opts.User.RequireUniqueEmail = true;
        })
        .AddRoles<AppRole>()
        .AddEntityFrameworkStores<TenantDbContext>();

        _serviceProvider = services.BuildServiceProvider();

        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        var mainScope = _serviceProvider.CreateScope();
        _userManager = mainScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var logger = mainScope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger<TwoFactorService>();
        _service = new TwoFactorService(_userManager, logger);

        // Create test user
        _testUser = new AppUser
        {
            UserName = "test@example.com",
            Email = "test@example.com",
            DisplayName = "Test User",
            IsActive = true,
            EmailConfirmed = true
        };
        await _userManager.CreateAsync(_testUser);
    }

    public async Task DisposeAsync()
    {
        _serviceProvider?.Dispose();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task GenerateSetupAsync_ReturnsSecretAndOtpUri()
    {
        var result = await _service.GenerateSetupAsync(_testUser, "TestApp");

        Assert.NotNull(result.Secret);
        Assert.NotEmpty(result.Secret);
        Assert.Contains("otpauth://totp/", result.OtpUri);
        Assert.Contains("TestApp", result.OtpUri);
        Assert.Contains(Uri.EscapeDataString("test@example.com"), result.OtpUri);
    }

    [Fact]
    public async Task GenerateSetupAsync_StoresSecretOnUser()
    {
        await _service.GenerateSetupAsync(_testUser, "TestApp");

        var updatedUser = await _userManager.FindByIdAsync(_testUser.Id);
        Assert.NotNull(updatedUser!.TwoFactorSecret);
    }

    [Fact]
    public async Task VerifyAndEnableAsync_WithValidCode_EnablesTwoFactor()
    {
        var setup = await _service.GenerateSetupAsync(_testUser, "TestApp");

        // Generate a valid TOTP code using the secret
        var secret = Base32Encoding.ToBytes(setup.Secret);
        var totp = new Totp(secret, step: 30, totpSize: 6);
        var code = totp.ComputeTotp();

        var result = await _service.VerifyAndEnableAsync(_testUser, code);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.NotNull(result.RecoveryCodes);
        Assert.Equal(8, result.RecoveryCodes!.Count);
    }

    [Fact]
    public async Task VerifyAndEnableAsync_WithInvalidCode_Fails()
    {
        await _service.GenerateSetupAsync(_testUser, "TestApp");

        var result = await _service.VerifyAndEnableAsync(_testUser, "000000");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Null(result.RecoveryCodes);
    }

    [Fact]
    public async Task VerifyAndEnableAsync_WithNoSetup_Fails()
    {
        var result = await _service.VerifyAndEnableAsync(_testUser, "123456");

        Assert.False(result.Success);
    }

    [Fact]
    public void ValidateCode_WithCorrectCode_ReturnsTrue()
    {
        var secret = KeyGeneration.GenerateRandomKey(20);
        var base32 = Base32Encoding.ToString(secret);
        var totp = new Totp(secret, step: 30, totpSize: 6);
        var code = totp.ComputeTotp();

        Assert.True(_service.ValidateCode(base32, code));
    }

    [Fact]
    public void ValidateCode_WithWrongCode_ReturnsFalse()
    {
        var secret = KeyGeneration.GenerateRandomKey(20);
        var base32 = Base32Encoding.ToString(secret);

        Assert.False(_service.ValidateCode(base32, "000000"));
    }

    [Fact]
    public void ValidateCode_WithNullCode_ReturnsFalse()
    {
        var secret = KeyGeneration.GenerateRandomKey(20);
        var base32 = Base32Encoding.ToString(secret);

        Assert.False(_service.ValidateCode(base32, null!));
        Assert.False(_service.ValidateCode(base32, ""));
        Assert.False(_service.ValidateCode(base32, "  "));
    }

    [Fact]
    public async Task ValidateRecoveryCodeAsync_ConsumesCode()
    {
        var setup = await _service.GenerateSetupAsync(_testUser, "TestApp");
        var secret = Base32Encoding.ToBytes(setup.Secret);
        var totp = new Totp(secret, step: 30, totpSize: 6);
        var enableResult = await _service.VerifyAndEnableAsync(_testUser, totp.ComputeTotp());

        var code = enableResult.RecoveryCodes![0];
        var result = await _service.ValidateRecoveryCodeAsync(_testUser, code);

        Assert.True(result);

        // Same code should not work again
        var result2 = await _service.ValidateRecoveryCodeAsync(_testUser, code);
        Assert.False(result2);
    }

    [Fact]
    public async Task DisableAsync_ClearsAllTwoFactorData()
    {
        var setup = await _service.GenerateSetupAsync(_testUser, "TestApp");
        var secret = Base32Encoding.ToBytes(setup.Secret);
        var totp = new Totp(secret, step: 30, totpSize: 6);
        await _service.VerifyAndEnableAsync(_testUser, totp.ComputeTotp());

        await _service.DisableAsync(_testUser);

        var updatedUser = await _userManager.FindByIdAsync(_testUser.Id);
        Assert.False(updatedUser!.IsTwoFactorEnabled);
        Assert.Null(updatedUser.TwoFactorSecret);
        Assert.Null(updatedUser.TwoFactorRecoveryCodes);
    }
}
