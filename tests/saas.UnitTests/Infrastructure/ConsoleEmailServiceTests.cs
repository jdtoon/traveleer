using Microsoft.Extensions.Logging.Abstractions;
using saas.Infrastructure.Services;
using saas.Shared;
using Xunit;

namespace saas.Tests.Infrastructure;

public class ConsoleEmailServiceTests
{
    [Fact]
    public async Task SendAsync_DoesNotThrow()
    {
        var templateService = new FakeEmailTemplateService();
        var service = new ConsoleEmailService(templateService, NullLogger<ConsoleEmailService>.Instance);
        await service.SendAsync(new EmailMessage("test@test.com", "Subject", "<p>Hello</p>"));
    }

    private class FakeEmailTemplateService : IEmailTemplateService
    {
        public string Render(string templateName, Dictionary<string, string> variables)
            => $"<html>{templateName}</html>";
    }
}
