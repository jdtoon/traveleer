using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Options;
using saas.Shared;

namespace saas.Infrastructure.Services;

public class SesEmailService : IEmailService
{
    private readonly IAmazonSimpleEmailServiceV2 _ses;
    private readonly EmailOptions _options;
    private readonly ILogger<SesEmailService> _logger;

    public SesEmailService(
        IAmazonSimpleEmailServiceV2 ses,
        IOptions<EmailOptions> options,
        ILogger<SesEmailService> logger)
    {
        _ses = ses;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message)
    {
        if (string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            _logger.LogError("SES email failed: FromAddress is not configured");
            return;
        }

        var request = new SendEmailRequest
        {
            FromEmailAddress = _options.FromAddress,
            Destination = new Destination
            {
                ToAddresses = new List<string> { message.To }
            },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = message.Subject },
                    Body = new Body
                    {
                        Html = new Content { Data = message.HtmlBody },
                        Text = string.IsNullOrWhiteSpace(message.PlainTextBody)
                            ? null
                            : new Content { Data = message.PlainTextBody }
                    }
                }
            }
        };

        try
        {
            await _ses.SendEmailAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SES email failed to send to {To}", message.To);
        }
    }

    public Task SendMagicLinkAsync(string to, string magicLinkUrl)
    {
        var htmlBody = $"""
            <p>Use this magic link to sign in:</p>
            <p><a href=\"{magicLinkUrl}\">Sign in</a></p>
            <p>If you did not request this, you can ignore this email.</p>
            """;

        var plainText = $"Sign in using this link: {magicLinkUrl}";

        return SendAsync(new EmailMessage(
            To: to,
            Subject: "Your magic link",
            HtmlBody: htmlBody,
            PlainTextBody: plainText));
    }
}
