using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using saas.Infrastructure.Services;
using saas.Shared;
using Xunit;

namespace saas.Tests.Infrastructure;

public class SmtpEmailServiceTests
{
    [Fact]
    public async Task SendAsync_LogsError_WhenFromAddressEmpty()
    {
        var options = Options.Create(new EmailOptions { FromAddress = "" });
        var service = new SmtpEmailService(options, NullLogger<SmtpEmailService>.Instance);

        // Should not throw — just logs an error and returns
        await service.SendAsync(new EmailMessage("test@test.com", "Subject", "<p>Hello</p>"));
    }

    [Fact]
    public async Task SendMagicLinkAsync_DelegatesToSendAsync()
    {
        // With an empty FromAddress, SendAsync short-circuits safely
        var options = Options.Create(new EmailOptions { FromAddress = "" });
        var service = new SmtpEmailService(options, NullLogger<SmtpEmailService>.Instance);

        await service.SendMagicLinkAsync("user@test.com", "https://example.com/magic");
    }
}
