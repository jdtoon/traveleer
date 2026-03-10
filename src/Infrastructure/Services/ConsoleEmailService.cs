using saas.Shared;

namespace saas.Infrastructure.Services;

public class ConsoleEmailService : IEmailService
{
    private readonly IEmailTemplateService _templateService;
    private readonly ILogger<ConsoleEmailService> _logger;

    public ConsoleEmailService(IEmailTemplateService templateService, ILogger<ConsoleEmailService> logger)
    {
        _templateService = templateService;
        _logger = logger;
    }

    public Task<EmailSendResult> SendAsync(EmailMessage message)
    {
        _logger.LogInformation(
            "[EMAIL] To={To} Subject={Subject} Attachments={Attachments}\n{Body}",
            message.To,
            message.Subject,
            string.Join(", ", message.Attachments?.Select(x => x.FileName) ?? []),
            message.HtmlBody);
        return Task.FromResult(EmailSendResult.Succeeded());
    }

    public Task<EmailSendResult> SendMagicLinkAsync(string to, string magicLinkUrl)
    {
        var htmlBody = _templateService.Render("MagicLink", new Dictionary<string, string>
        {
            ["MagicLinkUrl"] = magicLinkUrl
        });

        _logger.LogInformation("\n================ MAGIC LINK ================\nTo: {To}\nUrl: {Url}\n===========================================\n", to, magicLinkUrl);

        return SendAsync(new EmailMessage(
            To: to,
            Subject: "Your magic link",
            HtmlBody: htmlBody,
            PlainTextBody: $"Sign in using this link: {magicLinkUrl}"));
    }
}
