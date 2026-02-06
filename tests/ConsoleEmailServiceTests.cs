using Microsoft.Extensions.Logging.Abstractions;
using saas.Infrastructure.Services;
using saas.Shared;
using Xunit;

namespace saas.Tests;

public class ConsoleEmailServiceTests
{
    [Fact]
    public async Task SendAsync_DoesNotThrow()
    {
        var service = new ConsoleEmailService(NullLogger<ConsoleEmailService>.Instance);
        await service.SendAsync(new EmailMessage("test@test.com", "Subject", "<p>Hello</p>"));
    }
}
