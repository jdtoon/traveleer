using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using saas.Infrastructure.Services;
using saas.Shared;
using Xunit;

namespace saas.Tests.Infrastructure;

public class TurnstileBotProtectionTests
{
    [Fact]
    public async Task ValidateAsync_SuccessResponse_ReturnsTrue()
    {
        var handler = new StubHttpHandler("{" + "\"success\":true" + "}");
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new TurnstileOptions
        {
            Provider = "Cloudflare",
            SiteKey = "site",
            SecretKey = "secret"
        });

        var service = new TurnstileBotProtection(httpClient, options,
            NullLogger<TurnstileBotProtection>.Instance);

        var result = await service.ValidateAsync("token");

        Assert.True(result);
    }

    [Fact]
    public async Task ValidateAsync_MissingToken_ReturnsFalse()
    {
        var handler = new StubHttpHandler("{" + "\"success\":true" + "}");
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new TurnstileOptions
        {
            Provider = "Cloudflare",
            SiteKey = "site",
            SecretKey = "secret"
        });

        var service = new TurnstileBotProtection(httpClient, options,
            NullLogger<TurnstileBotProtection>.Instance);

        var result = await service.ValidateAsync(null);

        Assert.False(result);
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly string _body;

        public StubHttpHandler(string body)
        {
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
