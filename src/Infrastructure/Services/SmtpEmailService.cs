using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using saas.Shared;

namespace saas.Infrastructure.Services;

public class SmtpEmailService : IEmailService
{
    private readonly EmailOptions _options;
    private readonly IEmailTemplateService _templateService;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(
        IOptions<EmailOptions> options,
        IEmailTemplateService templateService,
        ILogger<SmtpEmailService> logger)
    {
        _options = options.Value;
        _templateService = templateService;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendAsync(EmailMessage message)
    {
        if (string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            _logger.LogError("SMTP email failed: FromAddress is not configured");
            return EmailSendResult.Failed("Email sender address is not configured.");
        }

        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        mimeMessage.To.Add(MailboxAddress.Parse(message.To));
        mimeMessage.Subject = message.Subject;

        var bodyBuilder = new BodyBuilder { HtmlBody = message.HtmlBody };
        if (!string.IsNullOrWhiteSpace(message.PlainTextBody))
            bodyBuilder.TextBody = message.PlainTextBody;

        mimeMessage.Body = bodyBuilder.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            var secureSocketOptions = _options.Smtp.UseSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await client.ConnectAsync(_options.Smtp.Host, _options.Smtp.Port, secureSocketOptions);

            if (!string.IsNullOrWhiteSpace(_options.Smtp.Username))
                await client.AuthenticateAsync(_options.Smtp.Username, _options.Smtp.Password);

            await client.SendAsync(mimeMessage);
            await client.DisconnectAsync(true);
            return EmailSendResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP email failed to send to {To}", message.To);
            return EmailSendResult.Failed(ex.Message);
        }
    }

    public Task<EmailSendResult> SendMagicLinkAsync(string to, string magicLinkUrl)
    {
        var htmlBody = _templateService.Render("MagicLink", new Dictionary<string, string>
        {
            ["MagicLinkUrl"] = magicLinkUrl
        });

        var plainText = $"Sign in using this link: {magicLinkUrl}";

        return SendAsync(new EmailMessage(
            To: to,
            Subject: "Your magic link",
            HtmlBody: htmlBody,
            PlainTextBody: plainText));
    }
}
