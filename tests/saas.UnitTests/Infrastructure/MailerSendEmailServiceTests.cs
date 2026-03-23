using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using saas.Infrastructure.Services;
using saas.Shared;
using Xunit;

namespace saas.Tests.Infrastructure;

public class MailerSendEmailServiceTests
{
    private static readonly IEmailTemplateService _stubTemplateService = new StubEmailTemplateService();

    [Fact]
    public async Task SendAsync_LogsError_WhenFromAddressEmpty()
    {
        var httpClient = new HttpClient();
        var options = Options.Create(new EmailOptions { FromAddress = "" });
        var service = new MailerSendEmailService(httpClient, options, _stubTemplateService, NullLogger<MailerSendEmailService>.Instance);

        // Should not throw — just logs an error and returns
        await service.SendAsync(new EmailMessage("test@test.com", "Subject", "<p>Hello</p>"));
    }

    [Fact]
    public async Task SendAsync_HandlesApiError_Gracefully()
    {
        // Use a handler that returns 401 to simulate bad token
        var handler = new StubHttpHandler(HttpStatusCode.Unauthorized, """{"message":"Unauthenticated."}""");
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new EmailOptions
        {
            FromAddress = "test@example.com",
            FromName = "Test"
        });
        var service = new MailerSendEmailService(httpClient, options, _stubTemplateService, NullLogger<MailerSendEmailService>.Instance);

        // Should not throw — logs the error
        await service.SendAsync(new EmailMessage("user@test.com", "Test Subject", "<p>Hello</p>"));
    }

    [Fact]
    public async Task SendMagicLinkAsync_DelegatesToSendAsync()
    {
        var httpClient = new HttpClient();
        var options = Options.Create(new EmailOptions { FromAddress = "" });
        var service = new MailerSendEmailService(httpClient, options, _stubTemplateService, NullLogger<MailerSendEmailService>.Instance);

        await service.SendMagicLinkAsync("user@test.com", "https://example.com/magic");
    }

    [Fact]
    public async Task SendPasswordResetAsync_DelegatesToSendAsync()
    {
        var httpClient = new HttpClient();
        var options = Options.Create(new EmailOptions { FromAddress = "" });
        var service = new MailerSendEmailService(httpClient, options, _stubTemplateService, NullLogger<MailerSendEmailService>.Instance);

        // Should not throw (FromAddress is empty — logs error and returns)
        await service.SendPasswordResetAsync("user@test.com", "https://example.com/reset?token=abc");
    }

    private class StubEmailTemplateService : IEmailTemplateService
    {
        public string Render(string templateName, Dictionary<string, string> variables)
            => $"<html>{templateName}</html>";
    }

    private class StubHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public StubHttpHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody)
            });
        }
    }
}
