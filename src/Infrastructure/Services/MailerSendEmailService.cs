using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Options;
using saas.Shared;

namespace saas.Infrastructure.Services;

public class MailerSendEmailService : IEmailService
{
    private readonly HttpClient _httpClient;
    private readonly EmailOptions _options;
    private readonly IEmailTemplateService _templateService;
    private readonly ILogger<MailerSendEmailService> _logger;

    public MailerSendEmailService(
        HttpClient httpClient,
        IOptions<EmailOptions> options,
        IEmailTemplateService templateService,
        ILogger<MailerSendEmailService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _templateService = templateService;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendAsync(EmailMessage message)
    {
        if (string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            _logger.LogError("MailerSend email failed: FromAddress is not configured");
            return EmailSendResult.Failed("Email sender address is not configured.");
        }

        var payload = new
        {
            from = new { email = _options.FromAddress, name = _options.FromName },
            to = new[] { new { email = message.To } },
            subject = message.Subject,
            html = message.HtmlBody,
            text = message.PlainTextBody ?? string.Empty,
            attachments = (message.Attachments ?? [])
                .Select(attachment => new
                {
                    content = Convert.ToBase64String(attachment.Content),
                    disposition = attachment.Disposition,
                    filename = attachment.FileName,
                    id = attachment.ContentId
                })
                .ToArray()
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("https://api.mailersend.com/v1/email", payload);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError("MailerSend API error {StatusCode}: {Body}", (int)response.StatusCode, body);
                return EmailSendResult.Failed($"MailerSend returned {(int)response.StatusCode}.");
            }

            var providerMessageId = response.Headers.TryGetValues("x-message-id", out var values)
                ? values.FirstOrDefault()
                : null;

            return EmailSendResult.Succeeded(providerMessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MailerSend email failed to send to {To}", message.To);
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

    public Task SendPasswordResetAsync(string to, string resetUrl)
    {
        var htmlBody = _templateService.Render("PasswordReset", new Dictionary<string, string>
        {
            ["ResetUrl"] = resetUrl
        });

        return SendAsync(new EmailMessage(
            To: to,
            Subject: "Reset your password",
            HtmlBody: htmlBody,
            PlainTextBody: $"Reset your password using this link: {resetUrl}"));
    }
}
