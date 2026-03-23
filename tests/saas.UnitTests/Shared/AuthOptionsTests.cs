using Microsoft.AspNetCore.Identity;
using saas.Shared;
using Xunit;

namespace saas.Tests.Shared;

public class AuthOptionsTests
{
    [Fact]
    public void IsPasswordLogin_ReturnsTrue_WhenLoginMethodIsPassword()
    {
        var opts = new AuthOptions { LoginMethod = "Password" };
        Assert.True(opts.IsPasswordLogin);
        Assert.False(opts.IsMagicLinkLogin);
    }

    [Fact]
    public void IsMagicLinkLogin_ReturnsTrue_WhenLoginMethodIsMagicLink()
    {
        var opts = new AuthOptions { LoginMethod = "MagicLink" };
        Assert.True(opts.IsMagicLinkLogin);
        Assert.False(opts.IsPasswordLogin);
    }

    [Fact]
    public void IsPasswordLogin_IsCaseInsensitive()
    {
        Assert.True(new AuthOptions { LoginMethod = "password" }.IsPasswordLogin);
        Assert.True(new AuthOptions { LoginMethod = "PASSWORD" }.IsPasswordLogin);
        Assert.True(new AuthOptions { LoginMethod = "magiclink" }.IsMagicLinkLogin);
        Assert.True(new AuthOptions { LoginMethod = "MAGICLINK" }.IsMagicLinkLogin);
    }

    [Fact]
    public void DefaultLoginMethod_IsPassword()
    {
        var opts = new AuthOptions();
        Assert.Equal("Password", opts.LoginMethod);
        Assert.True(opts.IsPasswordLogin);
    }

    [Fact]
    public void BothFlags_AreFalse_ForUnknownLoginMethod()
    {
        var opts = new AuthOptions { LoginMethod = "OAuth" };
        Assert.False(opts.IsPasswordLogin);
        Assert.False(opts.IsMagicLinkLogin);
    }
}
