using saas.Modules.Billing.Services;
using Xunit;

namespace saas.Tests.Modules.Billing;

public class WebhookSignatureValidatorTests
{
    [Fact]
    public void IsValid_CorrectSignature_ReturnsTrue()
    {
        var secret = "test-webhook-secret";
        var payload = """{"event":"charge.success","data":{"reference":"test123"}}""";

        // Compute expected signature
        var hash = System.Security.Cryptography.HMACSHA512.HashData(
            System.Text.Encoding.UTF8.GetBytes(secret),
            System.Text.Encoding.UTF8.GetBytes(payload)
        );
        var signature = Convert.ToHexStringLower(hash);

        Assert.True(WebhookSignatureValidator.IsValid(payload, signature, secret));
    }

    [Fact]
    public void IsValid_WrongSignature_ReturnsFalse()
    {
        var secret = "test-webhook-secret";
        var payload = """{"event":"charge.success"}""";
        var wrongSignature = "0000000000000000000000000000000000000000";

        Assert.False(WebhookSignatureValidator.IsValid(payload, wrongSignature, secret));
    }

    [Fact]
    public void IsValid_EmptyPayload_ReturnsFalse()
    {
        Assert.False(WebhookSignatureValidator.IsValid("", "somesig", "secret"));
    }

    [Fact]
    public void IsValid_EmptySignature_ReturnsFalse()
    {
        Assert.False(WebhookSignatureValidator.IsValid("payload", "", "secret"));
    }

    [Fact]
    public void IsValid_EmptySecret_ReturnsFalse()
    {
        Assert.False(WebhookSignatureValidator.IsValid("payload", "sig", ""));
    }

    [Fact]
    public void IsValid_TamperedPayload_ReturnsFalse()
    {
        var secret = "test-webhook-secret";
        var originalPayload = """{"event":"charge.success","data":{"amount":5000}}""";
        var tamperedPayload = """{"event":"charge.success","data":{"amount":0}}""";

        // Sign with original payload
        var hash = System.Security.Cryptography.HMACSHA512.HashData(
            System.Text.Encoding.UTF8.GetBytes(secret),
            System.Text.Encoding.UTF8.GetBytes(originalPayload)
        );
        var signature = Convert.ToHexStringLower(hash);

        // Validate with tampered payload
        Assert.False(WebhookSignatureValidator.IsValid(tamperedPayload, signature, secret));
    }
}
