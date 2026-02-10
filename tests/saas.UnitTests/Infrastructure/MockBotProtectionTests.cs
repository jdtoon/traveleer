using Microsoft.Extensions.Logging.Abstractions;
using saas.Infrastructure.Services;
using Xunit;

namespace saas.Tests.Infrastructure;

public class MockBotProtectionTests
{
    [Fact]
    public async Task ValidateAsync_ReturnsTrue()
    {
        var service = new MockBotProtection(NullLogger<MockBotProtection>.Instance);
        var result = await service.ValidateAsync(null);
        Assert.True(result);
    }
}
