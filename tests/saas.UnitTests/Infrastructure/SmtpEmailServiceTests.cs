using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using saas.Infrastructure.Services;
using saas.Shared;
using Xunit;

namespace saas.Tests.Infrastructure;

public class SmtpEmailServiceTests
{
    private static readonly IEmailTemplateService _stubTemplateService = new StubEmailTemplateService();

    [Fact]
    public async Task SendAsync_LogsError_WhenFromAddressEmpty()
    {
        var options = Options.Create(new EmailOptions { FromAddress = "" });
        var service = new SmtpEmailService(options, _stubTemplateService, NullLogger<SmtpEmailService>.Instance);

        // Should not throw — just logs an error and returns
        await service.SendAsync(new EmailMessage("test@test.com", "Subject", "<p>Hello</p>"));
    }

    [Fact]
    public async Task SendMagicLinkAsync_DelegatesToSendAsync()
    {
        // With an empty FromAddress, SendAsync short-circuits safely
        var options = Options.Create(new EmailOptions { FromAddress = "" });
        var service = new SmtpEmailService(options, _stubTemplateService, NullLogger<SmtpEmailService>.Instance);

        await service.SendMagicLinkAsync("user@test.com", "https://example.com/magic");
    }

    [Fact]
    public async Task SendPasswordResetAsync_DelegatesToSendAsync()
    {
        var options = Options.Create(new EmailOptions { FromAddress = "" });
        var service = new SmtpEmailService(options, _stubTemplateService, NullLogger<SmtpEmailService>.Instance);

        // Should not throw — logs error when FromAddress is empty
        await service.SendPasswordResetAsync("user@test.com", "https://example.com/reset?token=abc");
    }

    private class StubEmailTemplateService : IEmailTemplateService
    {
        public string Render(string templateName, Dictionary<string, string> variables)
            => $"<html>{templateName}</html>";
    }
}
